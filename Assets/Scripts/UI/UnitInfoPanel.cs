using TMPro;
using UnityEngine;

/// <summary>
/// Info-only panel for the selected unit: name, current/max HP, damage (if
/// it has any - Workers don't), and a short ability blurb from UnitInfo.
/// Purely inspection - selecting a unit here doesn't let the player
/// command it; SoldierAI/WorkerAI remain fully autonomous either way.
/// Driven by SelectionManager.OnUnitSelectionChanged, refreshed every
/// frame while visible since HP changes under it.
/// </summary>
public class UnitInfoPanel : MonoBehaviour
{
    [Tooltip("Root of the panel - toggled with selection. Can be this same GameObject.")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private TMP_Text detailLabel;

    private Unit current;
    private Health currentHealth;
    private SoldierAI currentSoldierAI;
    private UnitInfo currentInfo;

    private void Start()
    {
        if (SelectionManager.Instance != null)
            SelectionManager.Instance.OnUnitSelectionChanged += HandleSelectionChanged;

        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (SelectionManager.Instance != null)
            SelectionManager.Instance.OnUnitSelectionChanged -= HandleSelectionChanged;
    }

    private void HandleSelectionChanged(Unit unit)
    {
        current = unit;

        if (current == null)
        {
            currentHealth = null;
            currentSoldierAI = null;
            currentInfo = null;
        }
        else
        {
            currentHealth = current.GetComponent<Health>();
            currentSoldierAI = current.GetComponent<SoldierAI>();
            currentInfo = current.GetComponent<UnitInfo>();
        }

        if (panelRoot != null)
            panelRoot.SetActive(current != null);
    }

    private void Update()
    {
        // ReferenceEquals, not ==: see BuildingInfoPanel for why - Unity's
        // overridden == treats a destroyed-but-still-referenced object as
        // null too, which would make the check below unreachable and leave
        // the panel stuck showing a dead unit's stale info forever.
        if (ReferenceEquals(current, null))
            return;

        if (current == null)
        {
            HandleSelectionChanged(null);
            return;
        }

        SetText(BuildName(), BuildDetail());
    }

    private string BuildName()
    {
        return currentInfo != null && !string.IsNullOrEmpty(currentInfo.DisplayName)
            ? currentInfo.DisplayName
            : current.gameObject.name;
    }

    private string BuildDetail()
    {
        string hp = currentHealth != null
            ? "HP: " + currentHealth.Current + "/" + currentHealth.Max
            : "HP: -";

        string damage = currentSoldierAI != null
            ? "Damage: " + currentSoldierAI.Damage
            : "Damage: -";

        string ability = currentInfo != null && !string.IsNullOrEmpty(currentInfo.AbilityDescription)
            ? currentInfo.AbilityDescription
            : "";

        return hp + "\n" + damage + (string.IsNullOrEmpty(ability) ? "" : "\n" + ability);
    }

    private void SetText(string title, string detail)
    {
        if (nameLabel != null)
            nameLabel.text = title;
        if (detailLabel != null)
            detailLabel.text = detail;
    }
}
