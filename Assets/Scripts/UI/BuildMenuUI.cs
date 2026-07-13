using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Left-side build menu: lists every placeable building with its hotkey
/// and live costs (read straight from the BuildingData assets, so any
/// rebalance shows up automatically), plus the demolish controls. Pure
/// reference panel - the hotkeys themselves live in BuildingPlacer.
/// </summary>
public class BuildMenuUI : MonoBehaviour
{
    [Tooltip("Same order as BuildingPlacer's Debug Buildings array - entry 0 is hotkey 1, etc.")]
    [SerializeField] private BuildingData[] buildings;
    [SerializeField] private TMP_Text label;
    [Tooltip("The visible panel content to show/hide with B. This must NOT be the GameObject this script itself lives on - a disabled GameObject stops receiving Update(), so B could never reopen it. Put this script on an always-active object (e.g. the Canvas) and assign the panel here.")]
    [SerializeField] private GameObject panelRoot;


    private void Start()
    {
        RebuildText();
    }

private void Update()
    {
        if (Input.GetKeyDown(KeyCode.B) && panelRoot != null)
            panelRoot.SetActive(!panelRoot.activeSelf);
    }


    private void RebuildText()
    {
        if (label == null || buildings == null)
            return;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("<b>BUILD</b>");

        for (int i = 0; i < buildings.Length; i++)
        {
            if (buildings[i] == null)
                continue;

            string name = string.IsNullOrEmpty(buildings[i].buildingName)
                ? buildings[i].name
                : buildings[i].buildingName;

            sb.Append("[").Append(i + 1).Append("] ").Append(name).Append(" - ").AppendLine(FormatCosts(buildings[i]));
        }

        sb.AppendLine();
        sb.AppendLine("[X] Demolish - 50% refund");
        sb.Append("      (100% in same build phase)");

        label.text = sb.ToString();
    }

    private string FormatCosts(BuildingData data)
    {
        if (data.costs == null || data.costs.Length == 0)
            return "free";

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < data.costs.Length; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(data.costs[i].amount).Append(" ").Append(data.costs[i].resource.ToString().ToLower());
        }
        return sb.ToString();
    }
}
