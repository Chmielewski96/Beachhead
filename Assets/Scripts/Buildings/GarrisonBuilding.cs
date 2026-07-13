using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Maintains a self-replenishing pool of autonomous troops AND coordinates
/// them as a squad walking a LAP around the building.
///
/// Containment: a lap waypoint is only accepted if the walking path to it
/// is close to its straight-line distance (containmentRatio). A point
/// outside the castle walls is only reachable by detouring through the
/// gate - long path, short line - so it fails the ratio and the waypoint
/// shrinks inward instead. The lap thereby molds itself to the castle's
/// interior, whatever its shape: rectangular castles get an inner
/// rectangle-ish loop, organic ones get an organic loop, and a garrison in
/// the open field patrols the full circle unchanged.
/// </summary>
public class GarrisonBuilding : MonoBehaviour
{
    [SerializeField] private GarrisonData data;
    [Tooltip("Where troops appear - an empty child just outside the building's footprint.")]
    [SerializeField] private Transform spawnPoint;

    [Header("Group Patrol")]
    [Tooltip("The squad advances this many degrees around the building per patrol tick - a lap, not a wander.")]
    [SerializeField] private float patrolStepDegrees = 40f;
    [Tooltip("Seconds between patrol advances.")]
    [SerializeField] private float patrolMoveInterval = 5f;
    [Tooltip("A waypoint is accepted only if the walking path to it is at most this many times the straight-line distance. Points beyond the castle walls fail this (their path detours through the gate) and the lap pulls inward instead.")]
    [SerializeField] private float containmentRatio = 1.5f;

    // Radii to try per waypoint, outermost first - this is what molds the
    // lap to the castle interior instead of rejecting whole angles.
    private static readonly float[] RadiusFactors = { 1f, 0.7f, 0.45f, 0.25f };

    private readonly List<SoldierAI> activeTroops = new List<SoldierAI>();
    private int bonusGarrison;
    private float respawnTimer = 1f; // small initial delay, then one per cooldown
    private float patrolTimer = 2f;
    private float patrolAngle;
    private Vector3 lastPatrolPoint;
    private Quaternion lastFacing = Quaternion.identity;
    private NavMeshPath pathScratch;

    public int CurrentCap => data.maxGarrison + bonusGarrison;
    public int CurrentCount => activeTroops.Count;
    public bool CanReinforce => bonusGarrison < data.maxReinforcements;
    public int ReinforceCost => data.reinforceCost;
    public int ReinforcementsUsed => bonusGarrison;
    public int MaxReinforcements => data.maxReinforcements;


    private void Start()
    {
        patrolAngle = Random.Range(0f, 360f); // each garrison laps from its own phase
        lastPatrolPoint = transform.position;
    }

    private void Update()
    {
        TickRespawn();
        TickGroupPatrol();
    }

    private void TickRespawn()
    {
        if (activeTroops.Count >= CurrentCap)
            return;

        respawnTimer -= Time.deltaTime;
        if (respawnTimer <= 0f)
        {
            respawnTimer = data.respawnCooldown;
            SpawnTroop();
        }
    }

    private void TickGroupPatrol()
    {
        if (activeTroops.Count == 0)
            return;

        patrolTimer -= Time.deltaTime;
        if (patrolTimer > 0f)
            return;
        patrolTimer = patrolMoveInterval;

        // Advance the lap. Per angle: try the full radius first, then pull
        // inward; if no radius is walkable-and-contained, skip the angle.
        bool found = false;
        Vector3 point = lastPatrolPoint;
        int maxAngleAttempts = Mathf.CeilToInt(360f / Mathf.Max(1f, patrolStepDegrees));

        for (int attempt = 0; attempt < maxAngleAttempts && !found; attempt++)
        {
            patrolAngle = (patrolAngle + patrolStepDegrees) % 360f;
            float rad = patrolAngle * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));

            foreach (float factor in RadiusFactors)
            {
                Vector3 candidate = transform.position + direction * (data.patrolRadius * factor);
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                    continue;

                if (!IsContained(hit.position))
                    continue;

                point = hit.position;
                found = true;
                break;
            }
        }

        if (!found)
            return; // nowhere acceptable this tick - try again next tick

        // Face the formation along the direction of travel.
        Vector3 travelDir = point - lastPatrolPoint;
        travelDir.y = 0f;
        if (travelDir.sqrMagnitude > 0.01f)
            lastFacing = Quaternion.LookRotation(travelDir.normalized, Vector3.up);

        lastPatrolPoint = point;
        AssignFormationSlots();
    }

    /// <summary>
    /// True if the walking path from the garrison to the point is close to
    /// the straight-line distance - i.e. the point is 'inside' with us, not
    /// beyond a wall and reachable only via a long detour through the gate.
    /// </summary>
    private bool IsContained(Vector3 target)
    {
        // Origin: the spawn point (it's on walkable ground next to the
        // building - the building's own center is carved out of the mesh).
        Vector3 origin = spawnPoint != null ? spawnPoint.position : transform.position;
        if (NavMesh.SamplePosition(origin, out NavMeshHit originHit, 3f, NavMesh.AllAreas))
            origin = originHit.position;

        if (pathScratch == null)
            pathScratch = new NavMeshPath();

        if (!NavMesh.CalculatePath(origin, target, NavMesh.AllAreas, pathScratch))
            return false;
        if (pathScratch.status != NavMeshPathStatus.PathComplete)
            return false;

        float pathLength = 0f;
        Vector3[] corners = pathScratch.corners;
        for (int i = 1; i < corners.Length; i++)
            pathLength += Vector3.Distance(corners[i - 1], corners[i]);

        float straightLine = Vector3.Distance(origin, target);
        return pathLength <= Mathf.Max(2f, straightLine) * containmentRatio;
    }

    /// <summary>
    /// Hands every living troop its slot around the current patrol point.
    /// Each slot is snapped to walkable ground so a slot landing inside a
    /// building becomes a reachable point beside it, instead of an
    /// impossible destination the troop squashes itself against.
    /// </summary>
    private void AssignFormationSlots()
    {
        Vector3[] offsets = FormationOffsets.GetOffsets(activeTroops.Count);
        for (int i = 0; i < activeTroops.Count; i++)
        {
            if (activeTroops[i] == null)
                continue;

            Vector3 slot = lastPatrolPoint + lastFacing * offsets[i];
            if (NavMesh.SamplePosition(slot, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                slot = hit.position;

            activeTroops[i].SetPatrolDestination(slot);
        }
    }

    private void SpawnTroop()
    {
        if (data.soldierPrefab == null || spawnPoint == null)
            return;

        Vector3 position = spawnPoint.position;
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            position = hit.position;

        GameObject troopObject = Instantiate(data.soldierPrefab, position, Quaternion.identity);

        SoldierAI soldier = troopObject.GetComponent<SoldierAI>();
        if (soldier != null)
            soldier.Init(transform.position, data.patrolRadius);

        Health troopHealth = troopObject.GetComponent<Health>();
        if (troopHealth != null)
            troopHealth.OnDeath += () => HandleTroopDeath(soldier);

        activeTroops.Add(soldier);

        // Fold the newcomer into the CURRENT formation target - the squad
        // keeps walking its lap; the fresh spawn catches up at hurry speed.
        AssignFormationSlots();
    }

    private void HandleTroopDeath(SoldierAI soldier)
    {
        // The building may itself have been destroyed by the time a stray
        // troop dies - its orphaned soldiers just don't get replaced.
        if (this == null)
            return;

        activeTroops.Remove(soldier);

        // Close ranks: redistribute the survivors over the smaller formation.
        AssignFormationSlots();
    }

    /// <summary>+1 max garrison for shells, up to the data-defined cap.
    /// (Temporary hotkey drives this until the Phase 9 UI.)</summary>
    public bool TryReinforce()
    {
        if (!CanReinforce)
            return false;

        if (ResourceManager.Instance != null &&
            !ResourceManager.Instance.TrySpend(ResourceType.Shells, data.reinforceCost))
            return false;

        bonusGarrison++;
        return true;
    }
}
