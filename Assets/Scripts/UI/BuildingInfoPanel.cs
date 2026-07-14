using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Bottom-of-screen info panel for the selected building: name, what the
/// U key does for it, the live cost, and current/max counts. Driven by
/// SelectionManager.OnBuildingSelectionChanged; refreshes every frame
/// while visible because the numbers change under it (troops die, shells
/// get spent, reinforcements land).
///
/// Hosts two garrison control rows:
/// - SPECIALIZATION (choose-one, permanent): Guardians or Hunters.
/// - STANCE (freely switchable): Patrol / Seek &amp; Destroy / Defend Point.
///   Defend Point enters a targeting mode - the next ground click within
///   the garrison's command range posts the squad there. The targeting
///   mode deliberately holds its own garrison reference, so it survives
///   the ground click deselecting the building.
/// </summary>
public class BuildingInfoPanel : MonoBehaviour
{
    [Tooltip("Root of the panel - toggled with selection. Can be this same GameObject.")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private TMP_Text detailLabel;

    [Header("Garrison Specialization")]
    [Tooltip("Choose-one upgrade buttons - shown only while an UNSPECIALIZED garrison is selected.")]
    [SerializeField] private Button guardianButton;
    [SerializeField] private Button hunterButton;
    [SerializeField] private TMP_Text guardianButtonLabel;
    [SerializeField] private TMP_Text hunterButtonLabel;

    [Header("Garrison Stances")]
    [SerializeField] private Button patrolButton;
    [SerializeField] private Button seekButton;
    [SerializeField] private Button defendButton;
    [SerializeField] private TMP_Text defendButtonLabel;
    [Tooltip("Layers a Defend Point click can land on - typically just Ground.")]
    [SerializeField] private LayerMask groundMask;
    [Tooltip("Button tint for the squad's CURRENT stance.")]
    [SerializeField] private Color stanceActiveColor = new Color(0.85f, 0.65f, 0.2f);
    [Tooltip("Button tint for the stances not currently active.")]
    [SerializeField] private Color stanceIdleColor = new Color(0.35f, 0.35f, 0.4f);

    private BuildingSelectable current;
    private GarrisonBuilding currentGarrison;

    // Defend Point targeting holds its OWN garrison reference: the ground
    // click that places the point usually also deselects the building
    // (SelectionManager sees a click on nothing), which nulls
    // currentGarrison - the order must not die with the selection.
    private GarrisonBuilding targetingGarrison;
    private int targetingStartFrame;

    private void Start()
    {
        if (SelectionManager.Instance != null)
            SelectionManager.Instance.OnBuildingSelectionChanged += HandleSelectionChanged;

        if (guardianButton != null)
            guardianButton.onClick.AddListener(ChooseGuardians);
        if (hunterButton != null)
            hunterButton.onClick.AddListener(ChooseHunters);

        if (patrolButton != null)
            patrolButton.onClick.AddListener(ChoosePatrol);
        if (seekButton != null)
            seekButton.onClick.AddListener(ChooseSeekAndDestroy);
        if (defendButton != null)
            defendButton.onClick.AddListener(BeginDefendPointTargeting);

        SetSpecializeButtonsVisible(false);
        SetStanceButtonsVisible(false);

        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        if (SelectionManager.Instance != null)
            SelectionManager.Instance.OnBuildingSelectionChanged -= HandleSelectionChanged;

        if (guardianButton != null)
            guardianButton.onClick.RemoveListener(ChooseGuardians);
        if (hunterButton != null)
            hunterButton.onClick.RemoveListener(ChooseHunters);
        if (patrolButton != null)
            patrolButton.onClick.RemoveListener(ChoosePatrol);
        if (seekButton != null)
            seekButton.onClick.RemoveListener(ChooseSeekAndDestroy);
        if (defendButton != null)
            defendButton.onClick.RemoveListener(BeginDefendPointTargeting);
    }

    private void ChooseGuardians()
    {
        if (currentGarrison != null)
            currentGarrison.TrySpecialize(GarrisonBuilding.Specialization.Guardian);
    }

    private void ChooseHunters()
    {
        if (currentGarrison != null)
            currentGarrison.TrySpecialize(GarrisonBuilding.Specialization.Hunter);
    }

    private void ChoosePatrol()
    {
        CancelDefendPointTargeting();
        if (currentGarrison != null)
            currentGarrison.TrySetStance(GarrisonBuilding.Stance.Patrol, Vector3.zero);
    }

    private void ChooseSeekAndDestroy()
    {
        CancelDefendPointTargeting();
        if (currentGarrison != null)
            currentGarrison.TrySetStance(GarrisonBuilding.Stance.SeekAndDestroy, Vector3.zero);
    }

    private void BeginDefendPointTargeting()
    {
        if (currentGarrison == null)
            return;

        targetingGarrison = currentGarrison;
        targetingStartFrame = Time.frameCount; // the button click itself must not count as the placement click
    }

    private void CancelDefendPointTargeting()
    {
        targetingGarrison = null;
    }

    private void HandleSelectionChanged(BuildingSelectable building)
    {
        current = building;
        if (current == null)
        {
            currentGarrison = null;
            SetSpecializeButtonsVisible(false);
            SetStanceButtonsVisible(false);
        }
        if (panelRoot != null)
            panelRoot.SetActive(current != null);
    }

    private void Update()
    {
        TickDefendPointTargeting();

        if (current == null)
            return;

        // The building might have been destroyed while selected.
        if (current.gameObject == null)
        {
            HandleSelectionChanged(null);
            return;
        }

        currentGarrison = null;

        KeepWorkerRecruiter keep = current.GetComponentInParent<KeepWorkerRecruiter>();
        if (keep != null)
        {
            SetSpecializeButtonsVisible(false);
            SetStanceButtonsVisible(false);
            SetText("The Keep", BuildKeepText(keep));
            return;
        }

        GarrisonBuilding garrison = current.GetComponentInParent<GarrisonBuilding>();
        if (garrison != null)
        {
            currentGarrison = garrison;
            UpdateSpecializeButtons(garrison);
            UpdateStanceButtons(garrison);
            SetText("Garrison", BuildRepairLine(garrison) + BuildGarrisonText(garrison));
            return;
        }

        SetSpecializeButtonsVisible(false);
        SetStanceButtonsVisible(false);
        SetText(current.gameObject.name, BuildRepairLine(current));
    }

    /// <summary>
    /// While a Defend Point order is pending: left-click on ground places
    /// it (rejected clicks - out of range, not walkable - keep the mode
    /// alive so the player just clicks again); right-click or Escape
    /// cancels. Runs independently of the selection, on purpose.
    /// </summary>
    private void TickDefendPointTargeting()
    {
        if (targetingGarrison == null)
            return;

        // The garrison might have been destroyed mid-targeting.
        if (targetingGarrison.gameObject == null)
        {
            targetingGarrison = null;
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            CancelDefendPointTargeting();
            return;
        }

        if (!Input.GetMouseButtonDown(0))
            return;
        if (Time.frameCount == targetingStartFrame)
            return; // this is the click that pressed the Defend button itself
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return; // clicked some other UI element - not a placement attempt

        if (MouseWorld.TryGetGroundPoint(groundMask, out Vector3 point))
        {
            if (targetingGarrison.TrySetStance(GarrisonBuilding.Stance.DefendPoint, point))
                CancelDefendPointTargeting();
            // else: out of command range / unwalkable - stay in targeting
            // mode, the player can immediately click somewhere valid.
        }
    }

    private string BuildKeepText(KeepWorkerRecruiter keep)
    {
        if (keep.AliveWorkers >= keep.MaxWorkers)
            return "Workers: " + keep.AliveWorkers + "/" + keep.MaxWorkers + "  (limit reached)";

        return "[U] Recruit worker - " + keep.NextRecruitCost + " shells\n"
             + "Workers: " + keep.AliveWorkers + "/" + keep.MaxWorkers;
    }

/// <summary>
    /// "[R] Repair - 12 Sand" line when the selected building is damaged
    /// and repairable; empty string otherwise (undamaged, or the Keep,
    /// which has no PlacedBuilding). Prepended to the detail text.
    /// </summary>
    private string BuildRepairLine(Component context)
    {
        if (context == null)
            return "";

        PlacedBuilding placed = context.GetComponentInParent<PlacedBuilding>();
        if (placed == null || !placed.CanRepair())
            return "";

        return "[R] Repair - " + placed.DescribeRepairCost() + "\n";
    }


    private string BuildGarrisonText(GarrisonBuilding garrison)
    {
        string troops = "Troops: " + garrison.CurrentCount + "/" + garrison.CurrentCap
            + "   Upgrades: " + garrison.ReinforcementsUsed + "/" + garrison.MaxReinforcements;

        if (!garrison.CanSpecialize)
            troops += "   [" + garrison.CurrentSpecialization + "s]";

        if (!garrison.CanReinforce)
            return troops + "  (fully reinforced)";

        return "[U] Reinforce (+1 troop cap) - " + garrison.ReinforceCost + " shells\n" + troops;
    }

    private void UpdateSpecializeButtons(GarrisonBuilding garrison)
    {
        bool show = garrison.CanSpecialize;
        SetSpecializeButtonsVisible(show);
        if (!show)
            return;

        int shells = ResourceManager.Instance != null
            ? ResourceManager.Instance.GetAmount(ResourceType.Shells)
            : 0;

        if (guardianButton != null)
            guardianButton.interactable = shells >= garrison.GuardianCost;
        if (hunterButton != null)
            hunterButton.interactable = shells >= garrison.HunterCost;

        if (guardianButtonLabel != null)
            guardianButtonLabel.text = "Guardians\n" + garrison.GuardianCost + " shells";
        if (hunterButtonLabel != null)
            hunterButtonLabel.text = "Hunters\n" + garrison.HunterCost + " shells";
    }

    private void UpdateStanceButtons(GarrisonBuilding garrison)
    {
        SetStanceButtonsVisible(true);

        GarrisonBuilding.Stance stance = garrison.CurrentStance;
        SetButtonTint(patrolButton, stance == GarrisonBuilding.Stance.Patrol);
        SetButtonTint(seekButton, stance == GarrisonBuilding.Stance.SeekAndDestroy);
        SetButtonTint(defendButton, stance == GarrisonBuilding.Stance.DefendPoint);

        if (defendButtonLabel != null)
        {
            bool targetingThis = targetingGarrison == garrison;
            defendButtonLabel.text = targetingThis ? "Click ground..." : "Defend Point";
        }
    }

    private void SetButtonTint(Button button, bool active)
    {
        if (button == null || button.image == null)
            return;
        button.image.color = active ? stanceActiveColor : stanceIdleColor;
    }

    private void SetSpecializeButtonsVisible(bool visible)
    {
        if (guardianButton != null && guardianButton.gameObject.activeSelf != visible)
            guardianButton.gameObject.SetActive(visible);
        if (hunterButton != null && hunterButton.gameObject.activeSelf != visible)
            hunterButton.gameObject.SetActive(visible);
    }

    private void SetStanceButtonsVisible(bool visible)
    {
        if (patrolButton != null && patrolButton.gameObject.activeSelf != visible)
            patrolButton.gameObject.SetActive(visible);
        if (seekButton != null && seekButton.gameObject.activeSelf != visible)
            seekButton.gameObject.SetActive(visible);
        if (defendButton != null && defendButton.gameObject.activeSelf != visible)
            defendButton.gameObject.SetActive(visible);
    }

    private void SetText(string title, string detail)
    {
        if (nameLabel != null)
            nameLabel.text = title;
        if (detailLabel != null)
            detailLabel.text = detail;
    }
}
