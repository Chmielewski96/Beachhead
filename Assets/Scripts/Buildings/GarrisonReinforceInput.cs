using UnityEngine;

/// <summary>
/// Press U while a garrison building is SELECTED to buy a Reinforce upgrade.
/// Selection-based rather than hover-based, so it works from anywhere on
/// the map. Insufficient shells flashes the shell counter via the
/// usual OnSpendFailed event - no extra wiring needed.
/// </summary>
public class GarrisonReinforceInput : MonoBehaviour
{
private void Update()
    {
        if (PauseManager.Instance != null && PauseManager.Instance.IsPaused)
            return;

        if (IntroSequence.Instance != null && IntroSequence.Instance.IsIntroActive)
            return;

        if (!Input.GetKeyDown(KeyCode.U))
            return;

        if (SelectionManager.Instance == null || SelectionManager.Instance.SelectedBuilding == null)
            return;

        BuildingSelectable building = SelectionManager.Instance.SelectedBuilding;
        GarrisonBuilding garrison = building.GetComponentInParent<GarrisonBuilding>();
        if (garrison == null)
            garrison = building.GetComponentInChildren<GarrisonBuilding>();
        if (garrison == null)
            return;

        if (garrison.TryReinforce())
            Debug.Log("Garrison reinforced: cap is now " + garrison.CurrentCap);
        else if (!garrison.CanReinforce)
            Debug.Log("Garrison already at maximum reinforcement.");
    }
}
