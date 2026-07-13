using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Crab enemy FSM. Default behavior is marching on the Keep; anything
/// damageable entering its aggro radius becomes a target by priority:
/// units first (always), the Keep once in reach, and other buildings ONLY
/// when the path to the Keep is actually blocked - anchored to where the
/// path dead-ends, so free-standing walls that block nothing are ignored.
///
/// Two attack styles, selected per prefab (data, not code):
///  - SingleTarget: the regular crab's bite on a cooldown.
///  - ConeSlam: the Brute's Turtling-Heavy-style attack - a windup with a
///    white charge-glow tell and a direction FROZEN at windup start (so
///    sidestepping dodges it), then cone AoE damage with knockback.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Health))]
public class CrabAI : MonoBehaviour
{
    public enum AttackStyle { SingleTarget, ConeSlam }

    private enum State { MarchingToKeep, AttackingTarget }

    [Header("Aggro")]
    [SerializeField] private float aggroRadius = 6f;
    [Tooltip("Scanning every frame is wasteful - a few times a second is plenty.")]
    [SerializeField] private float scanInterval = 0.25f;
    [Tooltip("What the crab will attack: Building | Unit.")]
    [SerializeField] private LayerMask targetMask;
    [Tooltip("Which target layers count as mobile units - always top priority, and they interrupt building attacks.")]
    [SerializeField] private LayerMask unitMask;

    [Header("Blocked-Path Behavior")]
    [Tooltip("When the path to the Keep is partial, how far short of the Keep it must stop before the crab decides it's walled off and starts attacking buildings.")]
    [SerializeField] private float blockedGapTolerance = 2.5f;
    [Tooltip("How far a chased unit can get before the crab gives up on it. Keep larger than the aggro radius to avoid target flickering.")]
    [SerializeField] private float chaseLeashRadius = 10f;
    [Tooltip("When walled off, how far around the path's dead-end point to search for the blocking wall.")]
    [SerializeField] private float blockerSearchRadius = 3f;
    [Tooltip("If a crab makes no progress toward its wall for this long (attack slots crowded), it switches to a neighboring segment of the same blockade.")]
    [SerializeField] private float unreachableRetargetTime = 2f;

    [Header("Attack")]
    [SerializeField] private AttackStyle attackStyle = AttackStyle.SingleTarget;
    [SerializeField] private int damage = 5;
    [Tooltip("Measured from the closest point on the target's collider, not its center - so big buildings are hittable from any side.")]
    [SerializeField] private float attackRange = 1.2f;
    [SerializeField] private float attackCooldown = 1f;

    [Header("Cone Slam (Brute only - ignored for SingleTarget)")]
    [SerializeField] private float slamWindupDuration = 0.8f;
    [SerializeField] private float slamConeRange = 3f;
    [SerializeField] private float slamConeAngle = 75f;
    [SerializeField] private float slamKnockbackDistance = 3f;
    [SerializeField] private float slamKnockbackDuration = 0.25f;

    [Header("Death")]
    [SerializeField] private float toppleDuration = 0.4f;
    [SerializeField] private float corpseLingerTime = 1f;

    private NavMeshAgent agent;
    private Health selfHealth;
    private State state;
    private Health targetHealth;
    private Collider targetCollider;
    private Collider keepCollider;
    private NavMeshPath pathProbe;
    private float scanTimer;
    private float attackTimer;
    private float approachTimer;
    private float closestApproach = float.MaxValue;
    private bool isWindingUp;
    private Coroutine windupRoutine;
    private Renderer[] renderers;
    private Color[] baseColors;
    private MaterialPropertyBlock glowBlock;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        selfHealth = GetComponent<Health>();
        selfHealth.OnDeath += HandleDeath;

        // Randomize avoidance priority (lower = more assertive). When two
        // agents share the same priority neither yields and both hesitate -
        // in single-file corridors that becomes a visible traffic jam.
        agent.avoidancePriority = Random.Range(30, 70);

        // Charge-glow plumbing (only used by ConeSlam).
        renderers = GetComponentsInChildren<Renderer>();
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            Material mat = renderers[i].sharedMaterial;
            baseColors[i] = (mat != null && mat.HasProperty(BaseColorId))
                ? mat.GetColor(BaseColorId)
                : Color.white;
        }
        glowBlock = new MaterialPropertyBlock();
    }

    private void OnDestroy()
    {
        if (selfHealth != null)
            selfHealth.OnDeath -= HandleDeath;
    }

    private void Start()
    {
        if (Keep.Instance != null)
            keepCollider = Keep.Instance.GetComponentInChildren<Collider>();

        MarchToKeep();
    }

    private void Update()
    {
        // Same guard pattern as Turtling's IsDead flag: never act after death.
        if (selfHealth.IsDead)
            return;

        // Recovery: placing a wall right next to a crab can carve the NavMesh
        // out from under its feet. An off-mesh agent can't move AT ALL -
        // warp back to the nearest walkable spot and resume next frame.
        if (!agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
                MarchToKeep(); // Warp clears the agent's path - re-issue intent
            }
            return;
        }

        // Committed to a slam windup: hold position and direction (the
        // frozen-at-action-start pattern - dodgeable by design).
        if (isWindingUp)
            return;

        switch (state)
        {
            case State.MarchingToKeep:
                TickMarching();
                break;
            case State.AttackingTarget:
                TickAttacking();
                break;
        }
    }

    private void TickMarching()
    {
        scanTimer -= Time.deltaTime;
        if (scanTimer <= 0f)
        {
            scanTimer = scanInterval;
            if (TryAcquireTarget())
                state = State.AttackingTarget;
        }
    }

    private void TickAttacking()
    {
        // Target died (or its GameObject was destroyed) - resume the march.
        if (targetHealth == null || targetHealth.IsDead)
        {
            MarchToKeep();
            return;
        }

        bool targetIsUnit = ((1 << targetCollider.gameObject.layer) & unitMask) != 0;

        if (targetIsUnit)
        {
            // Leash check: a unit that outran the chase gets dropped, and the
            // crab re-acquires - nearest unit still in aggro range wins, or
            // it resumes the march if nobody's around.
            float targetDistance = Vector3.Distance(transform.position, targetCollider.ClosestPoint(transform.position));
            if (targetDistance > chaseLeashRadius)
            {
                if (!TryAcquireTarget())
                    MarchToKeep();
                return;
            }
        }
        else
        {
            // While chewing on a building, periodically re-evaluate:
            scanTimer -= Time.deltaTime;
            if (scanTimer <= 0f)
            {
                scanTimer = scanInterval;

                // 1. A unit walking into range interrupts - mobile threats
                //    always take priority over a wall.
                Collider[] hits = Physics.OverlapSphere(transform.position, aggroRadius, unitMask);
                if (TryFindNearest(transform.position, hits, unitMask, out Health unitHealth, out Collider unitCollider))
                {
                    SetTarget(unitHealth, unitCollider);
                }
                // 2. A breach opened somewhere else (another crab broke
                //    through) - abandon this wall and take the gap. Never
                //    applies to the Keep itself: that's the objective.
                else if (targetCollider != keepCollider && !IsPathToKeepBlocked(out _))
                {
                    MarchToKeep();
                    return;
                }
            }
        }

        // Distance to the collider's closest point, not the transform center -
        // otherwise a crab standing right against a big Keep would think it's
        // still 'far away' because the center is meters inside the building.
        Vector3 closestPoint = targetCollider.ClosestPoint(transform.position);
        float distance = Vector3.Distance(transform.position, closestPoint);

        if (distance > attackRange)
        {
            agent.isStopped = false;
            agent.SetDestination(closestPoint);

            // Crowding fallback (buildings only - the leash handles units):
            // no progress toward our wall for a while means every attack slot
            // is taken. Switch to a neighboring segment of the same blockade.
            if (!targetIsUnit)
            {
                if (distance < closestApproach - 0.05f)
                {
                    closestApproach = distance;
                    approachTimer = 0f;
                }
                else
                {
                    approachTimer += Time.deltaTime;
                    if (approachTimer >= unreachableRetargetTime)
                    {
                        approachTimer = 0f;
                        TryRetargetAdjacentBlocker();
                    }
                }
            }
        }
        else
        {
            agent.isStopped = true;
            attackTimer -= Time.deltaTime;
            if (attackTimer <= 0f)
            {
                attackTimer = attackCooldown;

                if (attackStyle == AttackStyle.SingleTarget)
                    targetHealth.TakeDamage(damage);
                else
                    windupRoutine = StartCoroutine(WindupAndSlam());
            }
        }
    }

    private IEnumerator WindupAndSlam()
    {
        isWindingUp = true;

        // Frozen at windup start - the Turtling Heavy pattern. The slam
        // lands where the crab AIMED, not where the target ran to, which is
        // exactly what makes it dodgeable and fair.
        Vector3 origin = transform.position;
        Vector3 slamDirection = targetCollider != null
            ? targetCollider.ClosestPoint(origin) - origin
            : transform.forward;
        slamDirection.y = 0f;
        if (slamDirection.sqrMagnitude < 0.01f)
            slamDirection = transform.forward;
        slamDirection.Normalize();

        // Visual tell: charge-glow toward white over the whole windup.
        float elapsed = 0f;
        while (elapsed < slamWindupDuration)
        {
            if (selfHealth.IsDead)
            {
                SetGlow(0f);
                isWindingUp = false;
                yield break;
            }

            elapsed += Time.deltaTime;
            SetGlow(Mathf.Lerp(0f, 0.85f, elapsed / slamWindupDuration));
            yield return null;
        }
        SetGlow(0f);

        // Resolve the cone from the FROZEN origin and direction.
        Collider[] hits = Physics.OverlapSphere(origin, slamConeRange, targetMask);
        HashSet<Health> alreadyHit = new HashSet<Health>();

        foreach (Collider hit in hits)
        {
            Health victim = hit.GetComponentInParent<Health>();
            if (victim == null || victim.IsDead || alreadyHit.Contains(victim))
                continue;

            Vector3 toVictim = hit.ClosestPoint(origin) - origin;
            toVictim.y = 0f;
            if (toVictim.sqrMagnitude > 0.0001f &&
                Vector3.Angle(slamDirection, toVictim) > slamConeAngle * 0.5f)
                continue;

            alreadyHit.Add(victim);
            victim.TakeDamage(damage);

            // Only things with a receiver get shoved - buildings just take the hit.
            KnockbackReceiver knockback = victim.GetComponent<KnockbackReceiver>();
            if (knockback != null && toVictim.sqrMagnitude > 0.0001f)
                knockback.ApplyKnockback(toVictim.normalized, slamKnockbackDistance, slamKnockbackDuration);
        }

        isWindingUp = false;
    }

    private void SetGlow(float intensity)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;

            glowBlock.Clear();
            if (intensity > 0f)
                glowBlock.SetColor(BaseColorId, Color.Lerp(baseColors[i], Color.white, intensity));
            renderers[i].SetPropertyBlock(glowBlock);
        }
    }

    private bool TryAcquireTarget()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, aggroRadius, targetMask);

        // Priority 1: mobile units - an active threat is always engaged.
        if (TryFindNearest(transform.position, hits, unitMask, out Health unitHealth, out Collider unitCollider))
        {
            SetTarget(unitHealth, unitCollider);
            return true;
        }

        // Priority 2: the Keep itself, once within reach - it IS the objective.
        if (keepCollider != null && Keep.Instance != null)
        {
            Health keepHealth = Keep.Instance.GetComponent<Health>();
            if (keepHealth != null && !keepHealth.IsDead)
            {
                float keepDistance = Vector3.Distance(transform.position, keepCollider.ClosestPoint(transform.position));
                if (keepDistance <= aggroRadius)
                {
                    SetTarget(keepHealth, keepCollider);
                    return true;
                }
            }
        }

        // Priority 3: when the path is blocked, attack the wall that's
        // actually IN THE WAY. Candidates come from around the path's
        // dead-end (only blockade walls are eligible), but each crab picks
        // the candidate nearest to ITSELF, spreading the horde across
        // neighboring segments instead of piling onto one block.
        if (IsPathToKeepBlocked(out Vector3 stuckPoint))
        {
            int buildingMask = targetMask.value & ~unitMask.value;
            Collider[] blockers = Physics.OverlapSphere(stuckPoint, blockerSearchRadius, buildingMask);
            if (TryFindNearest(transform.position, blockers, buildingMask, out Health blockerHealth, out Collider blockerCollider))
            {
                SetTarget(blockerHealth, blockerCollider);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Crowding fallback: pick a different segment of the CURRENT blockade
    /// (candidates are still anchored to the path's dead-end, so a wall
    /// that blocks nothing can never be chosen), excluding the segment we
    /// just failed to reach. If there's no alternative, keep shoving.
    /// </summary>
    private void TryRetargetAdjacentBlocker()
    {
        if (!IsPathToKeepBlocked(out Vector3 stuckPoint))
            return; // path opened up - the regular breach check handles that

        int buildingMask = targetMask.value & ~unitMask.value;
        Collider[] blockers = Physics.OverlapSphere(stuckPoint, blockerSearchRadius, buildingMask);
        if (TryFindNearest(transform.position, blockers, buildingMask, out Health blockerHealth, out Collider blockerCollider, targetCollider))
            SetTarget(blockerHealth, blockerCollider);
    }

    private bool TryFindNearest(Vector3 from, Collider[] hits, LayerMask filter, out Health bestHealth, out Collider bestCollider, Collider exclude = null)
    {
        float bestDistance = float.MaxValue;
        bestHealth = null;
        bestCollider = null;

        foreach (Collider hit in hits)
        {
            if (hit == exclude)
                continue;

            if (((1 << hit.gameObject.layer) & filter) == 0)
                continue;

            Health candidate = hit.GetComponentInParent<Health>();
            if (candidate == null || candidate.IsDead)
                continue;

            float distance = Vector3.Distance(from, hit.ClosestPoint(from));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestHealth = candidate;
                bestCollider = hit;
            }
        }

        return bestHealth != null;
    }

    private void SetTarget(Health health, Collider targetColliderIn)
    {
        targetHealth = health;
        targetCollider = targetColliderIn;
        state = State.AttackingTarget;
        attackTimer = attackCooldown * 0.5f; // small windup before the first hit
        approachTimer = 0f;
        closestApproach = float.MaxValue;
    }

    private bool IsPathToKeepBlocked(out Vector3 stuckPoint)
    {
        stuckPoint = transform.position;

        if (keepCollider == null || Keep.Instance == null)
            return false;

        if (pathProbe == null)
            pathProbe = new NavMeshPath();

        // A crab pressed against a wall is often standing just off the
        // NavMesh (carving erodes the surface around obstacles) and path
        // queries from off-mesh positions fail - snap to walkable first.
        Vector3 start = transform.position;
        if (NavMesh.SamplePosition(start, out NavMeshHit startHit, 2f, NavMesh.AllAreas))
            start = startHit.position;

        // 'Doorstep' = the walkable point nearest the Keep's center. Caution:
        // if walls are built snug against the Keep (their carve merges with
        // the Keep's own carve), this point can land OUTSIDE the wall ring.
        Vector3 keepDoorstep = Keep.Instance.transform.position;
        if (NavMesh.SamplePosition(keepDoorstep, out NavMeshHit keepHit, 15f, NavMesh.AllAreas))
            keepDoorstep = keepHit.position;

        bool doorstepTouchesKeep =
            Vector3.Distance(keepDoorstep, keepCollider.ClosestPoint(keepDoorstep)) <= blockedGapTolerance;

        if (!NavMesh.CalculatePath(start, keepDoorstep, NavMesh.AllAreas, pathProbe))
        {
            stuckPoint = start;
            return true; // no path computable even from a walkable spot
        }

        if (pathProbe.status == NavMeshPathStatus.PathComplete)
        {
            if (doorstepTouchesKeep)
                return false; // genuinely reachable - path leads right to the Keep

            // We CAN reach the walkable point nearest the Keep... but that
            // point isn't actually AT the Keep: the Keep is sealed so
            // tightly no walkable ground touches it. Blocked - and the
            // doorstep sits right against the seal, which makes it exactly
            // the right anchor for the blocker search.
            stuckPoint = keepDoorstep;
            return true;
        }

        // Partial path: it ends right against whatever is in the way.
        Vector3[] corners = pathProbe.corners;
        if (corners == null || corners.Length == 0)
            return true;

        stuckPoint = corners[corners.Length - 1];

        // If there IS walkable ground at the Keep and our path ends on it,
        // we've effectively arrived (the Keep's own carve makes a fully
        // 'complete' path impossible in some layouts) - not blocked.
        float gap = Vector3.Distance(stuckPoint, keepCollider.ClosestPoint(stuckPoint));
        if (doorstepTouchesKeep && gap <= blockedGapTolerance)
            return false;

        return true;
    }

    private void MarchToKeep()
    {
        state = State.MarchingToKeep;
        targetHealth = null;
        targetCollider = null;

        if (Keep.Instance != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.SetDestination(Keep.Instance.transform.position);
        }
    }

    private void HandleDeath()
    {
        if (windupRoutine != null)
        {
            StopCoroutine(windupRoutine);
            SetGlow(0f);
            isWindingUp = false;
        }

        // Stop navigating and become intangible so corpses don't block the
        // living. Guarded: a crab can die while shoved off the mesh (wall
        // carve fringe), and isStopped throws on an off-mesh agent.
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
