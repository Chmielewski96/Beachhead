using UnityEngine;

/// <summary>
/// Full-heals every PLACED building (Wall, Tower, Garrison - anything with
/// a PlacedBuilding component) the instant a wave is cleared. The Keep is
/// naturally excluded: it has no PlacedBuilding component (it's
/// undemolishable and was never placed via BuildingPlacer), so this never
/// touches it without needing an explicit exclusion check.
/// </summary>
public class WaveEndBuildingHealer : MonoBehaviour
{
    private void OnEnable()
    {
        if (WaveManager.Instance != null)
            WaveManager.Instance.OnWaveCleared += HandleWaveCleared;
    }

    private void OnDisable()
    {
        if (WaveManager.Instance != null)
            WaveManager.Instance.OnWaveCleared -= HandleWaveCleared;
    }

    private void HandleWaveCleared(int waveNumber)
    {
        PlacedBuilding[] buildings = FindObjectsByType<PlacedBuilding>(FindObjectsSortMode.None);
        foreach (PlacedBuilding building in buildings)
        {
            Health health = building.GetComponent<Health>();
            if (health == null || health.IsDead)
                continue;

            health.Heal(health.Max); // Heal() clamps to Max on its own - always tops off fully regardless of current HP
        }
    }
}
