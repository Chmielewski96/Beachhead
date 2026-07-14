using UnityEngine;

/// <summary>
/// Stamped onto every player-placed building at placement time by the
/// BuildingPlacer. Remembers what it cost and WHEN it was placed, so
/// demolition can refund correctly: 100% if demolished within the SAME
/// build phase it was placed in (free experimentation while planning),
/// 50% otherwise (real commitment once a wave has run).
///
/// The Keep never gets this component (it isn't player-placed), which is
/// what makes it undemolishable - no special-casing needed.
/// </summary>
public class PlacedBuilding : MonoBehaviour
{
    private BuildingData data;
    private int builtDuringBuildPhase = -1; // wave number of the build phase it was placed in; -1 = placed mid-combat

    public void Init(BuildingData buildingData)
    {
        data = buildingData;

        if (WaveManager.Instance != null &&
            WaveManager.Instance.CurrentPhase == WaveManager.Phase.Build)
        {
            builtDuringBuildPhase = WaveManager.Instance.CurrentWaveNumber;
        }
    }

    public void Demolish()
    {
        if (data != null && data.costs != null && ResourceManager.Instance != null)
        {
            bool fullRefund = WaveManager.Instance != null
                && WaveManager.Instance.CurrentPhase == WaveManager.Phase.Build
                && WaveManager.Instance.CurrentWaveNumber == builtDuringBuildPhase;

            foreach (BuildingData.ResourceCost cost in data.costs)
            {
                int refund = fullRefund ? cost.amount : cost.amount / 2;
                if (refund > 0)
                    ResourceManager.Instance.Add(cost.resource, refund);
            }

            Debug.Log("Demolished " + data.buildingName + " (" + (fullRefund ? "100%" : "50%") + " refund).");
        }

        Destroy(gameObject);
    }

/// <summary>True if this building has HP missing (and isn't already rubble).</summary>
    public bool CanRepair()
    {
        Health health = GetComponent<Health>();
        return health != null && !health.IsDead && health.Current < health.Max;
    }

    /// <summary>
    /// Repair pricing: half the build cost, scaled by the fraction of HP
    /// missing - patching a scratch is cheap, rebuilding a near-ruin costs
    /// about half of what a fresh one would (at which point demolish +
    /// rebuild is the break-even alternative, which feels right).
    /// Multi-resource costs scale each component the same way, minimum 1
    /// of each once there's any damage at all - repairs are never free.
    /// </summary>
    public int GetRepairCost(BuildingData.ResourceCost cost, float missingFraction)
    {
        return Mathf.Max(1, Mathf.CeilToInt(cost.amount * 0.5f * missingFraction));
    }

    /// <summary>Panel-friendly one-liner, e.g. "12 Sand + 5 Shells".</summary>
    public string DescribeRepairCost()
    {
        Health health = GetComponent<Health>();
        if (health == null || data == null || data.costs == null)
            return "";

        float missingFraction = 1f - (float)health.Current / health.Max;
        string text = "";
        foreach (BuildingData.ResourceCost cost in data.costs)
        {
            if (text.Length > 0)
                text += " + ";
            text += GetRepairCost(cost, missingFraction) + " " + cost.resource;
        }
        return text;
    }

    /// <summary>
    /// Spends the repair cost ATOMICALLY (all resources or none - same
    /// contract as the placer) and heals to full. Returns false if the
    /// building isn't damaged or any single resource falls short.
    /// </summary>
    public bool TryRepair()
    {
        if (!CanRepair() || data == null || data.costs == null || ResourceManager.Instance == null)
            return false;

        Health health = GetComponent<Health>();
        float missingFraction = 1f - (float)health.Current / health.Max;

        // Affordability check for EVERY resource before spending ANY.
        foreach (BuildingData.ResourceCost cost in data.costs)
        {
            if (ResourceManager.Instance.GetAmount(cost.resource) < GetRepairCost(cost, missingFraction))
                return false;
        }

        foreach (BuildingData.ResourceCost cost in data.costs)
            ResourceManager.Instance.TrySpend(cost.resource, GetRepairCost(cost, missingFraction));

        health.Heal(health.Max - health.Current);
        return true;
    }

}
