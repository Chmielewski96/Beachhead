using UnityEngine;

/// <summary>
/// Demolish MODE - press X to enter (not just an instant hover-check), a
/// hint prompts you to click a building, then left-click any player-placed
/// building to demolish it (refund logic lives in PlacedBuilding). The
/// mode PERSISTS across multiple demolishes, same as building placement
/// persists across multiple placements - X again, right-click, or Esc
/// exits. A click that misses every building does nothing and stays in
/// the mode, so a mis-click doesn't force you to re-press X.
///
/// Same IsBlockingWorldClicks pattern as BuildingPlacer: stays true through
/// the exit frame, so the very click that exits the mode can't also fall
/// through and select a unit or issue a move order that same frame.
/// </summary>
public class DemolishInput : MonoBehaviour
{
    public static DemolishInput Instance { get; private set; }

    [SerializeField] private LayerMask buildingMask;
    [Tooltip("Small hint shown while demolish mode is active, e.g. 'Left-click a building to demolish it'. Text is authored once in the Inspector; this script only toggles visibility.")]
    [SerializeField] private GameObject hintLabel;

    [Header("Hover Highlight")]
    [SerializeField] private Color hoverColor = new Color(1f, 0.25f, 0.25f);


    public bool IsDemolishing { get; private set; }
    public bool IsBlockingWorldClicks => IsDemolishing || lastExitFrame == Time.frameCount;

    private int lastExitFrame = -1;
    private Renderer[] hoveredRenderers;
    private MaterialPropertyBlock hoverBlock;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");


private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        if (hintLabel != null)
            hintLabel.SetActive(false);

        hoverBlock = new MaterialPropertyBlock();
    }

private void Update()
    {
        if (PauseManager.Instance != null && PauseManager.Instance.IsPaused)
            return;

        if (IntroSequence.Instance != null && IntroSequence.Instance.IsIntroActive)
            return;

        if (!IsDemolishing)
        {
            // Don't fight with placement mode - one modal tool at a time.
            if (Input.GetKeyDown(KeyCode.X) &&
                (BuildingPlacer.Instance == null || !BuildingPlacer.Instance.IsPlacing))
            {
                EnterDemolishMode();
            }
            return;
        }

        if (Input.GetKeyDown(KeyCode.X) || Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            ExitDemolishMode();
            return;
        }

        UpdateHoverHighlight();

        if (Input.GetMouseButtonDown(0) && MouseWorld.TryGetObjectUnderMouse(buildingMask, out Collider hit))
        {
            PlacedBuilding building = hit.GetComponentInParent<PlacedBuilding>();
            if (building != null)
                building.Demolish();
            // Mode persists either way - matches BuildingPlacer's "keep
            // placing until explicitly cancelled" UX. The demolished
            // building takes its renderers with it, so no un-hover needed.
            ClearHoverTracking();
        }
    }

    private void UpdateHoverHighlight()
    {
        PlacedBuilding current = null;
        if (MouseWorld.TryGetObjectUnderMouse(buildingMask, out Collider hit))
            current = hit.GetComponentInParent<PlacedBuilding>();

        GameObject currentGo = current != null ? current.gameObject : null;
        GameObject hoveredGo = (hoveredRenderers != null && hoveredRenderers.Length > 0 && hoveredRenderers[0] != null)
            ? hoveredRenderers[0].transform.root.gameObject
            : null;

        if (currentGo == hoveredGo)
            return; // still the same building (or still nothing) - no change needed

        RestoreHoveredColors();

        if (current != null)
            ApplyHoverTint(current.gameObject);
    }

    private void ApplyHoverTint(GameObject target)
    {
        hoveredRenderers = target.GetComponentsInChildren<Renderer>();

        foreach (Renderer r in hoveredRenderers)
        {
            hoverBlock.Clear();
            hoverBlock.SetColor(BaseColorId, hoverColor);
            r.SetPropertyBlock(hoverBlock);
        }
    }

    private void RestoreHoveredColors()
    {
        if (hoveredRenderers == null)
            return;

        foreach (Renderer r in hoveredRenderers)
        {
            if (r == null)
                continue; // demolished mid-hover - nothing left to restore

            hoverBlock.Clear(); // empty block = renderer's own material color, no leaked override
            r.SetPropertyBlock(hoverBlock);
        }

        ClearHoverTracking();
    }

    private void ClearHoverTracking()
    {
        hoveredRenderers = null;
    }

    /// <summary>Called by other modal tools (e.g. BuildingPlacer) to cancel demolish mode if it's active when they take over.</summary>
    public void Cancel()
    {
        if (IsDemolishing)
            ExitDemolishMode();
    }

    private void EnterDemolishMode()
    {
        IsDemolishing = true;
        if (hintLabel != null)
            hintLabel.SetActive(true);
    }

private void ExitDemolishMode()
    {
        IsDemolishing = false;
        lastExitFrame = Time.frameCount;
        if (hintLabel != null)
            hintLabel.SetActive(false);

        RestoreHoveredColors();
    }
}
