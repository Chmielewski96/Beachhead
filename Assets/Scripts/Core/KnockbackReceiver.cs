using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Lets a NavMeshAgent-driven unit be knocked back. Agents ignore physics
/// forces entirely, so knockback must be fed through agent.Move - which
/// also keeps the shove ON the NavMesh (no getting punched through walls
/// or off the map). Attackers just call ApplyKnockback; units without this
/// component simply can't be shoved (buildings, for instance).
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class KnockbackReceiver : MonoBehaviour
{
    private NavMeshAgent agent;
    private Coroutine active;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    public void ApplyKnockback(Vector3 direction, float distance, float duration)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            return;
        direction.Normalize();

        if (active != null)
            StopCoroutine(active);
        active = StartCoroutine(KnockbackRoutine(direction, distance, duration));
    }

    private IEnumerator KnockbackRoutine(Vector3 direction, float distance, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
                yield break; // died or got displaced mid-shove - just stop

            float dt = Time.deltaTime;
            elapsed += dt;
            agent.Move(direction * (distance * dt / duration));
            yield return null;
        }

        active = null;
    }
}
