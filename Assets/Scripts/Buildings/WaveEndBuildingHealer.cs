using UnityEngine;

/// <summary>
/// Full-heals every PLACED building (Wall, Tower, Garrison - anything with
/// a PlacedBuilding component) AND every friendly unit (Soldier, Guardian,
/// Hunter, Worker) the instant a wave is cleared. The Keep is naturally
/// excluded from the building sweep: it has no PlacedBuilding component
/// (it's undemolishable and was never placed via BuildingPlacer), so this
/// never touches it without needing an explicit exclusion check. Units are
/// swept by their AI component (SoldierAI/WorkerAI) rather than the Unit
/// marker class - Unit isn't actually attached to any of these prefabs in
/// this game (they're autonomous, not individually player-selectable), so
/// it wouldn't catch anything.
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

        HealFriendlyUnits<SoldierAI>();
        HealFriendlyUnits<WorkerAI>();
    }

    private void HealFriendlyUnits<T>() where T : Component
    {
        T[] units = FindObjectsByType<T>(FindObjectsSortMode.None);
        foreach (T unit in units)
        {
            Health health = unit.GetComponent<Health>();
            if (health == null || health.IsDead)
                continue;

            health.Heal(health.Max);
        }
    }
}
