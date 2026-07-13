using System.Collections;
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


    [Header("Death")]
    [SerializeField] private float toppleDuration = 0.4f;
    [SerializeField] private float corpseLingerTime = 1f;

    private NavMeshAgent agent;
    private Health selfHealth;
    private State state = State.Patrolling;
    private Vector3 homePoint;
    private Vector3 patrolDestination;
    private Health targetHealth;
    private Collider targetCollider;
    private float scanTimer;
    private float attackTimer;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        selfHealth = GetComponent<Health>();
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
            // Fight's over - another enemy right here, or back to formation?
            if (!TryAcquireTarget())
                EnterPatrolling();
            return;
        }

        // Leash is measured from HOME: soldiers defend a place. A crab
        // kiting a soldier away from its post stops working at this line.
        if (Vector3.Distance(transform.position, homePoint) > chaseLeashDistance)
        {
            EnterPatrolling();
            return;
        }

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
        Collider[] hits = Physics.OverlapSphere(transform.position, aggroRadius, enemyMask);

        float bestDistance = float.MaxValue;
        Health bestHealth = null;
        Collider bestCollider = null;

        foreach (Collider hit in hits)
        {
            Health candidate = hit.GetComponentInParent<Health>();
            if (candidate == null || candidate.IsDead)
                continue;

            if (!HasLineOfSight(hit))
                continue; // wall in the way - can't see it, don't chase it

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
