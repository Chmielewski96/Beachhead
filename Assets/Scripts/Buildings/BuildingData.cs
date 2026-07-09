using UnityEngine;

/// <summary>
/// Data-driven definition of a placeable building. Each building type is an
/// asset (Create > Beachhead > Building Data), not a hardcoded prefab
/// reference in some manager - this is the project's core architectural
/// lesson: data lives in ScriptableObjects, behavior lives in MonoBehaviours.
/// </summary>
[CreateAssetMenu(menuName = "Beachhead/Building Data")]
public class BuildingData : ScriptableObject
{
    public string buildingName;

    [Tooltip("The real building prefab - colliders, NavMeshObstacle (carving), the works.")]
    public GameObject prefab;

    [Tooltip("Visual-only copy used as the placement preview: NO colliders, NO NavMeshObstacle, transparent material.")]
    public GameObject ghostPrefab;

    [Tooltip("Unused until Phase 6 (economy) - set sensible values now anyway.")]
    public int shellCost;

    [Tooltip("XZ size in world units, used for the placement validity check. Match the prefab's actual footprint.")]
    public Vector2 footprint = new Vector2(2f, 2f);
}
