using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Crab enemy: a two-state FSM. Default behavior is marching on the Keep;
/// anything damageable (walls, units, the Keep itself) entering its aggro
/// radius becomes a target to chase and attack until dead, then it resumes
/// the march. This is the entire tower-defense threat model: crabs chew
/// through obstacles in their way but always trend toward the Keep.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Health))]
public class CrabAI : MonoBehaviour
{
    private enum State { MarchingToKeep, AttackingTarget }

    [Header("Aggro")]
    [SerializeField] private float aggroRadius = 6f;
    [Tooltip("Scanning every frame is wasteful - a few times a second is plenty.")]
    [SerializeField] private float scanInterval = 0.25f;
    [Tooltip("What the crab will attack: Building | Unit.")]
    [SerializeField] private LayerMask targetMask;

    [Tooltip("Which target layers count as mobile units - always top priority, and they interrupt building attacks.")]
    [SerializeField] private LayerMask unitMask;

    [Tooltip("When the path to the Keep is partial, how far short of the Keep it must stop before the crab decides it's walled off and starts attacking buildings.")]
    [SerializeField] private float blockedGapTolerance = 2.5f;

    [Tooltip("How far a chased unit can get before the crab gives up on it. Keep this larger than the aggro radius so a unit dancing on the aggro boundary doesn't cause target flickering.")]
    [SerializeField] private float chaseLeashRadius = 10f;

    [Tooltip("When walled off, how far around the path's dead-end point to search for the blocking wall.")]
    [SerializeField] private float blockerSearchRadius = 3f;

    [Tooltip("If a crab makes no progress toward its wall for this long (usually because other crabs hog every attack slot), it switches to a neighboring segment of the same blockade.")]
    [SerializeField] private float unreachableRetargetTime = 2f;





    [Header("Attack")]
    [SerializeField] private int damage = 5;
    [Tooltip("Measured from the closest point on the target's collider, not its center - so big buildings are hittable from any side.")]
    [SerializeField] private float attackRange = 1.2f;
    [SerializeField] private float attackCooldown = 1f;

    [Header("Death")]
    [SerializeField] private float toppleDuration = 0.4f;
    [SerializeField] private float corpseLingerTime = 1f;

    private NavMeshAgent agent;
    private Health selfHealth;
    private State state;
    private Health targetHealth;
    private Collider targetCollider;
    private Collider keepCollider;
    private UnityEngine.AI.NavMeshPath pathProbe; // reused scratch path for blocked-checks


    private float scanTimer;
    private float attackTimer;
    private float approachTimer;
    private float closestApproach = float.MaxValue;


private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        selfHealth = GetComponent<Health>();
        selfHealth.OnDeath += HandleDeath;

        // Randomize avoidance priority (lower = more assertive). When two
        // agents share the same priority neither yields and both hesitate -
        // in single-file corridors that becomes a visible traffic jam. A
        // random spread means every encounter has a clear right-of-way.
        agent.avoidancePriority = Random.Range(30, 70);
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
        // Same guard pattern as Turtling's IsDead flag: never act after death,
        // even if this Update slips in on the death frame.
        if (selfHealth.IsDead)
            return;

        // Recovery: placing a wall right next to a crab can carve the NavMesh
        // out from under its feet (carving erodes ~one agent radius beyond
        // the wall's faces). An off-mesh agent can't move AT ALL - every
        // SetDestination silently fails - so warp back to the nearest
        // walkable spot and resume next frame.
        if (!agent.isOnNavMesh)
        {
            if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out UnityEngine.AI.NavMeshHit hit, 3f, UnityEngine.AI.NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
                MarchToKeep(); // Warp clears the agent's path - re-issue intent
            }
            return;
        }

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
                targetHealth.TakeDamage(damage);
            }
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
        // actually IN THE WAY - the one nearest to where the path toward
        // the Keep dead-ends - NOT whichever wall is nearest the crab.
        // A free-standing wall that blocks nothing gets walked past.
        if (IsPathToKeepBlocked(out Vector3 stuckPoint))
        {
            // Candidates come from around the path's dead-end (so only walls
            // actually part of the blockade are eligible) - but each crab
            // picks the candidate nearest to ITSELF, spreading the horde
            // across neighboring segments instead of piling onto one block.
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
            pathProbe = new UnityEngine.AI.NavMeshPath();

        // A crab pressed against a wall is often standing just off the
        // NavMesh (carving erodes the surface around obstacles) and path
        // queries from off-mesh positions fail - snap to walkable first.
        Vector3 start = transform.position;
        if (UnityEngine.AI.NavMesh.SamplePosition(start, out UnityEngine.AI.NavMeshHit startHit, 2f, UnityEngine.AI.NavMesh.AllAreas))
            start = startHit.position;

        // 'Doorstep' = the walkable point nearest the Keep's center. Caution:
        // if walls are built snug against the Keep (their carve merges with
        // the Keep's own carve), this point can land OUTSIDE the wall ring.
        Vector3 keepDoorstep = Keep.Instance.transform.position;
        if (UnityEngine.AI.NavMesh.SamplePosition(keepDoorstep, out UnityEngine.AI.NavMeshHit keepHit, 15f, UnityEngine.AI.NavMesh.AllAreas))
            keepDoorstep = keepHit.position;

        bool doorstepTouchesKeep =
            Vector3.Distance(keepDoorstep, keepCollider.ClosestPoint(keepDoorstep)) <= blockedGapTolerance;

        if (!UnityEngine.AI.NavMesh.CalculatePath(start, keepDoorstep, UnityEngine.AI.NavMesh.AllAreas, pathProbe))
        {
            stuckPoint = start;
            return true; // no path computable even from a walkable spot
        }

        if (pathProbe.status == UnityEngine.AI.NavMeshPathStatus.PathComplete)
        {
            if (doorstepTouchesKeep)
                return false; // genuinely reachable - path leads right to the Keep

            // We CAN reach the walkable point nearest the Keep... but that
            // point isn't actually AT the Keep. Meaning: the Keep is sealed
            // so tightly that no walkable ground touches it at all. That is
            // blocked - and the doorstep sits right against the seal, which
            // makes it exactly the right anchor for the blocker search.
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

        if (Keep.Instance != null)
        {
            agent.isStopped = false;
            agent.SetDestination(Keep.Instance.transform.position);
        }
    }

    private void HandleDeath()
    {
        // Stop navigating and become intangible so corpses don't block the living.
        agent.isStopped = true;
        agent.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>())
            c.enabled = false;

        StartCoroutine(DeathSequence());
    }

    private IEnumerator DeathSequence()
    {
        // Quick topple, then vanish - placeholder-art appropriate.
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
