using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Lets the Keep recruit additional Workers for shells, with escalating
/// costs (element 0 = the second worker, element 1 = the third...). The
/// worker cap is implied by the cost list length + the starting worker.
/// Counting is done live against the scene, so a dead worker frees a slot
/// and can be replaced (at the tier price for that slot).
/// </summary>
public class KeepWorkerRecruiter : MonoBehaviour
{
    [SerializeField] private GameObject workerPrefab;
    [Tooltip("Shell cost of each additional worker beyond the starting one: element 0 = second worker, element 1 = third, etc.")]
    [SerializeField] private int[] recruitCosts = { 50, 100 };
    [Tooltip("How far around the Keep to look for a walkable spawn spot.")]
    [SerializeField] private float spawnSearchRadius = 6f;

    public int MaxWorkers => recruitCosts.Length + 1;

    /// <summary>Live worker count - exposed for the info panel.</summary>
    public int AliveWorkers
    {
        get { return CountAliveWorkers(); }
    }

    /// <summary>What the NEXT recruit would cost right now.</summary>
    public int NextRecruitCost
    {
        get { return recruitCosts[Mathf.Clamp(CountAliveWorkers() - 1, 0, recruitCosts.Length - 1)]; }
    }

public bool TryRecruit()
    {
        if (workerPrefab == null)
            return false;

        int alive = CountAliveWorkers();
        if (alive >= MaxWorkers)
        {
            Debug.Log("Worker limit reached (" + MaxWorkers + ").");
            return false;
        }

        // alive=1 -> buying the 2nd (index 0), alive=2 -> the 3rd (index 1).
        // If ALL workers died, the replacement costs the cheapest tier.
        int cost = recruitCosts[Mathf.Clamp(alive - 1, 0, recruitCosts.Length - 1)];

        if (ResourceManager.Instance != null &&
            !ResourceManager.Instance.TrySpend(ResourceType.Shells, cost))
        {
            // Spelled out explicitly because the common confusion is
            // 'I have enough shells' when the price actually stepped up a
            // tier since the last recruit (e.g. worker #3 costs more than
            // #2) - this makes the ACTUAL required price and current
            // worker count unambiguous rather than a silent refusal.
            Debug.Log("Recruit failed: need " + cost + " shells for worker #" + (alive + 1)
                + " (have " + ResourceManager.Instance.GetAmount(ResourceType.Shells) + "). Current workers: " + alive + "/" + MaxWorkers + ".");
            return false;
        }

        Instantiate(workerPrefab, FindSpawnPosition(), Quaternion.identity);
        Debug.Log("Worker recruited for " + cost + " shells (" + (alive + 1) + "/" + MaxWorkers + ").");
        return true;
    }

    private int CountAliveWorkers()
    {
        int count = 0;
        foreach (WorkerAI worker in FindObjectsByType<WorkerAI>(FindObjectsSortMode.None))
        {
            Health health = worker.GetComponent<Health>();
            if (health == null || !health.IsDead)
                count++;
        }
        return count;
    }

    private Vector3 FindSpawnPosition()
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector2 dir = Random.insideUnitCircle.normalized;
            Vector3 candidate = transform.position + new Vector3(dir.x, 0f, dir.y) * spawnSearchRadius;
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                return hit.position;
        }
        return transform.position; // worst case; should essentially never happen
    }
}
