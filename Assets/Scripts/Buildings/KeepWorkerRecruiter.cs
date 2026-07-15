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
    private void Awake()
    {
        // Component-level singleton guard (Destroy(this), never
        // Destroy(gameObject)) - this component lives ON the Keep itself,
        // and destroying the whole GameObject would take the Keep with it
        // if this were ever accidentally duplicated.
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    [SerializeField] private GameObject workerPrefab;
    [Tooltip("Shell cost of each additional worker beyond the starting one: element 0 = second worker, element 1 = third, etc.")]
    [SerializeField] private int[] recruitCosts = { 50, 100 };
    [Tooltip("How far around the Keep to look for a walkable spawn spot.")]
    [SerializeField] private float spawnSearchRadius = 6f;

    [Header("Worker Shovels (one-time, permanent, affects ALL workers)")]
    [SerializeField] private int shovelsCost = 250;
    [Tooltip("Visual swap applied to every worker the instant Shovels is purchased - present workers in place, future recruits spawn as this directly.")]
    [SerializeField] private GameObject upgradedWorkerPrefab;

    public static KeepWorkerRecruiter Instance { get; private set; }
    public bool ShovelsPurchased { get; private set; }
    public int ShovelsCost => shovelsCost;

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

        GameObject prefabToSpawn = (ShovelsPurchased && upgradedWorkerPrefab != null) ? upgradedWorkerPrefab : workerPrefab;
        Instantiate(prefabToSpawn, FindSpawnPosition(), Quaternion.identity);
        Debug.Log("Worker recruited for " + cost + " shells (" + (alive + 1) + "/" + MaxWorkers + ").");
        return true;
    }

/// <summary>
    /// One-time, permanent: doubles every worker's sand-per-trip, present
    /// and future. Deliberately NOT baked into each WorkerAI's own
    /// carryCapacity field at spawn time - WorkerAI reads this flag live
    /// (via Instance) every time it matters, so a purchase mid-game
    /// immediately benefits workers already out gathering, not just ones
    /// recruited afterward.
    /// </summary>
    public bool TryPurchaseShovels()
    {
        if (ShovelsPurchased)
            return false;

        if (ResourceManager.Instance != null &&
            !ResourceManager.Instance.TrySpend(ResourceType.Shells, shovelsCost))
            return false;

        ShovelsPurchased = true;
        SwapExistingWorkersToUpgradedModel();
        return true;
    }

    /// <summary>
    /// Every currently-alive worker gets the new model in place - same
    /// position, same rotation, and if it was actively gathering, the SAME
    /// node (a fresh AssignDeposit call). What's lost is only whatever it
    /// was carrying mid-trip and any in-progress harvest/deposit timer -
    /// an acceptable one-time hiccup for a purely cosmetic swap, and the
    /// same tradeoff GarrisonBuilding's own specialization swap already
    /// makes for troops.
    /// </summary>
    private void SwapExistingWorkersToUpgradedModel()
    {
        if (upgradedWorkerPrefab == null)
            return;

        WorkerAI[] existingWorkers = FindObjectsByType<WorkerAI>(FindObjectsSortMode.None);
        foreach (WorkerAI worker in existingWorkers)
        {
            Health health = worker.GetComponent<Health>();
            if (health != null && health.IsDead)
                continue; // a corpse mid-topple doesn't need upgrading

            if (worker.IsUpgradedModel)
                continue; // already correct - don't needlessly destroy-and-respawn it

            Vector3 position = worker.transform.position;
            Quaternion rotation = worker.transform.rotation;
            ResourceDeposit node = worker.AssignedNode;

            Destroy(worker.gameObject);

            GameObject replacement = Instantiate(upgradedWorkerPrefab, position, rotation);
            WorkerAI replacementAI = replacement.GetComponent<WorkerAI>();
            if (replacementAI != null && node != null)
                replacementAI.AssignDeposit(node);
        }
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
