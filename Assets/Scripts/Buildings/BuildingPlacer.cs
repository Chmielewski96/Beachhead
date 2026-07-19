using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Placement mode: spawns a ghost preview that follows the mouse with grid
/// snapping, validates the spot each frame (tinting the ghost green/red via
/// MaterialPropertyBlock), and instantiates the real building on confirm.
/// Left-click places (hold Shift to keep placing), R rotates 90 degrees,
/// right-click or Esc cancels.
/// </summary>
public class BuildingPlacer : MonoBehaviour
{
    public static BuildingPlacer Instance { get; private set; }

    /// <summary>True while a ghost is active.</summary>
    public bool IsPlacing => currentData != null;

    /// <summary>
    /// True while placing AND for the remainder of the frame placement mode
    /// exits. Selection/movement input checks this instead of IsPlacing so
    /// the same click that cancels or confirms placement can't also fall
    /// through and select units or issue a move order that frame.
    /// </summary>
    public bool IsBlockingWorldClicks => IsPlacing || lastExitFrame == Time.frameCount;

    [Header("Layers")]
    [SerializeField] private LayerMask groundMask;
    [Tooltip("Everything that blocks placement: Unit, Building, Enemy, ResourceNode.")]
    [SerializeField] private LayerMask blockingMask;

    [Header("Grid")]
    [SerializeField] private float cellSize = 2f;

    [Header("Ghost Tint")]
    [SerializeField] private Color validColor = new Color(0.2f, 1f, 0.2f, 0.5f);
    [SerializeField] private Color invalidColor = new Color(1f, 0.2f, 0.2f, 0.5f);

    [Header("Validity Check")]
    [Tooltip("Vertical size of the overlap check volume.")]
    [SerializeField] private float checkHeight = 2f;
    [Tooltip("Shrinks the check volume slightly so buildings placed in adjacent grid cells (walls!) don't falsely block each other.")]
    [SerializeField] private float footprintMargin = 0.05f;

    [Header("Debug Hotkeys (temporary until the build menu UI exists)")]
    [Tooltip("Press 1 to start placing element 0, 2 for element 1, etc.")]
    [SerializeField] private BuildingData[] debugBuildings;

    private BuildingData currentData;
    private GameObject ghost;
    private Renderer[] ghostRenderers;
    private MaterialPropertyBlock propertyBlock;
    private float ghostYRotation;
    private bool isValid;
    private int lastExitFrame = -1;

    // Line-drag state (Shift + drag to preview and place a row of buildings)
    private bool isDraggingLine;
    private Vector3 lineAnchor;
    private readonly System.Collections.Generic.List<GameObject> lineGhosts = new System.Collections.Generic.List<GameObject>();
    private readonly System.Collections.Generic.List<Renderer[]> lineGhostRenderers = new System.Collections.Generic.List<Renderer[]>();
    private readonly System.Collections.Generic.List<Vector3> lineCells = new System.Collections.Generic.List<Vector3>();
    private readonly System.Collections.Generic.List<bool> lineCellValid = new System.Collections.Generic.List<bool>();


    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        propertyBlock = new MaterialPropertyBlock();
    }

private void Update()
    {
        if (PauseManager.Instance != null && PauseManager.Instance.IsPaused)
            return;

        if (IntroSequence.Instance != null && IntroSequence.Instance.IsIntroActive)
            return;

        // Hotkeys work both outside placement mode (start placing) and
        // inside it (toggle off / switch building).
        HandleDebugHotkeys();

        if (!IsPlacing)
            return;

        if (isDraggingLine)
        {
            UpdateLineDrag();
            HandleLineDragInput();
        }
        else
        {
            UpdateGhost();
            HandlePlacementInput();
        }
    }

public void StartPlacing(BuildingData data)
    {
        CancelPlacement();

        currentData = data;
        ghostYRotation = 0f;
        ghost = Instantiate(data.ghostPrefab);
        StripGhostColliders(ghost);
        ghostRenderers = GetTintableRenderers(ghost);
        ApplyRangeRingScale(ghost);
    }

/// <summary>
    /// Ghosts must NEVER carry colliders: a ghost collider on a blocking
    /// layer fails its own validity check - and because the physics
    /// broadphase lags one frame behind the transform, the failure is
    /// position-dependent and maddeningly intermittent (moving the mouse
    /// fast 'outruns' the stale collider and placement suddenly works).
    /// This bug has now shipped on two different ghost prefabs (Tower,
    /// Garrison), so the placer strips colliders in code and the prefab's
    /// contents stop mattering.
    /// </summary>
    private static void StripGhostColliders(GameObject ghostInstance)
    {
        foreach (Collider c in ghostInstance.GetComponentsInChildren<Collider>(true))
            Destroy(c);
    }


    /// <summary>
    /// All renderers under the ghost EXCEPT anything on/under "RangeRing" -
    /// the ring keeps its own material and must not be stomped by the
    /// green/red validity tint.
    /// </summary>
    private Renderer[] GetTintableRenderers(GameObject ghostInstance)
    {
        Renderer[] all = ghostInstance.GetComponentsInChildren<Renderer>();
        var tintable = new System.Collections.Generic.List<Renderer>(all.Length);

        foreach (Renderer r in all)
        {
            bool underRangeRing = false;
            for (Transform t = r.transform; t != null && t != ghostInstance.transform; t = t.parent)
            {
                if (t.name == "RangeRing")
                {
                    underRangeRing = true;
                    break;
                }
            }

            if (!underRangeRing)
                tintable.Add(r);
        }

        return tintable.ToArray();
    }

    /// <summary>
    /// If the building being placed is a tower, scale the ghost's "RangeRing"
    /// child (a flat disc) to the tower's actual range from its data - so the
    /// preview ring can never drift out of sync with the stats. Non-tower
    /// buildings (or ghosts without a ring) are silently unaffected.
    /// </summary>
    private void ApplyRangeRingScale(GameObject ghostInstance)
    {
if (currentData is TowerData towerData)
        {
            Transform ring = ghostInstance.transform.Find("RangeRing");
            if (ring != null)
                ring.localScale = new Vector3(towerData.range * 2f, ring.localScale.y, towerData.range * 2f);
        }
else if (currentData is GarrisonData garrisonData)
        {
            // Same ring, same scaling convention - commandRange (the Defend
            // Point range) rather than patrolRadius, so the placement
            // preview shows how far the garrison can later be ordered to
            // defend a point, not its default patrol lap.
            Transform ring = ghostInstance.transform.Find("RangeRing");
            if (ring != null)
                ring.localScale = new Vector3(garrisonData.commandRange * 2f, ring.localScale.y, garrisonData.commandRange * 2f);
        }
    }

public void CancelPlacement()
    {
        ClearLineGhosts();
        isDraggingLine = false;

        if (ghost != null)
            Destroy(ghost);

        if (currentData != null)
            lastExitFrame = Time.frameCount;

        ghost = null;
        ghostRenderers = null;
        currentData = null;
    }

private void HandleDebugHotkeys()
    {
        if (debugBuildings == null)
            return;

        for (int i = 0; i < debugBuildings.Length && i < 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i) && debugBuildings[i] != null)
            {
                // Pressing the hotkey of the building already being placed
                // toggles placement mode off; a different hotkey switches
                // to that building instead.
                if (currentData == debugBuildings[i])
                {
                    CancelPlacement();
                }
                else
                {
                    DemolishInput.Instance?.Cancel(); // one modal tool at a time
                    StartPlacing(debugBuildings[i]);
                }
            }
        }
    }

    private void UpdateGhost()
    {
        if (Input.GetKeyDown(KeyCode.R))
            ghostYRotation = (ghostYRotation + 90f) % 360f;

        if (!MouseWorld.TryGetGroundPoint(groundMask, out Vector3 groundPoint))
        {
            ghost.SetActive(false); // cursor is off the map - hide rather than strand the ghost
            isValid = false;
            return;
        }

        ghost.SetActive(true);

        Vector3 snapped = new Vector3(
            Mathf.Round(groundPoint.x / cellSize) * cellSize,
            groundPoint.y,
            Mathf.Round(groundPoint.z / cellSize) * cellSize);

        Quaternion rotation = Quaternion.Euler(0f, ghostYRotation, 0f);
        ghost.transform.SetPositionAndRotation(snapped, rotation);

        isValid = CheckValidity(snapped, rotation);

        // Red also when the spot is fine but the resources aren't - to the
        // player it means 'can't place here right now' either way. isValid
        // itself stays spatial-only: clicking a valid-but-unaffordable spot
        // still runs TrySpendFor, whose refusal flashes the resource counter.
        TintGhost(isValid && AffordableCount(currentData) >= 1 ? validColor : invalidColor);
    }

    private bool CheckValidity(Vector3 position, Quaternion rotation)
    {
        // Passing the rotation to OverlapBox means a rotated footprint is
        // checked correctly - no manual swapping of X/Z needed for walls
        // turned 90 degrees.
        Vector3 halfExtents = new Vector3(
            currentData.footprint.x * 0.5f - footprintMargin,
            checkHeight * 0.5f,
            currentData.footprint.y * 0.5f - footprintMargin);

        Vector3 center = position + Vector3.up * (checkHeight * 0.5f);

        return Physics.OverlapBox(center, halfExtents, rotation, blockingMask).Length == 0;
    }

private void TintGhost(Color color)
    {
        TintRenderers(ghostRenderers, color);
    }

    private void TintRenderers(Renderer[] renderers, Color color)
    {
        foreach (Renderer r in renderers)
        {
            r.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, color);
            r.SetPropertyBlock(propertyBlock);
        }
    }

    private Vector3 Snap(Vector3 point)
    {
        return new Vector3(
            Mathf.Round(point.x / cellSize) * cellSize,
            point.y,
            Mathf.Round(point.z / cellSize) * cellSize);
    }

    private void UpdateLineDrag()
    {
        if (!MouseWorld.TryGetGroundPoint(groundMask, out Vector3 groundPoint))
            return; // cursor off the map - keep the last valid preview

        BuildLineCells(lineAnchor, Snap(groundPoint));
        SyncLineGhosts();
    }

    private void BuildLineCells(Vector3 start, Vector3 end)
    {
        lineCells.Clear();

        float dx = end.x - start.x;
        float dz = end.z - start.z;

        // Straight, axis-aligned line along whichever axis dominates the drag.
        if (Mathf.Abs(dx) >= Mathf.Abs(dz))
        {
            int steps = Mathf.RoundToInt(Mathf.Abs(dx) / cellSize);
            float dir = dx >= 0f ? 1f : -1f;
            for (int i = 0; i <= steps; i++)
                lineCells.Add(new Vector3(start.x + i * cellSize * dir, start.y, start.z));
        }
        else
        {
            int steps = Mathf.RoundToInt(Mathf.Abs(dz) / cellSize);
            float dir = dz >= 0f ? 1f : -1f;
            for (int i = 0; i <= steps; i++)
                lineCells.Add(new Vector3(start.x, start.y, start.z + i * cellSize * dir));
        }
    }

    private void SyncLineGhosts()
    {
        Quaternion rotation = Quaternion.Euler(0f, ghostYRotation, 0f);

        // Grow/shrink the ghost pool to match the cell count instead of
        // destroying and recreating everything each frame.
        while (lineGhosts.Count < lineCells.Count)
        {
            GameObject g = Instantiate(currentData.ghostPrefab);
            StripGhostColliders(g);
            ApplyRangeRingScale(g);
            lineGhosts.Add(g);
            lineGhostRenderers.Add(GetTintableRenderers(g));
        }
        while (lineGhosts.Count > lineCells.Count)
        {
            int last = lineGhosts.Count - 1;
            Destroy(lineGhosts[last]);
            lineGhosts.RemoveAt(last);
            lineGhostRenderers.RemoveAt(last);
        }

        lineCellValid.Clear();

        // Affordability preview for the whole line: only the first N valid
        // cells (N = what the resources cover) show green, the rest red -
        // matching exactly what the commit will actually place and skip.
        int affordable = AffordableCount(currentData);
        int affordableRank = 0;
        for (int i = 0; i < lineCells.Count; i++)
        {
            lineGhosts[i].transform.SetPositionAndRotation(lineCells[i], rotation);
            bool valid = CheckValidity(lineCells[i], rotation);
            lineCellValid.Add(valid);

            bool payable = valid && affordableRank < affordable;
            if (valid)
                affordableRank++;

            TintRenderers(lineGhostRenderers[i], payable ? validColor : invalidColor);
        }
    }

    private void HandleLineDragInput()
    {
        // Right-click/Esc abandons the line but stays in placement mode.
        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
        {
            ExitLineDrag();
            return;
        }

        // Releasing the drag commits every valid cell; invalid ones are skipped.
        if (Input.GetMouseButtonUp(0))
        {
            Quaternion rotation = Quaternion.Euler(0f, ghostYRotation, 0f);
            for (int i = 0; i < lineCells.Count; i++)
            {
                if (!lineCellValid[i])
                    continue;

                // Pay per segment; when the shells run out, place what was
                // affordable and stop - a partial wall is better than none.
                if (!TrySpendFor(currentData))
                    break;

                GameObject placed = Instantiate(currentData.prefab, lineCells[i], rotation);
                placed.AddComponent<PlacedBuilding>().Init(currentData);
            }
            ExitLineDrag();
        }
    }

    private void ExitLineDrag()
    {
        ClearLineGhosts();
        isDraggingLine = false;
        if (ghost != null)
            ghost.SetActive(true);
    }

    private void ClearLineGhosts()
    {
        foreach (GameObject g in lineGhosts)
            Destroy(g);
        lineGhosts.Clear();
        lineGhostRenderers.Clear();
        lineCells.Clear();
        lineCellValid.Clear();
    }

private void HandlePlacementInput()
    {
        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
        {
            CancelPlacement();
            return;
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (Input.GetMouseButtonDown(0) && ghost.activeSelf)
        {
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (shiftHeld)
            {
                // Anchor a line - ghosts for it are built in UpdateLineDrag.
                // Anchoring is allowed even on an invalid cell; each cell is
                // validated individually and invalid ones are skipped on commit.
                isDraggingLine = true;
                lineAnchor = ghost.transform.position;
                ghost.SetActive(false);
            }
            else if (isValid)
            {
                if (TrySpendFor(currentData))
                {
                    GameObject placed = Instantiate(currentData.prefab, ghost.transform.position, ghost.transform.rotation);
                    placed.AddComponent<PlacedBuilding>().Init(currentData);
                }
                // Placement mode persists - right-click or Esc to exit.
                // A refused spend fires ResourceManager.OnSpendFailed, which
                // the shell counter turns into a red flash - no coupling here.
            }
        }
    }

private bool TrySpendFor(BuildingData data)
    {
        if (ResourceManager.Instance == null)
            return true; // no economy in the scene - build for free (early testing)

        if (data.costs == null || data.costs.Length == 0)
            return true; // free building

        var rm = ResourceManager.Instance;

        // Atomic spend: verify EVERYTHING first. On the first shortfall, run
        // the failing TrySpend anyway - it refuses and fires OnSpendFailed,
        // which flashes the right counter - then bail without charging
        // anything else.
        foreach (BuildingData.ResourceCost cost in data.costs)
        {
            if (rm.GetAmount(cost.resource) < cost.amount)
            {
                rm.TrySpend(cost.resource, cost.amount);
                return false;
            }
        }

        foreach (BuildingData.ResourceCost cost in data.costs)
            rm.TrySpend(cost.resource, cost.amount);

        return true;
    }

/// <summary>
    /// How many copies of this building the current resources cover.
    /// Read-only - unlike TrySpendFor it never fires OnSpendFailed, so
    /// it's safe to call every frame for the ghost tint without making
    /// the resource counters flash red continuously.
    /// </summary>
    private int AffordableCount(BuildingData data)
    {
        if (ResourceManager.Instance == null || data.costs == null || data.costs.Length == 0)
            return int.MaxValue;

        int count = int.MaxValue;
        foreach (BuildingData.ResourceCost cost in data.costs)
        {
            if (cost.amount <= 0)
                continue;
            count = Mathf.Min(count, ResourceManager.Instance.GetAmount(cost.resource) / cost.amount);
        }
        return count;
    }


}
