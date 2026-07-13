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
}
