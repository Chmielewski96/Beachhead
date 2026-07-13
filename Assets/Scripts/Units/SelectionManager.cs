using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Tracks the current selection: click to select one, drag a box to select
/// many, Shift adds to the existing selection instead of replacing it.
/// Movement orders (right-click) read from Selected to know who to command.
/// </summary>
public class SelectionManager : MonoBehaviour
{
    public static SelectionManager Instance { get; private set; }

    public IReadOnlyList<Unit> Selected => selected;
    private readonly List<Unit> selected = new List<Unit>();

    /// <summary>The currently selected building, or null. Buildings and units are never selected together.</summary>
    public BuildingSelectable SelectedBuilding { get; private set; }

    /// <summary>Fired when the selected building changes (null = deselected).</summary>
    public event System.Action<BuildingSelectable> OnBuildingSelectionChanged;


    [Header("Layers")]
    [SerializeField] private LayerMask unitMask;

    [Tooltip("Single-clicking these layers selects a building (if it has a BuildingSelectable). Drag-box stays units-only.")]
    [SerializeField] private LayerMask buildingMask;


    [Header("Drag Box UI")]
    [Tooltip("A UI Image's RectTransform, anchored/pivoted to (0,0), parented directly under the Canvas.")]
    [SerializeField] private RectTransform dragBoxVisual;
    [SerializeField] private Canvas parentCanvas;
    [SerializeField] private float dragThresholdPixels = 4f;

    private Vector2 dragStartScreenPos;
    private bool isDragging;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (dragBoxVisual != null)
            dragBoxVisual.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return; // clicks on UI never affect selection

        // Placement mode owns the mouse - the click that places or cancels
        // a building must not also select/deselect units.
        if (BuildingPlacer.Instance != null && BuildingPlacer.Instance.IsBlockingWorldClicks)
            return;

        // Same reasoning for demolish mode.
        if (DemolishInput.Instance != null && DemolishInput.Instance.IsBlockingWorldClicks)
            return;


        if (Input.GetMouseButtonDown(0))
            BeginDrag();

        if (Input.GetMouseButton(0))
            UpdateDrag();

        if (Input.GetMouseButtonUp(0))
            EndDrag();
    }

    private void BeginDrag()
    {
        dragStartScreenPos = Input.mousePosition;
        isDragging = false; // only becomes true once we cross the pixel threshold
    }

    private void UpdateDrag()
    {
        float dragDistance = Vector2.Distance(dragStartScreenPos, Input.mousePosition);

        if (!isDragging && dragDistance > dragThresholdPixels)
        {
            isDragging = true;
            if (dragBoxVisual != null)
                dragBoxVisual.gameObject.SetActive(true);
        }

        if (isDragging && dragBoxVisual != null)
            UpdateDragVisual(dragStartScreenPos, Input.mousePosition);
    }

    private void EndDrag()
    {
        if (isDragging)
            SelectUnitsInScreenRect(dragStartScreenPos, Input.mousePosition);
        else
            SelectSingleUnitUnderMouse();

        isDragging = false;
        if (dragBoxVisual != null)
            dragBoxVisual.gameObject.SetActive(false);
    }

private void UpdateDragVisual(Vector2 startScreen, Vector2 endScreen)
    {
        Camera uiCamera = parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : parentCanvas.worldCamera;
        RectTransform canvasRect = parentCanvas.transform as RectTransform;

        Vector2 minScreen = Vector2.Min(startScreen, endScreen);
        Vector2 maxScreen = Vector2.Max(startScreen, endScreen);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, minScreen, uiCamera, out Vector2 minLocal);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, maxScreen, uiCamera, out Vector2 maxLocal);

        // ScreenPointToLocalPointInRectangle returns points relative to the
        // Canvas's own pivot, but our drag box is anchored to the Canvas's
        // bottom-left corner (0,0) - convert pivot-relative to corner-relative
        // so both reference frames actually line up, regardless of what the
        // Canvas's pivot happens to be set to.
        Vector2 pivotOffset = Vector2.Scale(canvasRect.pivot, canvasRect.rect.size);
        minLocal += pivotOffset;
        maxLocal += pivotOffset;

        dragBoxVisual.anchoredPosition = minLocal;
        dragBoxVisual.sizeDelta = maxLocal - minLocal;
    }

private void SelectSingleUnitUnderMouse()
    {
        bool additive = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (MouseWorld.TryGetObjectUnderMouse(unitMask, out Collider hit))
        {
            Unit unit = hit.GetComponentInParent<Unit>();
            if (unit != null)
            {
                if (!additive)
                    ClearSelection();
                else
                    DeselectBuilding(); // buildings never mix with unit selections

                AddToSelection(unit);
                return;
            }
        }

        // No unit under the cursor - maybe a selectable building?
        if (MouseWorld.TryGetObjectUnderMouse(buildingMask, out Collider buildingHit))
        {
            BuildingSelectable building = buildingHit.GetComponentInParent<BuildingSelectable>();
            if (building != null)
            {
                ClearSelection();
                SelectedBuilding = building;
                building.OnSelected();
                OnBuildingSelectionChanged?.Invoke(building);
                return;
            }
        }

        // Missed everything - a plain click clears; a Shift-click leaves
        // the existing selection alone (standard RTS behavior).
        if (!additive)
            ClearSelection();
    }

    private void DeselectBuilding()
    {
        if (SelectedBuilding == null)
            return;

        SelectedBuilding.OnDeselected();
        SelectedBuilding = null;
        OnBuildingSelectionChanged?.Invoke(null);
    }

    private void SelectUnitsInScreenRect(Vector2 startScreen, Vector2 endScreen)
    {
        bool additive = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (!additive)
            ClearSelection();

        Vector2 min = Vector2.Min(startScreen, endScreen);
        Vector2 max = Vector2.Max(startScreen, endScreen);
        Rect screenRect = new Rect(min, max - min);

        Unit[] allUnits = FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit unit in allUnits)
        {
            Vector3 screenPoint = Camera.main.WorldToScreenPoint(unit.transform.position);

            // WorldToScreenPoint still returns a point for things behind the
            // camera - discard those or the box can "see through" the rig.
            if (screenPoint.z < 0f)
                continue;

            if (screenRect.Contains(screenPoint))
                AddToSelection(unit);
        }
    }

    private void AddToSelection(Unit unit)
    {
        if (selected.Contains(unit))
            return;

        selected.Add(unit);
        unit.OnSelected();
    }

/// <summary>
    /// Called by units as they're destroyed, so the selection never holds
    /// stale references (touching a destroyed Unit's transform throws).
    /// No OnDeselected call - the object is going away anyway.
    /// </summary>
    public void RemoveFromSelection(Unit unit)
    {
        selected.Remove(unit);
    }


private void ClearSelection()
    {
        foreach (Unit unit in selected)
            unit.OnDeselected();
        selected.Clear();

        DeselectBuilding();
    }
}
