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

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        selfHealth = GetComponent<Health>();
        selfHealth.OnDeath += HandleDeath;
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
            // While chewing on a building, periodically re-evaluate on the
            // same interval used for scanning:
            scanTimer -= Time.deltaTime;
            if (scanTimer <= 0f)
            {
                scanTimer = scanInterval;

                // 1. A unit walking into range interrupts - mobile threats
                //    always take priority over a wall.
                Collider[] hits = Physics.OverlapSphere(transform.position, aggroRadius, unitMask);
                if (TryFindNearest(hits, unitMask, out Health unitHealth, out Collider unitCollider))
                {
                    SetTarget(unitHealth, unitCollider);
                }
                // 2. A breach opened somewhere else (another crab broke
                //    through) - abandon this wall and take the gap. Never
                //    applies to the Keep itself: that's the objective.
                else if (targetCollider != keepCollider && !IsPathToKeepBlocked())
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
        if (TryFindNearest(hits, unitMask, out Health unitHealth, out Collider unitCollider))
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

        // Priority 3: other buildings (walls) - but ONLY when the path to
        // the Keep is blocked. A wall that isn't in the way isn't worth
        // stopping for; walk past it instead.
        if (IsPathToKeepBlocked() && TryFindNearest(hits, ~unitMask, out Health buildingHealth, out Collider buildingCollider))
        {
            SetTarget(buildingHealth, buildingCollider);
            return true;
        }

        return false;
    }

    private bool TryFindNearest(Collider[] hits, LayerMask filter, out Health bestHealth, out Collider bestCollider)
    {
        float bestDistance = float.MaxValue;
        bestHealth = null;
        bestCollider = null;

        foreach (Collider hit in hits)
        {
            if (((1 << hit.gameObject.layer) & filter) == 0)
                continue;

            Health candidate = hit.GetComponentInParent<Health>();
            if (candidate == null || candidate.IsDead)
                continue;

            float distance = Vector3.Distance(transform.position, hit.ClosestPoint(transform.position));
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
    }

private bool IsPathToKeepBlocked()
    {
        if (keepCollider == null || Keep.Instance == null)
            return false;

        if (pathProbe == null)
            pathProbe = new UnityEngine.AI.NavMeshPath();

        // A crab pressed against a wall is often standing just off the
        // NavMesh - carving erodes the walkable surface around obstacles by
        // roughly one agent radius, and melee range sits right on that
        // edge. Path queries from an off-mesh position simply FAIL, which
        // previously read as 'blocked forever'. Snap both endpoints to the
        // nearest walkable spot before asking.
        Vector3 start = transform.position;
        if (UnityEngine.AI.NavMesh.SamplePosition(start, out UnityEngine.AI.NavMeshHit startHit, 2f, UnityEngine.AI.NavMesh.AllAreas))
            start = startHit.position;

        Vector3 keepDoorstep = Keep.Instance.transform.position;
        if (UnityEngine.AI.NavMesh.SamplePosition(keepDoorstep, out UnityEngine.AI.NavMeshHit keepHit, 10f, UnityEngine.AI.NavMesh.AllAreas))
            keepDoorstep = keepHit.position;

        if (!UnityEngine.AI.NavMesh.CalculatePath(start, keepDoorstep, UnityEngine.AI.NavMesh.AllAreas, pathProbe))
            return true; // no path computable even from a walkable spot - blocked

        if (pathProbe.status == UnityEngine.AI.NavMeshPathStatus.PathComplete)
            return false;

        // Partial path: judge by where it ENDS. Right up against the Keep
        // means effectively arrived (the Keep's own carve makes its center
        // unreachable even with no walls anywhere); stopping well short
        // means something is in the way.
        Vector3[] corners = pathProbe.corners;
        if (corners == null || corners.Length == 0)
            return true;

        Vector3 pathEnd = corners[corners.Length - 1];
        float gap = Vector3.Distance(pathEnd, keepCollider.ClosestPoint(pathEnd));
        return gap > blockedGapTolerance;
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
