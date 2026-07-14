using UnityEngine;

/// <summary>
/// Press R while a damaged building is SELECTED to repair it to full.
/// Cost: half the build cost times the fraction of HP missing (see
/// PlacedBuilding.TryRepair). Selection-based like the U-key inputs, so
/// it works from anywhere on the map. The Keep has no PlacedBuilding
/// component (it isn't player-placed), which makes it unrepairable the
/// same way it's undemolishable - no special-casing needed.
/// </summary>
public class BuildingRepairInput : MonoBehaviour
{
    private void Update()
    {
        if (PauseManager.Instance != null && PauseManager.Instance.IsPaused)
            return;

        if (IntroSequence.Instance != null && IntroSequence.Instance.IsIntroActive)
            return;

        if (!Input.GetKeyDown(KeyCode.R))
            return;

        if (SelectionManager.Instance == null || SelectionManager.Instance.SelectedBuilding == null)
            return;

        BuildingSelectable building = SelectionManager.Instance.SelectedBuilding;
        PlacedBuilding placed = building.GetComponentInParent<PlacedBuilding>();
        if (placed == null)
            placed = building.GetComponentInChildren<PlacedBuilding>();
        if (placed == null)
            return;

        if (placed.TryRepair())
            Debug.Log("Building repaired to full.");
    }
}
