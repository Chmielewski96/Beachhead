using TMPro;
using UnityEngine;

/// <summary>
/// Bottom-of-screen info panel for the selected building: name, what the
/// U key does for it, the live cost, and current/max counts. Driven by
/// SelectionManager.OnBuildingSelectionChanged; refreshes every frame
/// while visible because the numbers change under it (troops die, shells
/// get spent, reinforcements land).
/// </summary>
public class BuildingInfoPanel : MonoBehaviour
{
    [Tooltip("Root of the panel - toggled with selection. Can be this same GameObject.")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private TMP_Text detailLabel;

    private BuildingSelectable current;

    private void Start()
    {
        if (SelectionManager.Instance != null)
            SelectionManager.Instance.OnBuildingSelectionChanged += HandleSelectionChanged;

        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (SelectionManager.Instance != null)
            SelectionManager.Instance.OnBuildingSelectionChanged -= HandleSelectionChanged;
    }

    private void HandleSelectionChanged(BuildingSelectable building)
    {
        current = building;
        if (panelRoot != null)
            panelRoot.SetActive(current != null);
    }

    private void Update()
    {
        if (current == null)
            return;

        // The building might have been destroyed while selected.
        if (current.gameObject == null)
        {
            HandleSelectionChanged(null);
            return;
        }

        KeepWorkerRecruiter keep = current.GetComponentInParent<KeepWorkerRecruiter>();
        if (keep != null)
        {
            SetText("The Keep", BuildKeepText(keep));
            return;
        }

        GarrisonBuilding garrison = current.GetComponentInParent<GarrisonBuilding>();
        if (garrison != null)
        {
            SetText("Garrison", BuildGarrisonText(garrison));
            return;
        }

        SetText(current.gameObject.name, "");
    }

    private string BuildKeepText(KeepWorkerRecruiter keep)
    {
        if (keep.AliveWorkers >= keep.MaxWorkers)
            return "Workers: " + keep.AliveWorkers + "/" + keep.MaxWorkers + "  (limit reached)";

        return "[U] Recruit worker - " + keep.NextRecruitCost + " shells\n"
             + "Workers: " + keep.AliveWorkers + "/" + keep.MaxWorkers;
    }

    private string BuildGarrisonText(GarrisonBuilding garrison)
    {
        string troops = "Troops: " + garrison.CurrentCount + "/" + garrison.CurrentCap
            + "   Upgrades: " + garrison.ReinforcementsUsed + "/" + garrison.MaxReinforcements;

        if (!garrison.CanReinforce)
            return troops + "  (fully reinforced)";

        return "[U] Reinforce (+1 troop cap) - " + garrison.ReinforceCost + " shells\n" + troops;
    }

    private void SetText(string title, string detail)
    {
        if (nameLabel != null)
            nameLabel.text = title;
        if (detailLabel != null)
            detailLabel.text = detail;
    }
}
