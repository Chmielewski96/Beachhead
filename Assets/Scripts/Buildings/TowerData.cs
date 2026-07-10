using UnityEngine;

/// <summary>
/// Tower stats, extending BuildingData - your first ScriptableObject
/// INHERITANCE. Because a TowerData *is* a BuildingData, the entire
/// placement system (ghost, grid snap, validity, costs, hotkeys) works on
/// towers with zero changes - it only sees the base type. Tower-specific
/// systems (the Tower script itself) see the extra combat fields.
/// </summary>
[CreateAssetMenu(menuName = "Beachhead/Tower Data")]
public class TowerData : BuildingData
{
    [Header("Combat")]
    public float range = 10f;
    public int damage = 10;
    public float fireCooldown = 1f;

    [Header("Projectile")]
    public GameObject projectilePrefab;
    public float projectileSpeed = 15f;
}
