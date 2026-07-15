using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Worker gather loop as an enum FSM:
///
///   Idle -> MovingToNode -> Harvesting -> MovingToKeep -> Depositing -+
///              ^                                                      |
///              +------------------------------------------------------+
///
/// Two external entry points make it player-interruptible at ANY moment:
/// AssignDeposit (right-click a sand pile) starts/redirects the loop, and
/// OnManualMoveOrder (right-click ground) cancels it entirely - a direct
/// order is law. THIS interruptibility is why it's an FSM and not a chain
/// of coroutines: yanking a coroutine mid-sequence and cleaning up after
/// it is painful; changing a state variable is one line.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class WorkerAI : MonoBehaviour
{
    private enum State { Idle, MovingToNode, Harvesting, MovingToKeep, Depositing }

    [Header("Gathering")]
    [SerializeField] private int carryCapacity = 10;
    [SerializeField] private float harvestDuration = 3f;
    [SerializeField] private float depositDuration = 0.5f;
    [Tooltip("How close (to the node's/Keep's collider surface) counts as arrived.")]
    [SerializeField] private float interactRange = 1.5f;

    [Header("Visuals (optional)")]
    [Tooltip("Child object shown while carrying sand - e.g. a small sphere above the head.")]
    [SerializeField] private GameObject carryVisual;
    [Tooltip("The upgraded model's digging shovel - assign the 'Shovel' child on WorkerShovel only. Left null on the base Worker, which simply skips the animation entirely.")]
    [SerializeField] private Transform shovelPivot;
    [SerializeField] private float shovelRestAngle = 75f;
    [SerializeField] private float shovelDigAngle = 150f;
    [SerializeField] private float shovelSwingSpeed = 4f;

    [Header("Auto-Work")]
    [Tooltip("When idle with nothing assigned (fresh spawn, or a node just ran dry), automatically seek the nearest non-empty deposit. Manual assignment (right-click a node) and manual move orders (right-click ground) still work exactly as before and always take priority - this only fills the gaps where a worker would otherwise stand around waiting to be told what to do.")]
    [SerializeField] private bool autoSeekWork = true;
    [SerializeField] private float idleSeekInterval = 1.5f;

    [Header("Debug")]
    [Tooltip("Log every state transition - keep on until you trust the FSM, per the plan.")]
    [SerializeField] private bool logStateTransitions = false;

    [Header("Model Identity")]
    [Tooltip("True on an already-upgraded model (e.g. WorkerShovel) - lets KeepWorkerRecruiter's swap skip a worker that's already correct instead of needlessly re-swapping it.")]
    [SerializeField] private bool isUpgradedModel = false;
    public bool IsUpgradedModel => isUpgradedModel;

    [Header("Death")]
    [SerializeField] private float toppleDuration = 0.4f;
    [SerializeField] private float corpseLingerTime = 1f;

    private NavMeshAgent agent;
    private Health selfHealth;
    private HitFlash hitFlash;
    private State state = State.Idle;
    private ResourceDeposit assignedNode;
    private Collider nodeCollider;
    private Collider keepCollider;
    private int carried;
    private ResourceType carriedType = ResourceType.Sand;
    private float stateTimer;
    private float idleSeekTimer;
    private Vector3 shovelRestEuler; // X/Y preserved exactly as authored - only Z ever gets driven
    private float shovelAnimTimer; // time SINCE ENTERING Harvesting, not global Time.time - starts every cycle at rest

    /// <summary>
    /// carryCapacity, doubled if Worker Shovels has been purchased. Read
    /// live rather than baked in at spawn, so a purchase mid-game benefits
    /// every worker immediately - including one already out gathering.
    /// </summary>
    private int EffectiveCarryCapacity =>
        KeepWorkerRecruiter.Instance != null && KeepWorkerRecruiter.Instance.ShovelsPurchased
            ? carryCapacity * 2
            : carryCapacity;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        selfHealth = GetComponent<Health>();
        hitFlash = GetComponentInChildren<HitFlash>();
        if (selfHealth != null)
            selfHealth.OnDeath += HandleDeath;

        if (shovelPivot != null)
            shovelRestEuler = shovelPivot.localEulerAngles;
    }

    private void Start()
    {
        if (Keep.Instance != null)
            keepCollider = Keep.Instance.GetComponentInChildren<Collider>();
    }

private void OnDestroy()
    {
        // Free the node the moment this worker leaves the world, so a
        // replacement recruit can take over the deposit immediately.
        if (assignedNode != null)
            assignedNode.Release(this);

        if (selfHealth != null)
            selfHealth.OnDeath -= HandleDeath;
    }

    /// <summary>
    /// Workers had no death handling of their own at all before this -
    /// they relied entirely on the generic DestroyOnDeath component for an
    /// instant, animation-free vanish. This mirrors SoldierAI's approach:
    /// stop, go visibly red, topple, linger as a corpse, then clear away.
    /// </summary>
    private void HandleDeath()
    {
        if (hitFlash != null)
            hitFlash.SetSustainedTint(Color.red, 1f);

        if (agent.enabled && agent.isOnNavMesh)
            agent.isStopped = true;
        agent.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>())
            c.enabled = false;

        StartCoroutine(DeathSequence());
    }

    private System.Collections.IEnumerator DeathSequence()
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


    private void Update()
    {
        if (selfHealth != null && selfHealth.IsDead)
            return;

        switch (state)
        {
            case State.MovingToNode: TickMovingToNode(); break;
            case State.Harvesting: TickHarvesting(); break;
            case State.MovingToKeep: TickMovingToKeep(); break;
            case State.Depositing: TickDepositing(); break;
            case State.Idle: TickIdle(); break;
        }

        if (carryVisual != null && carryVisual.activeSelf != (carried > 0))
            carryVisual.SetActive(carried > 0);

        TickShovelAnimation();
    }

    /// <summary>
    /// Swings the shovel between shovelRestAngle and shovelDigAngle (Z only
    /// - X/Y stay exactly as authored) for the whole time this worker is
    /// actively Harvesting; snaps back to rest the instant it isn't. No-op
    /// entirely if shovelPivot was never assigned (every base Worker).
    /// </summary>
private void TickShovelAnimation()
    {
        if (shovelPivot == null)
            return;

        float z;
        if (state == State.Harvesting)
        {
            shovelAnimTimer += Time.deltaTime;

            // (1 - cos)/2 instead of a raw sine: at timer=0 this evaluates
            // to exactly 0, landing precisely on shovelRestAngle the moment
            // Harvesting begins, then eases up to shovelDigAngle and back.
            // A raw sine driven off THIS SAME local timer would have had
            // the identical zero-start problem the old Time.time version
            // did, just moved one step over - the fix is really that this
            // timer resets to 0 on every fresh entry into Harvesting
            // (see EnterState), not merely which wave shape is used.
            float t = 0.5f - 0.5f * Mathf.Cos(shovelAnimTimer * shovelSwingSpeed);
            z = Mathf.Lerp(shovelRestAngle, shovelDigAngle, t);
        }
        else
        {
            z = shovelRestAngle;
        }

        shovelPivot.localRotation = Quaternion.Euler(shovelRestEuler.x, shovelRestEuler.y, z);
    }

    /// <summary>Player right-clicked a resource node with this worker selected.</summary>
/// <summary>Read-only - lets an external swap (e.g. the Worker Shovels visual upgrade) re-assign the replacement worker to the same node.</summary>
    public ResourceDeposit AssignedNode => assignedNode;

public void AssignDeposit(ResourceDeposit node)
    {
        // Switching nodes hands the old one back to the pool immediately.
        if (assignedNode != null && assignedNode != node)
            assignedNode.Release(this);

        assignedNode = node;
        nodeCollider = node != null ? node.GetComponentInChildren<Collider>() : null;

        if (assignedNode != null)
        {
            assignedNode.Claim(this);
            EnterState(State.MovingToNode);
        }
    }

    /// <summary>Player right-clicked plain ground: a direct order is law - cancel the gather loop.</summary>
public void OnManualMoveOrder()
    {
        if (assignedNode != null)
            assignedNode.Release(this);

        assignedNode = null;
        nodeCollider = null;
        EnterState(State.Idle);
    }

    private void EnterState(State newState)
    {
        if (logStateTransitions)
            Debug.Log(name + ": " + state + " -> " + newState);

        state = newState;

        switch (newState)
        {
            case State.Idle:
                // Hand the agent back to plain movement - crucial when the
                // interruption arrives mid-Harvest, where isStopped is true.
                agent.isStopped = false;
                // Try immediately rather than waiting out the full interval -
                // a freshly spawned or just-emptied-out worker shouldn't
                // stand around for a second and a half before it reacts.
                idleSeekTimer = 0f;
                break;

            case State.MovingToNode:
                agent.isStopped = false;
                if (nodeCollider != null)
                    agent.SetDestination(nodeCollider.ClosestPoint(transform.position));
                break;

            case State.Harvesting:
                agent.isStopped = true;
                stateTimer = harvestDuration;
                shovelAnimTimer = 0f; // fresh cycle - TickShovelAnimation starts exactly at rest
                break;

            case State.MovingToKeep:
                agent.isStopped = false;
                if (keepCollider != null)
                    agent.SetDestination(keepCollider.ClosestPoint(transform.position));
                break;

            case State.Depositing:
                agent.isStopped = true;
                stateTimer = depositDuration;
                break;
        }
    }

    private void TickMovingToNode()
    {
        // Node depleted (destroyed itself) while we walked to it.
        if (assignedNode == null)
        {
            EnterState(carried > 0 ? State.MovingToKeep : State.Idle);
            return;
        }

        if (WithinRange(nodeCollider))
            EnterState(State.Harvesting);
    }

private void TickHarvesting()
    {
        if (assignedNode == null)
        {
            EnterState(carried > 0 ? State.MovingToKeep : State.Idle);
            return;
        }

        // Shoved off the node mid-harvest? (Even a stopped agent can be
        // displaced by other agents' avoidance.) Harvesting only counts
        // while actually AT the node - walk back and start over.
        if (!WithinRange(nodeCollider))
        {
            EnterState(State.MovingToNode);
            return;
        }

        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
        {
            // Capture the type BEFORE harvesting - a Harvest that empties the
            // node destroys it, and we still need to know what we're carrying.
            carriedType = assignedNode.ResourceType;
            carried += assignedNode.Harvest(EffectiveCarryCapacity - carried);
            EnterState(State.MovingToKeep);
        }
    }

    private void TickMovingToKeep()
    {
        if (keepCollider == null)
        {
            EnterState(State.Idle);
            return;
        }

        if (WithinRange(keepCollider))
            EnterState(State.Depositing);
    }

    private void TickDepositing()
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer > 0f)
            return;

        if (carried > 0 && ResourceManager.Instance != null)
            ResourceManager.Instance.Add(carriedType, carried);
        carried = 0;

        // The loop self-sustains: back to the node if it still exists.
        EnterState(assignedNode != null ? State.MovingToNode : State.Idle);
    }

private void TickIdle()
    {
        if (!autoSeekWork)
            return;

        // A manual move order (right-click ground) still in progress takes
        // priority - don't fight it by snatching the worker back into a
        // gather loop mid-repositioning. Once the agent has genuinely
        // stopped moving (including a freshly spawned worker, which was
        // never moving in the first place), it's fair game.
        if (agent.pathPending || agent.remainingDistance > agent.stoppingDistance + 0.1f)
            return;

        idleSeekTimer -= Time.deltaTime;
        if (idleSeekTimer > 0f)
            return;
        idleSeekTimer = idleSeekInterval;

        TryAutoAssignNearestNode();
    }

private void TryAutoAssignNearestNode()
    {
        ResourceDeposit[] allNodes = FindObjectsByType<ResourceDeposit>(FindObjectsSortMode.None);

        ResourceDeposit nearest = null;
        float bestDistance = float.MaxValue;

        foreach (ResourceDeposit node in allNodes)
        {
            if (node.IsEmpty)
                continue;

            // One worker per node - taken nodes are invisible to everyone
            // else, which spreads the workforce evenly across deposits
            // instead of piling onto whichever node is nearest the Keep.
            if (node.IsClaimedByOther(this))
                continue;

            float distance = Vector3.Distance(transform.position, node.transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = node;
            }
        }

        if (nearest != null)
            AssignDeposit(nearest); // same entry point the player's right-click uses
    }


    private bool WithinRange(Collider targetCollider)
    {
        if (targetCollider == null)
            return false;

        Vector3 closest = targetCollider.ClosestPoint(transform.position);
        return Vector3.Distance(transform.position, closest) <= interactRange;
    }
}
