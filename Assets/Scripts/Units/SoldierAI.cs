using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Autonomous garrison troop - the deliberate OPPOSITE of WorkerAI. The
/// Worker's FSM is externally driven (player clicks interrupt it at will);
/// the Soldier's is fully self-directed. It is NOT selectable, takes NO
/// orders, and never touches SelectionManager - the player's lever is
/// WHERE the garrison building stands, not what its troops do.
///
/// Movement while patrolling is coordinated by the GarrisonBuilding (lap
/// waypoints + formation slots via SetPatrolDestination). Two speed tiers:
/// a relaxed patrol pace when near the assigned slot, and a hurry pace for
/// combat and for catching up to a distant slot (fresh spawns joining a
/// moving squad, survivors regrouping after a fight).
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Health))]
public class SoldierAI : MonoBehaviour
{
    private enum State { Patrolling, Attacking }

    // Same pattern as CrabAI.AttackStyle: one script, behavior picked per
    // prefab. Melee = the original soldier; Ranged = the Hunter upgrade.
    private enum CombatStyle { Melee, Ranged }

    [Header("Patrol")]
    [SerializeField] private float scanInterval = 0.4f;

    [Header("Movement Speeds")]
    [Tooltip("Relaxed pace while in formation, near the assigned slot.")]
    [SerializeField] private float patrolSpeed = 2.5f;
    [Tooltip("Pace in combat, and when catching up to a distant formation slot.")]
    [SerializeField] private float hurrySpeed = 3.5f;
    [Tooltip("Farther than this from the assigned slot while patrolling = hurry to catch up.")]
    [SerializeField] private float catchUpDistance = 3f;

    [Header("Combat")]
    [SerializeField] private LayerMask enemyMask;
    [SerializeField] private float aggroRadius = 10f;
    [SerializeField] private int damage = 6;
    [SerializeField] private float attackRange = 1.2f;
    [SerializeField] private float attackCooldown = 0.9f;
    [Tooltip("Max distance from HOME (not from the target) a chase may stray before the soldier disengages. Soldiers defend a place, not a grudge.")]
    [SerializeField] private float chaseLeashDistance = 16f;

    [Tooltip("Buildings on these layers block the soldier's sight - an enemy behind an intact wall is invisible until it breaches or comes around. The Keep never blocks sight (soldiers see across the courtyard).")]
    [SerializeField] private LayerMask sightBlockerMask;

    [Header("Combat Style")]
    [Tooltip("Melee: walk up and hit. Ranged (Hunter): stop, aim, shoot - and kite away when the enemy closes in.")]
    [SerializeField] private CombatStyle combatStyle = CombatStyle.Melee;

    [Header("Ranged (Hunters only - ignored for Melee)")]
    [SerializeField] private GameObject arrowPrefab;
    [Tooltip("Preferred shooting distance. Beyond it the hunter advances; inside kiteDistance it backs off.")]
    [SerializeField] private float shootRange = 7f;
    [SerializeField] private float projectileSpeed = 14f;
    [Tooltip("Seconds of standing still before a shot can loose - moving (advancing, kiting) spoils the aim.")]
    [SerializeField] private float aimDuration = 0.5f;
    [Tooltip("An enemy closer than this makes the hunter step away before shooting again.")]
    [SerializeField] private float kiteDistance = 3f;
    [Tooltip("How far each kite step retreats.")]
    [SerializeField] private float kiteStepDistance = 2.5f;
    [Tooltip("A crowded hunter only kites once every N shots - kiting on every single frame it's crowded made hunters nearly unkillable, since they'd retreat before anything could ever reach them. Between kites they stand their ground and keep shooting point-blank.")]
    [SerializeField] private int kiteEveryShots = 2;
    [Tooltip("Flash color that fades IN over the aim, reaching full brightness right as the arrow looses, then cuts to zero.")]
    [SerializeField] private Color aimFlashColor = Color.white;

    [Header("AoE Slash (optional - Guardian only, ignored otherwise)")]
    [Tooltip("If true, the melee hit becomes a small cone slash in front of the soldier instead of a single-target hit - damage still uses the same 'damage' field above, applied to everyone the cone catches.")]
    [SerializeField] private bool useAoeSlash = false;
    [SerializeField] private float slashRadius = 2.2f;
    [Tooltip("Full cone width in degrees, centered on the soldier's forward - keep this modest, it's meant to catch 'a few enemies right in front', not everything nearby.")]
    [SerializeField] private float slashAngle = 100f;
    [Tooltip("The visual sword swing - assign the 'Sword' child. Leave null to skip the animation entirely (damage/AoE still work fine without it).")]
    [SerializeField] private Transform swordPivot;
    [SerializeField] private float swordSwingAngle = 90f;
    [Tooltip("Very quick on purpose - a snappy slash, not a telegraphed windup like the Brute's slam. Each half (out and back) takes this long.")]
    [SerializeField] private float swordSwingDuration = 0.08f;

    [Header("Stances (driven by the GarrisonBuilding)")]
    [Tooltip("Defend Point multiplies the aggro radius by this - posted guards watch their spot, they don't roam after everything they can see.")]
    [SerializeField] private float defendAggroMultiplier = 0.6f;
    [Tooltip("Defend Point leash: max chase distance from the DEFENDED POINT (not the garrison). Small on purpose - the whole point of the stance is staying put.")]
    [SerializeField] private float defendLeashDistance = 6f;


    [Header("Death")]
    [SerializeField] private float toppleDuration = 0.4f;
    [SerializeField] private float corpseLingerTime = 1f;

    private NavMeshAgent agent;
    private Health selfHealth;
    private HitFlash hitFlash;
    private State state = State.Patrolling;
    private Vector3 homePoint;
    private Vector3 patrolDestination;
    private Health targetHealth;
    private Collider targetCollider;
    private float scanTimer;
    private float attackTimer;
    private float aimTimer;
    private int shotsSinceKite;
    private bool isKiting; // true for the WHOLE retreat, not just the triggering frame
    private Coroutine swordSwingRoutine;

    private GarrisonBuilding.Stance stance = GarrisonBuilding.Stance.Patrol;
    private Vector3 defendAnchor;

    // Every aggro/leash decision below routes through these three so each
    // stance is defined in exactly one place:
    //   Patrol         - normal aggro, leash from HOME, sight rules apply.
    //   SeekAndDestroy - unlimited aggro, NO leash, sees through walls
    //                    (they're hunting, not standing watch).
    //   DefendPoint    - reduced aggro, tight leash from the DEFENDED
    //                    point, sight rules apply.
    private float EffectiveAggroRadius =>
        stance == GarrisonBuilding.Stance.SeekAndDestroy ? 9999f :
        stance == GarrisonBuilding.Stance.DefendPoint ? aggroRadius * defendAggroMultiplier :
        aggroRadius;

    private Vector3 LeashAnchor =>
        stance == GarrisonBuilding.Stance.DefendPoint ? defendAnchor : homePoint;

    private float EffectiveLeashDistance =>
        stance == GarrisonBuilding.Stance.SeekAndDestroy ? float.MaxValue :
        stance == GarrisonBuilding.Stance.DefendPoint ? defendLeashDistance :
        chaseLeashDistance;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        selfHealth = GetComponent<Health>();
        hitFlash = GetComponentInChildren<HitFlash>();
        selfHealth.OnDeath += HandleDeath;

        // Same corridor-traffic lesson as the crabs: equals never yield.
        agent.avoidancePriority = Random.Range(30, 70);
    }

    private void OnDestroy()
    {
        if (selfHealth != null)
            selfHealth.OnDeath -= HandleDeath;
    }

    private void Start()
    {
        // Fallback for soldiers placed by hand (no garrison called Init).
        if (homePoint == Vector3.zero)
            homePoint = transform.position;
        if (patrolDestination == Vector3.zero)
            patrolDestination = homePoint;
    }

    /// <summary>Called by the spawning GarrisonBuilding.</summary>
    public void Init(Vector3 home, float radius)
    {
        homePoint = home;
        patrolDestination = home;
    }

    /// <summary>
    /// Called by the GarrisonBuilding whenever the player switches the
    /// squad's stance (and on every fresh spawn, so newcomers match the
    /// squad). anchor is the defended point for DefendPoint; ignored
    /// otherwise. No state reset needed here - the leash/aggro checks all
    /// read the effective values live, so a soldier mid-chase self-corrects
    /// on its very next tick (a too-far target gets dropped by the new
    /// leash; a suddenly-visible one gets acquired by the next scan).
    /// </summary>
    public void SetStance(GarrisonBuilding.Stance newStance, Vector3 anchor)
    {
        stance = newStance;
        defendAnchor = anchor;
    }

    /// <summary>
    /// Called by the GarrisonBuilding, which coordinates the whole squad's
    /// patrol as a formation - each soldier just receives its own slot.
    /// While fighting, the slot is only stored; the soldier walks to it
    /// once the fight ends (EnterPatrolling).
    /// </summary>
    public void SetPatrolDestination(Vector3 slot)
    {
        patrolDestination = slot;

        if (state == State.Patrolling && !selfHealth.IsDead && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.SetDestination(patrolDestination);
        }
    }

    private void Update()
    {
        if (selfHealth.IsDead)
            return;

        // Off-mesh recovery - same carve-swallow hazard as the crabs when a
        // wall gets placed right next to a patrolling soldier.
        if (!agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
                EnterPatrolling();
            }
            return;
        }

        switch (state)
        {
            case State.Patrolling: TickPatrolling(); break;
            case State.Attacking: TickAttacking(); break;
        }
    }

    private void TickPatrolling()
    {
        // Two-tier pace: amble in formation, hurry when the slot is far
        // (fresh spawn joining a moving squad, or regrouping after a fight).
        float slotDistance = Vector3.Distance(transform.position, patrolDestination);
        agent.speed = slotDistance > catchUpDistance ? hurrySpeed : patrolSpeed;

        scanTimer -= Time.deltaTime;
        if (scanTimer <= 0f)
        {
            scanTimer = scanInterval;
            TryAcquireTarget();
        }
    }

private void TickAttacking()
    {
        agent.speed = hurrySpeed; // combat is never leisurely

        if (targetHealth == null || targetHealth.IsDead)
        {
            isKiting = false;
            ClearAimFlash();
            // Fight's over - another enemy right here, or back to formation?
            if (!TryAcquireTarget())
                EnterPatrolling();
            return;
        }

        // Leash is measured from the stance's anchor (home for Patrol, the
        // defended point for DefendPoint; Seek & Destroy has no leash at
        // all). A crab kiting a soldier away from its post stops working
        // at this line.
        if (Vector3.Distance(transform.position, LeashAnchor) > EffectiveLeashDistance)
        {
            isKiting = false;
            ClearAimFlash();
            EnterPatrolling();
            return;
        }

        // Subtract our own agent.radius: raw root-to-surface distance never
        // accounted for the soldier's own body, so attackRange was really
        // measured root-to-surface rather than surface-to-surface - the same
        // fix as CrabAI, for the same reason (obstacle avoidance used to
        // mask the slack; it's gone now, so the imprecision is visible).
        Vector3 closestPoint = targetCollider.ClosestPoint(transform.position);
        float distance = Vector3.Distance(transform.position, closestPoint) - agent.radius;

        if (combatStyle == CombatStyle.Melee)
            TickMelee(closestPoint, distance);
        else
            TickRanged(closestPoint, distance);
    }

private void TickMelee(Vector3 closestPoint, float distance)
    {
        if (distance > attackRange)
        {
            agent.isStopped = false;
            agent.SetDestination(closestPoint);
        }
        else
        {
            agent.isStopped = true;
            FaceTarget(closestPoint);

            attackTimer -= Time.deltaTime;
            if (attackTimer <= 0f)
            {
                attackTimer = attackCooldown;
                if (useAoeSlash)
                    PerformAoeSlash();
                else
                    targetHealth.TakeDamage(damage);
            }
        }
    }

    /// <summary>
    /// A small forward cone instead of a single-target hit - "a few
    /// enemies right in front of him", not a battlefield-wide sweep. No
    /// windup, no frozen-direction telegraph, no knockback (unlike the
    /// Brute's ConeSlam this is modeled on) - Guardians are meant to read
    /// as quick, responsive fighters, not a slow heavy hitter, and a
    /// shielded defensive unit shoving enemies around didn't feel like the
    /// right fit anyway. Easy to bolt on later via KnockbackReceiver if
    /// that changes - the pattern's identical to CrabAI's own slam.
    /// </summary>
    private void PerformAoeSlash()
    {
        if (swordPivot != null)
        {
            if (swordSwingRoutine != null)
                StopCoroutine(swordSwingRoutine);
            swordSwingRoutine = StartCoroutine(SwingSword());
        }

        Vector3 origin = transform.position;
        Collider[] hits = Physics.OverlapSphere(origin, slashRadius, enemyMask);
        HashSet<Health> alreadyHit = new HashSet<Health>();

        foreach (Collider hit in hits)
        {
            Health victim = hit.GetComponentInParent<Health>();
            if (victim == null || victim.IsDead || alreadyHit.Contains(victim))
                continue;

            Vector3 toVictim = hit.ClosestPoint(origin) - origin;
            toVictim.y = 0f;
            if (toVictim.sqrMagnitude > 0.0001f && Vector3.Angle(transform.forward, toVictim) > slashAngle * 0.5f)
                continue; // outside the forward cone - behind or to the side doesn't get hit

            alreadyHit.Add(victim);
            victim.TakeDamage(damage);
        }
    }

    /// <summary>Quick out-and-back swing, Y axis only - a fast slash flourish, not a slow telegraph.</summary>
    private IEnumerator SwingSword()
    {
        Quaternion start = swordPivot.localRotation;
        Quaternion swung = start * Quaternion.Euler(0f, swordSwingAngle, 0f);

        float elapsed = 0f;
        while (elapsed < swordSwingDuration)
        {
            elapsed += Time.deltaTime;
            swordPivot.localRotation = Quaternion.Slerp(start, swung, elapsed / swordSwingDuration);
            yield return null;
        }
        swordPivot.localRotation = swung;

        elapsed = 0f;
        while (elapsed < swordSwingDuration)
        {
            elapsed += Time.deltaTime;
            swordPivot.localRotation = Quaternion.Slerp(swung, start, elapsed / swordSwingDuration);
            yield return null;
        }
        swordPivot.localRotation = start;
        swordSwingRoutine = null;
    }

/// <summary>
    /// The Hunter loop: stop -> aim -> shoot -> kite when crowded.
    /// Movement of any kind (advancing, kiting) resets the aim; the bow
    /// cooldown keeps ticking regardless, so a hunter that kites and
    /// resettles fires almost immediately once the aim completes.
    /// </summary>
private void TickRanged(Vector3 closestPoint, float distance)
    {
        attackTimer -= Time.deltaTime;

        // Mid-retreat: keep moving until we've actually put distance
        // between us and the target (or arrived at the retreat point).
        // Without this, the very next frame after triggering a kite would
        // fall into the aim branch below - which sets isStopped=true and
        // cancels the retreat after a single frame (a few centimeters,
        // effectively invisible). That was the whole bug: kiting WAS
        // triggering, it just never got to actually happen.
        if (isKiting)
        {
            if (distance >= kiteDistance * 1.3f || (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f))
            {
                isKiting = false; // safely spaced out (or arrived) - resume normal behavior
            }
            else
            {
                agent.isStopped = false;
                return;
            }
        }

        // Enemy in our face: only kite once every kiteEveryShots shots -
        // kiting on every crowded frame let hunters retreat before
        // anything could ever reach them, which made them unkillable.
        // Between kites they hold ground and keep firing point-blank,
        // taking their lumps like the Melee soldier would.
        if (distance < kiteDistance && shotsSinceKite >= kiteEveryShots)
        {
            Vector3 away = transform.position - closestPoint;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f)
                away = -transform.forward;
            away.Normalize();

            Vector3 retreat = transform.position + away * kiteStepDistance;
            if (Vector3.Distance(retreat, LeashAnchor) > EffectiveLeashDistance)
                retreat = transform.position + (LeashAnchor - transform.position).normalized * kiteStepDistance;

            if (NavMesh.SamplePosition(retreat, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
                retreat = navHit.position;

            agent.isStopped = false;
            agent.SetDestination(retreat);
            aimTimer = aimDuration; // moving spoils the aim
            shotsSinceKite = 0;
            isKiting = true;
            ClearAimFlash();
            return;
        }

        // Too far to shoot - close the gap. (Note: this can't trigger for
        // a hunter that's merely close-but-not-kiting, since kiteDistance
        // is always well under shootRange.)
        if (distance > shootRange)
        {
            agent.isStopped = false;
            agent.SetDestination(closestPoint);
            aimTimer = aimDuration;
            ClearAimFlash();
            return;
        }

        // In range (whether at the preferred distance or crowded and
        // tolerating it) - plant feet, turn to face, aim, loose.
        agent.isStopped = true;
        FaceTarget(closestPoint);

        // Overkill hand-off: if in-flight arrows already account for this
        // target, switch to one that still needs shooting (if any).
        if (targetHealth.EffectiveHP <= 0 && TryAcquireTarget())
        {
            ClearAimFlash();
            return;
        }

        // Fade the flash in as the aim progresses - 0 the instant aiming
        // starts, full brightness right as the shot looses. Driven by
        // whichever of aimTimer/attackTimer is the ACTUAL bottleneck, not
        // just aimTimer alone: attackCooldown is often longer than
        // aimDuration (aim finishes, but the bow still isn't ready), and
        // computing progress from aimTimer alone made the glow peak at
        // full white the moment aiming finished, then just sit there fully
        // lit for however long the cooldown had left - looking stuck.
        // Taking the MAX of the two remaining fractions means progress
        // only reaches 1 at the exact frame Shoot() actually fires.
        float aimFraction = aimDuration > 0f ? Mathf.Clamp01(aimTimer / aimDuration) : 0f;
        float cooldownFraction = attackCooldown > 0f ? Mathf.Clamp01(attackTimer / attackCooldown) : 0f;
        float progress = 1f - Mathf.Max(aimFraction, cooldownFraction);
        if (hitFlash != null)
            hitFlash.SetSustainedTint(aimFlashColor, progress);

        aimTimer -= Time.deltaTime;
        if (aimTimer <= 0f && attackTimer <= 0f)
            Shoot();
    }

private void ClearAimFlash()
    {
        if (hitFlash != null)
            hitFlash.ClearSustainedTint();
    }


private void Shoot()
    {
        attackTimer = attackCooldown;
        shotsSinceKite++;

        // Fresh aim cycle starting now: cut the flash to zero immediately
        // (the release) and restart the fade-in for the next shot.
        aimTimer = aimDuration;
        ClearAimFlash();

        if (arrowPrefab == null || targetHealth == null)
            return;

        Vector3 spawnPosition = transform.position + Vector3.up * 0.8f;
        GameObject arrowObject = Instantiate(arrowPrefab, spawnPosition, Quaternion.identity);
        Projectile arrow = arrowObject.GetComponent<Projectile>();
        if (arrow != null)
            arrow.Init(targetHealth, damage, projectileSpeed); // reserves pending damage - tower-style overkill prevention for free
    }

private void FaceTarget(Vector3 point)
    {
        Vector3 direction = point - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f)
            return;

        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), 10f * Time.deltaTime);
    }





private bool TryAcquireTarget()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, EffectiveAggroRadius, enemyMask);

        float bestDistance = float.MaxValue;
        Health bestHealth = null;
        Collider bestCollider = null;

        foreach (Collider hit in hits)
        {
            Health candidate = hit.GetComponentInParent<Health>();
            if (candidate == null || candidate.IsDead)
                continue;

            // Seek & Destroy hunts by knowledge, not sight - a squad sent
            // out to kill shouldn't stand idle just because a wall hides
            // its prey. The other stances are standing watch and only
            // react to what they can actually see.
            if (stance != GarrisonBuilding.Stance.SeekAndDestroy && !HasLineOfSight(hit))
                continue; // wall in the way - can't see it, don't chase it

            // Hunters borrow the towers' overkill prevention: a target whose
            // EffectiveHP is already zero (arrows in flight will finish it)
            // isn't worth another arrow.
            if (combatStyle == CombatStyle.Ranged && candidate.EffectiveHP <= 0)
                continue;

            // Leash-aware acquisition: never lock onto an enemy the leash
            // wouldn't let us actually fight. Without this, an enemy just
            // beyond the leash gets acquired (it's within aggro of the
            // soldier), chased until the home-distance leash trips,
            // dropped, re-acquired on the next scan... an infinite yo-yo.
            // The soldier simply doesn't SEE enemies beyond its patrol
            // duty; they become visible again the moment they come closer.
            Vector3 pointNearHome = hit.ClosestPoint(LeashAnchor);
            if (Vector3.Distance(LeashAnchor, pointNearHome) > EffectiveLeashDistance)
                continue;

            float distance = Vector3.Distance(transform.position, hit.ClosestPoint(transform.position));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestHealth = candidate;
                bestCollider = hit;
            }
        }

        if (bestHealth == null)
            return false;

        targetHealth = bestHealth;
        targetCollider = bestCollider;
        attackTimer = attackCooldown * 0.5f;
        aimTimer = aimDuration; // fresh engagement starts the glow at 0, not partway through
        state = State.Attacking;
        return true;
    }

/// <summary>
    /// True if nothing on the sight-blocker layers stands between us and the
    /// target. The Keep is explicitly exempt - it's OUR building, in the
    /// middle of the courtyard we're guarding, and treating it as a wall
    /// would blind half of every patrol lap.
    /// </summary>
    private bool HasLineOfSight(Collider target)
    {
        Vector3 eye = transform.position + Vector3.up * 0.6f;
        Vector3 aim = target.ClosestPoint(eye) + Vector3.up * 0.3f;
        Vector3 direction = aim - eye;
        float distance = direction.magnitude;
        if (distance < 0.01f)
            return true;

        RaycastHit[] blockers = Physics.RaycastAll(eye, direction / distance, distance, sightBlockerMask);
        foreach (RaycastHit blocker in blockers)
        {
            if (blocker.collider.GetComponentInParent<Keep>() != null)
                continue; // the Keep never blocks sight

            return false;
        }

        return true;
    }


    private void EnterPatrolling()
    {
        targetHealth = null;
        targetCollider = null;
        state = State.Patrolling;

        if (agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.SetDestination(patrolDestination);
        }
    }

private void HandleDeath()
    {
        isKiting = false;
        ClearAimFlash();

        // Persistent red - distinct from the momentary damage/aim flashes,
        // this one is never cleared (the corpse is destroyed shortly after
        // anyway) so the topple visibly reads as "dead" the whole time.
        if (hitFlash != null)
            hitFlash.SetSustainedTint(Color.red, 1f);

        // A unit can die while standing OFF the mesh (shoved into a wall's
        // carve fringe by attackers) - isStopped throws on an off-mesh
        // agent. Disabling is always safe.
        if (agent.enabled && agent.isOnNavMesh)
            agent.isStopped = true;
        agent.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>())
            c.enabled = false;

        StartCoroutine(DeathSequence());
    }

    private IEnumerator DeathSequence()
    {
        Quaternion start = transform.rotation;
        Quaternion end = start * Quaternion.Euler(0f, 0f, 90f);
        float elapsed = 0f;

        while (elapsed < toppleDuration)
        {
            elapsed += Time.deltaTime;
            transform.rotation = Quaternion.Slerp(start, end, elapsed / toppleDuration);
            yield return null;
        }

        Destroy(gameObject, corpseLingerTime);
    }
}
