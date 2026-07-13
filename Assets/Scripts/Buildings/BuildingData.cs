using UnityEngine;

/// <summary>
/// Data-driven definition of a placeable building. Each building type is an
/// asset (Create > Beachhead > Building Data), not a hardcoded prefab
/// reference in some manager. Costs are a LIST: a building can charge
/// several resources at once (e.g. Tower: 50 sand + 20 shells), and the
/// placer spends atomically - all or nothing.
/// </summary>
[CreateAssetMenu(menuName = "Beachhead/Building Data")]
public class BuildingData : ScriptableObject
{
    [System.Serializable]
    public struct ResourceCost
    {
        public ResourceType resource;
        public int amount;
    }

    public string buildingName;

    [Tooltip("The real building prefab - colliders, NavMeshObstacle (carving), the works.")]
    public GameObject prefab;

    [Tooltip("Visual-only copy used as the placement preview: NO colliders, NO NavMeshObstacle, NO behavior scripts, transparent material.")]
    public GameObject ghostPrefab;

    [Tooltip("Everything this building costs to place - can mix resources.")]
    public ResourceCost[] costs;

    [Tooltip("XZ size in world units, used for the placement validity check. Match the prefab's actual footprint.")]
    public Vector2 footprint = new Vector2(2f, 2f);
}
