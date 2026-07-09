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
    }

    public void MoveTo(Vector3 destination)
    {
        agent.SetDestination(destination);
    }
}
