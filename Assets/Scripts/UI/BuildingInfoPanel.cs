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
    [Tooltip("VFX spawned at the point when a Defend Point order lands - reuses the same marker as resource-node assignment, for the same 'order received' feel.")]
    [SerializeField] private GameObject defendPointMarkerPrefab;
    [Tooltip("Same flat disc mesh/material as the Tower/Garrison PLACEMENT ghost rings, reused here for the command-range preview during Defend Point targeting.")]
    [SerializeField] private Mesh rangeRingMesh;
    [SerializeField] private Material rangeRingMaterial;
    [Tooltip("Button tint for the squad's CURRENT stance.")]
    [SerializeField] private Color stanceActiveColor = new Color(0.85f, 0.65f, 0.2f);
    [Tooltip("Button tint for the stances not currently active.")]
    [SerializeField] private Color stanceIdleColor = new Color(0.35f, 0.35f, 0.4f);

    [Header("Tower Upgrades (choose-one, side by side, like Garrison's Guardian/Hunter)")]
    [Tooltip("Shown only while a Tower that CAN upgrade (data.cannonUpgradeData != null) is selected.")]
    [SerializeField] private Button cannonButton;
    [SerializeField] private TMP_Text cannonButtonLabel;
    [Tooltip("Shown only while a Tower that CAN upgrade to Lens (data.lensUpgradeData != null) is selected.")]
    [SerializeField] private Button lensButton;
    [SerializeField] private TMP_Text lensButtonLabel;

    [Header("Keep: Worker Shovels")]
    [Tooltip("Shown only while the Keep is selected AND shovels haven't been purchased yet - one-time, permanent.")]
    [SerializeField] private Button shovelsButton;
    [SerializeField] private TMP_Text shovelsButtonLabel;

    private BuildingSelectable current;
    private GarrisonBuilding currentGarrison;
    private Tower currentTower;
    private KeepWorkerRecruiter currentKeep;

    // Defend Point targeting holds its OWN garrison reference: the ground
    // click that places the point usually also deselects the building
    // (SelectionManager sees a click on nothing), which nulls
    // currentGarrison - the order must not die with the selection.
    private GarrisonBuilding targetingGarrison;
    private int targetingStartFrame;

    /// <summary>
    /// True from the moment "Defend Point" is clicked until it succeeds or
    /// is cancelled - SelectionManager checks this the same way it already
    /// checks BuildingPlacer/DemolishInput, so a ground click that MISSES
    /// the valid area doesn't also deselect the garrison out from under
    /// the targeting mode. Without this, the panel (and this ring) closed
    /// on the very first failed click regardless of the "stay in targeting
    /// mode, just click again" logic below - that retry was already silently
    /// broken, since the UI telling the player to retry vanished immediately.
    /// </summary>
    public static bool IsTargetingDefendPoint { get; private set; }

    // World-space preview of the garrison's command range while Defend
    // Point targeting is active - built lazily from the same Cylinder
    // mesh + RangeZone material BuildingPlacer's own ghost rings use, so
    // it reads as the same visual language rather than a new one.
    private GameObject commandRangeRing;

    // Same idea, separate object: a world-space preview of a SELECTED
    // tower's actual range (data.range - applies to Cannon/Lens too, not
    // just the base tower's projectile reach). Kept as its own GameObject
    // rather than sharing commandRangeRing, since the two can never be
    // needed at the same time but ARE conceptually different previews for
    // different building types.
    private GameObject towerRangeRing;

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
        if (cannonButton != null)
            cannonButton.onClick.AddListener(ChooseCannonUpgrade);
        if (lensButton != null)
            lensButton.onClick.AddListener(ChooseLensUpgrade);
        if (shovelsButton != null)
            shovelsButton.onClick.AddListener(ChooseShovels);

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
        if (cannonButton != null)
            cannonButton.onClick.RemoveListener(ChooseCannonUpgrade);
        if (lensButton != null)
            lensButton.onClick.RemoveListener(ChooseLensUpgrade);
        if (shovelsButton != null)
            shovelsButton.onClick.RemoveListener(ChooseShovels);

        if (commandRangeRing != null)
            Destroy(commandRangeRing);
        if (towerRangeRing != null)
            Destroy(towerRangeRing);
    }

    private void ChooseCannonUpgrade()
    {
        if (currentTower != null)
            currentTower.TryUpgradeToCannon();
    }

    private void ChooseLensUpgrade()
    {
        if (currentTower != null)
            currentTower.TryUpgradeToLens();
    }

    private void ChooseShovels()
    {
        if (currentKeep != null)
            currentKeep.TryPurchaseShovels();
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
        IsTargetingDefendPoint = true;

        ShowCommandRangeRing(currentGarrison);
    }

    private void CancelDefendPointTargeting()
    {
        targetingGarrison = null;
        IsTargetingDefendPoint = false;
        if (commandRangeRing != null)
            commandRangeRing.SetActive(false);
    }

    /// <summary>
    /// Lazily builds a flat translucent disc (same mesh/material as the
    /// Tower/Garrison PLACEMENT ghost rings) sized to the garrison's actual
    /// commandRange, centered on the garrison itself - not a UI element,
    /// so it's kept OUT of the Canvas hierarchy entirely (a UI RectTransform
    /// parent would apply Canvas scaling to what needs to be a real,
    /// unscaled world-space size).
    /// </summary>
private void ShowCommandRangeRing(GarrisonBuilding garrison)
    {
        if (commandRangeRing == null)
        {
            commandRangeRing = new GameObject("CommandRangeRing", typeof(MeshFilter), typeof(MeshRenderer));
            commandRangeRing.transform.SetParent(null);
            commandRangeRing.GetComponent<MeshFilter>().sharedMesh = rangeRingMesh;
            commandRangeRing.GetComponent<MeshRenderer>().sharedMaterial = rangeRingMaterial;
        }

        commandRangeRing.transform.position = garrison.transform.position;
        commandRangeRing.transform.localScale = new Vector3(garrison.CommandRange * 2f, 0.01f, garrison.CommandRange * 2f);
        commandRangeRing.SetActive(true);
    }

    /// <summary>Same pattern as ShowCommandRangeRing, for a selected tower's own range.</summary>
    private void ShowTowerRangeRing(Tower tower)
    {
        if (towerRangeRing == null)
        {
            towerRangeRing = new GameObject("TowerRangeRing", typeof(MeshFilter), typeof(MeshRenderer));
            towerRangeRing.transform.SetParent(null);
            towerRangeRing.GetComponent<MeshFilter>().sharedMesh = rangeRingMesh;
            towerRangeRing.GetComponent<MeshRenderer>().sharedMaterial = rangeRingMaterial;
        }

        towerRangeRing.transform.position = tower.transform.position;
        towerRangeRing.transform.localScale = new Vector3(tower.Range * 2f, 0.01f, tower.Range * 2f);
        towerRangeRing.SetActive(true);
    }

    private void HideTowerRangeRing()
    {
        if (towerRangeRing != null)
            towerRangeRing.SetActive(false);
    }

    private void HandleSelectionChanged(BuildingSelectable building)
    {
        current = building;
        if (current == null)
        {
            currentGarrison = null;
            currentTower = null;
            currentKeep = null;
            SetSpecializeButtonsVisible(false);
            SetStanceButtonsVisible(false);
            SetCannonButtonVisible(false);
            SetLensButtonVisible(false);
            SetShovelsButtonVisible(false);
            HideTowerRangeRing();
        }
        if (panelRoot != null)
            panelRoot.SetActive(current != null);
    }

    private void Update()
    {
        TickDefendPointTargeting();

        // ReferenceEquals, NOT ==: Unity overrides == on UnityEngine.Object
        // to treat a DESTROYED-but-still-referenced object as "null" too -
        // which meant this check used to catch that case FIRST and return
        // early, before the destroy-cleanup branch right below it ever got
        // a chance to run. That branch was dead code: current was never
        // actually reset, panelRoot never deactivated, and a just-upgraded
        // (destroyed) tower's stale info just sat there until the player
        // selected something else and overwrote current for real.
        if (ReferenceEquals(current, null))
            return;

        // The building might have been destroyed while selected - THIS is
        // exactly the case Unity's overridden == is built to catch.
        if (current == null)
        {
            HandleSelectionChanged(null);
            return;
        }

        currentGarrison = null;
        currentTower = null;
        currentKeep = null;

        KeepWorkerRecruiter keep = current.GetComponentInParent<KeepWorkerRecruiter>();
        if (keep != null)
        {
            currentKeep = keep;
            SetSpecializeButtonsVisible(false);
            SetStanceButtonsVisible(false);
            SetCannonButtonVisible(false);
            SetLensButtonVisible(false);
            HideTowerRangeRing();
            UpdateShovelsButton(keep);
            SetText("The Keep", BuildKeepText(keep));
            return;
        }

        GarrisonBuilding garrison = current.GetComponentInParent<GarrisonBuilding>();
        if (garrison != null)
        {
            currentGarrison = garrison;
            UpdateSpecializeButtons(garrison);
            UpdateStanceButtons(garrison);
            SetCannonButtonVisible(false);
            SetLensButtonVisible(false);
            SetShovelsButtonVisible(false);
            HideTowerRangeRing();
            SetText("Garrison", BuildRepairLine(garrison) + BuildGarrisonText(garrison));
            return;
        }

        Tower tower = current.GetComponentInParent<Tower>();
        if (tower != null)
        {
            currentTower = tower;
            SetSpecializeButtonsVisible(false);
            SetStanceButtonsVisible(false);
            UpdateCannonButton(tower);
            UpdateLensButton(tower);
            SetShovelsButtonVisible(false);
            ShowTowerRangeRing(tower);
            SetText(tower.DisplayName, BuildRepairLine(current));
            return;
        }

        SetSpecializeButtonsVisible(false);
        SetStanceButtonsVisible(false);
        SetCannonButtonVisible(false);
        SetLensButtonVisible(false);
        SetShovelsButtonVisible(false);
        HideTowerRangeRing();
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
            {
                if (defendPointMarkerPrefab != null)
                    Instantiate(defendPointMarkerPrefab, point + Vector3.up * 0.05f, Quaternion.identity);
                CancelDefendPointTargeting();
            }
            // else: out of command range / unwalkable - stay in targeting
            // mode, the player can immediately click somewhere valid.
        }
    }

    private string BuildKeepText(KeepWorkerRecruiter keep)
    {
        string shovelsLine = keep.ShovelsPurchased ? "Worker Shovels: purchased (20 sand/trip)\n" : "";

        if (keep.AliveWorkers >= keep.MaxWorkers)
            return shovelsLine + "Workers: " + keep.AliveWorkers + "/" + keep.MaxWorkers + "  (limit reached)";

        return shovelsLine + "[U] Recruit worker - " + keep.NextRecruitCost + " shells\n"
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

    private void UpdateCannonButton(Tower tower)
    {
        bool show = tower.CanUpgradeToCannon;
        SetCannonButtonVisible(show);
        if (!show)
            return;

        int shells = ResourceManager.Instance != null
            ? ResourceManager.Instance.GetAmount(ResourceType.Shells)
            : 0;

        if (cannonButton != null)
            cannonButton.interactable = shells >= tower.CannonUpgradeCost;
        if (cannonButtonLabel != null)
            cannonButtonLabel.text = "Upgrade: Cannon\n" + tower.CannonUpgradeCost + " shells";
    }

    private void SetCannonButtonVisible(bool visible)
    {
        if (cannonButton != null && cannonButton.gameObject.activeSelf != visible)
            cannonButton.gameObject.SetActive(visible);
    }

    private void UpdateLensButton(Tower tower)
    {
        bool show = tower.CanUpgradeToLens;
        SetLensButtonVisible(show);
        if (!show)
            return;

        int shells = ResourceManager.Instance != null
            ? ResourceManager.Instance.GetAmount(ResourceType.Shells)
            : 0;

        if (lensButton != null)
            lensButton.interactable = shells >= tower.LensUpgradeCost;
        if (lensButtonLabel != null)
            lensButtonLabel.text = "Upgrade: Lens\n" + tower.LensUpgradeCost + " shells";
    }

    private void SetLensButtonVisible(bool visible)
    {
        if (lensButton != null && lensButton.gameObject.activeSelf != visible)
            lensButton.gameObject.SetActive(visible);
    }

    private void UpdateShovelsButton(KeepWorkerRecruiter keep)
    {
        bool show = !keep.ShovelsPurchased;
        SetShovelsButtonVisible(show);
        if (!show)
            return;

        int shells = ResourceManager.Instance != null
            ? ResourceManager.Instance.GetAmount(ResourceType.Shells)
            : 0;

        if (shovelsButton != null)
            shovelsButton.interactable = shells >= keep.ShovelsCost;
        if (shovelsButtonLabel != null)
            shovelsButtonLabel.text = "Worker Shovels\n" + keep.ShovelsCost + " shells";
    }

    private void SetShovelsButtonVisible(bool visible)
    {
        if (shovelsButton != null && shovelsButton.gameObject.activeSelf != visible)
            shovelsButton.gameObject.SetActive(visible);
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
