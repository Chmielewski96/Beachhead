using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The entire "commandable" surface of a unit, for now: give it a point,
/// it walks there. Deliberately tiny - selection, orders, and formation
/// logic all live in other scripts and just call MoveTo().
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class UnitMovement : MonoBehaviour
{
    private NavMeshAgent agent;

private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();

        // Crabs and Soldiers randomize their avoidance priority into 30-70 so
        // equal-priority pairs (which never yield to each other and just
        // shove) don't happen among THEM. Player units get a distinct,
        // consistently LOWER range (more assertive - no overlap with 30-70
        // at all), so a worker or commanded unit always gets right-of-way
        // over autonomous crabs/soldiers instead of getting stuck fighting
        // through a patrol formation or a mob.
        agent.avoidancePriority = Random.Range(10, 30);
    }

    public void MoveTo(Vector3 destination)
    {
        agent.SetDestination(destination);
    }
}
