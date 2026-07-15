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

    [Header("Splash (0 = single-target only, the default tower)")]
    [Tooltip("Radius around the primary target that also takes splashDamage - the Cannon upgrade's whole point.")]
    public float splashRadius = 0f;
    public int splashDamage = 0;

    [Tooltip("0 = flat/straight shot (the default tower's arrow). >0 = a purely visual lob that peaks at the midpoint - conveys a heavier projectile without changing hit timing at all.")]
    public float projectileArcHeight = 0f;

    [Tooltip("Optional VFX spawned at the exact moment of impact - leave null for a plain hit (the default tower's arrow).")]
    public GameObject impactEffectPrefab;

    [Header("Cannon Upgrade")]
    [Tooltip("If set, a tower using THIS data can pay cannonUpgradeCost to become this instead - swaps the GameObject in place. Leave null on the Cannon's own data so no further upgrade is offered.")]
    public TowerData cannonUpgradeData;
    public int cannonUpgradeCost = 40;
}
