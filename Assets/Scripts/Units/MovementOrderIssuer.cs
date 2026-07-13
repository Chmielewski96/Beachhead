using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Listens for right-click and issues movement orders to the currently
/// selected Units (via SelectionManager), applying formation offsets so
/// the group spreads out instead of piling onto one exact point.
/// </summary>
public class MovementOrderIssuer : MonoBehaviour
{
    [SerializeField] private LayerMask groundMask;

    [Tooltip("Right-clicking colliders on these layers assigns selected Workers to gather, instead of a plain move.")]
    [SerializeField] private LayerMask resourceNodeMask;

    [Tooltip("Optional - a small VFX prefab spawned at the ordered point. Leave empty to skip.")]
    [SerializeField] private GameObject clickMarkerPrefab;

    [Tooltip("Optional - a VFX prefab spawned on a resource node when Workers are successfully assigned to it (a blue ring reads well). Leave empty to skip.")]
    [SerializeField] private GameObject nodeMarkerPrefab;

    private void Update()
    {
        if (PauseManager.Instance != null && PauseManager.Instance.IsPaused)
            return;

        if (IntroSequence.Instance != null && IntroSequence.Instance.IsIntroActive)
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        // The right-click that cancels building placement must not also
        // issue a movement order on the same frame.
        if (BuildingPlacer.Instance != null && BuildingPlacer.Instance.IsBlockingWorldClicks)
            return;

        // Same reasoning for demolish mode.
        if (DemolishInput.Instance != null && DemolishInput.Instance.IsBlockingWorldClicks)
            return;


        if (Input.GetMouseButtonDown(1))
            IssueMoveOrder();
    }

private void IssueMoveOrder()
    {
        // Right-click priority 1: a resource node - assign selected Workers
        // to gather from it. (The node raycast runs BEFORE the ground one;
        // otherwise the ground hit behind the node would always win.)
        if (MouseWorld.TryGetObjectUnderMouse(resourceNodeMask, out Collider nodeHit))
        {
            ResourceDeposit node = nodeHit.GetComponentInParent<ResourceDeposit>();
            if (node != null && TryAssignWorkers(node))
            {
                // Confirmation feedback: mark the node so it's clear the
                // gather order was actually received.
                if (nodeMarkerPrefab != null)
                    Instantiate(nodeMarkerPrefab, node.transform.position + Vector3.up * 0.05f, Quaternion.identity);

                return;
            }
        }

        // Right-click priority 2: plain ground - a formation move order.
        if (!MouseWorld.TryGetGroundPoint(groundMask, out Vector3 targetPoint))
            return;

        var selected = SelectionManager.Instance.Selected;
        if (selected.Count == 0)
            return;

        Vector3[] offsets = FormationOffsets.GetOffsets(selected.Count);
        Quaternion facing = GetApproachRotation(selected, targetPoint);

        Vector3[] slots = new Vector3[offsets.Length];
        for (int i = 0; i < offsets.Length; i++)
            slots[i] = targetPoint + facing * offsets[i];

        int[] assignment = AssignSlotsByMarchOrder(selected, offsets, targetPoint, facing);

        for (int i = 0; i < selected.Count; i++)
        {
            // A direct move order is law - it cancels any gather loop first.
            WorkerAI worker = selected[i].GetComponent<WorkerAI>();
            if (worker != null)
                worker.OnManualMoveOrder();

            UnitMovement movement = selected[i].GetComponent<UnitMovement>();
            if (movement != null)
                movement.MoveTo(slots[assignment[i]]);
        }

        if (clickMarkerPrefab != null)
            Instantiate(clickMarkerPrefab, targetPoint, Quaternion.identity);
    }

    /// <summary>Assigns every selected Worker to the node. Returns false if the selection contains no Workers, letting the click fall through to a plain move.</summary>
    private bool TryAssignWorkers(ResourceDeposit node)
    {
        var selected = SelectionManager.Instance.Selected;
        bool assignedAny = false;

        foreach (Unit unit in selected)
        {
            WorkerAI worker = unit.GetComponent<WorkerAI>();
            if (worker != null)
            {
                worker.AssignDeposit(node);
                assignedAny = true;
            }
        }

        return assignedAny;
    }

/// <summary>
    /// Greedy global matching: consider every (unit, slot) pair, repeatedly
    /// take the closest still-unassigned pair. Not mathematically optimal
    /// (that would be the Hungarian algorithm), but eliminates nearly all
    /// visible path-crossing at a fraction of the complexity - plenty for
    /// RTS-scale squads. Returns slot index per unit index.
    /// </summary>
/// <summary>
    /// Match units to slots by march order, not by raw distance: units
    /// furthest along the direction of travel take the front row (they're
    /// leading anyway), and within each row units are matched left-to-right
    /// by lateral position. Everyone keeps their relative place in the pack,
    /// so paths don't cross and nobody plows through an already-formed row.
    /// Returns slot index per unit index.
    /// </summary>
    private int[] AssignSlotsByMarchOrder(System.Collections.Generic.IReadOnlyList<Unit> units, Vector3[] offsets, Vector3 targetPoint, Quaternion facing)
    {
        int count = units.Count;

        // Express unit positions in formation-local space: +Z = direction
        // of travel, +X = formation right. Makes 'who's in front' and
        // 'who's on the left' trivial component comparisons.
        Quaternion inverseFacing = Quaternion.Inverse(facing);
        Vector3[] unitLocal = new Vector3[count];
        for (int i = 0; i < count; i++)
            unitLocal[i] = inverseFacing * (units[i].transform.position - targetPoint);

        // Units ranked front-most first (highest local Z).
        var unitOrder = new System.Collections.Generic.List<int>(count);
        for (int i = 0; i < count; i++)
            unitOrder.Add(i);
        unitOrder.Sort((a, b) => unitLocal[b].z.CompareTo(unitLocal[a].z));

        // Slots ranked front row first, left-to-right within each row.
        var slotOrder = new System.Collections.Generic.List<int>(count);
        for (int i = 0; i < count; i++)
            slotOrder.Add(i);
        slotOrder.Sort((a, b) =>
        {
            int zCompare = offsets[b].z.CompareTo(offsets[a].z);
            return zCompare != 0 ? zCompare : offsets[a].x.CompareTo(offsets[b].x);
        });

        int[] assignment = new int[count];
        int index = 0;

        while (index < count)
        {
            // Find the extent of the current row (identical Z offsets).
            float rowZ = offsets[slotOrder[index]].z;
            int rowEnd = index;
            while (rowEnd < count && Mathf.Approximately(offsets[slotOrder[rowEnd]].z, rowZ))
                rowEnd++;
            int rowCount = rowEnd - index;

            // The next rowCount front-most units belong to this row - order
            // them left-to-right to match the slots' left-to-right order.
            var rowUnits = unitOrder.GetRange(index, rowCount);
            rowUnits.Sort((a, b) => unitLocal[a].x.CompareTo(unitLocal[b].x));

            for (int k = 0; k < rowCount; k++)
                assignment[rowUnits[k]] = slotOrder[index + k];

            index = rowEnd;
        }

        return assignment;
    }


private Quaternion GetApproachRotation(System.Collections.Generic.IReadOnlyList<Unit> units, Vector3 targetPoint)
    {
        Vector3 centroid = Vector3.zero;
        foreach (Unit unit in units)
            centroid += unit.transform.position;
        centroid /= units.Count;

        Vector3 approachDir = targetPoint - centroid;
        approachDir.y = 0f;

        // Degenerate case: units are already standing on the target point,
        // so there's no meaningful direction to face - don't rotate at all.
        if (approachDir.sqrMagnitude < 0.0001f)
            return Quaternion.identity;

        // The formation's own front row is defined along local +Z (see
        // FormationOffsets), so mapping +Z onto the approach direction
        // rotates the whole wedge to face it, front row leading.
        return Quaternion.LookRotation(approachDir.normalized, Vector3.up);
    }

}
