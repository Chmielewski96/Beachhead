using UnityEngine;

/// <summary>
/// Static display info for UnitInfoPanel - name and a short ability blurb,
/// authored per prefab. Max HP and damage aren't duplicated here; the panel
/// reads those live from Health/SoldierAI so they can never drift out of
/// sync with the unit's actual stats.
/// </summary>
public class UnitInfo : MonoBehaviour
{
    [SerializeField] private string displayName = "Unit";
    [TextArea]
    [SerializeField] private string abilityDescription = "";

    public string DisplayName => displayName;
    public string AbilityDescription => abilityDescription;
}
