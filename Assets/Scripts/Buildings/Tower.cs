using UnityEngine;

/// <summary>
/// Auto-firing tower. Re-acquires its target on an interval (never per
/// frame), prioritizing the enemy CLOSEST TO THE KEEP - the most dangerous
/// one - rather than the one closest to the tower. Classic TD priority:
/// with a marching horde, the difference is very visible.
/// </summary>
public class Tower : MonoBehaviour
{
    [SerializeField] private TowerData data;
    [SerializeField] private LayerMask enemyMask;
    [Tooltip("Where projectiles spawn from - an empty child at the tower top.")]
    [SerializeField] private Transform firePoint;
    [SerializeField] private float retargetInterval = 0.25f;

    [Header("Turret Rotation (optional - leave empty for a non-turreted tower; shared by Cannon's barrel AND Lens's lens, despite the field names)")]
    [Tooltip("The rotating part - the tilted 'Cannon' child on the Cannon upgrade, or the 'Lens' child on the Lens upgrade. Only its YAW is driven; whatever tilt you sculpted into its rest rotation is preserved every frame.")]
    [SerializeField] private Transform cannonPivot;
    [SerializeField] private float turretTurnSpeed = 180f;
    [Tooltip("Correction applied to the computed yaw - the code assumes the modeled forward axis points at the target BEFORE this offset. Tune this instead of the pivot's own Transform: TickCannonTurret overwrites the yaw every frame, so a manual edit in the Inspector gets silently discarded the instant Play mode starts. Cannon needs 90 - Lens may need a completely different value since it's a different mesh.")]
    [SerializeField] private float cannonYawOffset = 90f;

    [Header("Attack Material Swap (optional - e.g. Lens's glow while beaming)")]
    [Tooltip("Renderer whose material swaps while actively attacking - e.g. the Lens mesh. Leave null to skip entirely.")]
    [SerializeField] private Renderer attackMaterialRenderer;
    [SerializeField] private Material attackMaterial;

    private Health target;
    private float retargetTimer;
    private float fireTimer;
    private Vector3 cannonRestTilt;
    private Material defaultMaterial;
    private bool isShowingAttackMaterial;

    private void Awake()
    {
        if (cannonPivot != null)
            cannonRestTilt = cannonPivot.localEulerAngles;

        if (attackMaterialRenderer != null)
            defaultMaterial = attackMaterialRenderer.sharedMaterial;
    }

    private void Update()
    {
        retargetTimer -= Time.deltaTime;
        if (retargetTimer <= 0f)
        {
            retargetTimer = retargetInterval;
            AcquireTarget();
        }

        TickCannonTurret();
        TickAttackMaterial();

        if (target == null || target.IsDead)
            return;

        fireTimer -= Time.deltaTime;
        if (fireTimer <= 0f)
        {
            fireTimer = data.fireCooldown;
            if (data.useBeamAttack)
                TickBeam();
            else
                Fire();
        }
    }

    /// <summary>
    /// True while this tower has a live target AND is a beam-style tower -
    /// exactly the condition a beam VFX script should use to decide
    /// whether to be visible at all this frame.
    /// </summary>
    public bool IsBeamActive => data != null && data.useBeamAttack && target != null && !target.IsDead;

    /// <summary>Where a beam VFX should start - the same firePoint discrete projectiles spawn from.</summary>
    public Vector3 BeamStartPoint => firePoint != null ? firePoint.position : transform.position;

    /// <summary>Where a beam VFX should end - the target's body, not its feet (matches Projectile's own AimHeightOffset convention).</summary>
    public Vector3 BeamEndPoint => target != null ? target.transform.position + Vector3.up * 0.5f : transform.position;

    /// <summary>
    /// The Lens Tower's whole point: no projectile, no travel time, damage
    /// applies the instant the tick fires. fireCooldown IS the tick
    /// interval (0.25s) and damage IS the per-tick amount (8) - both
    /// already generic fields, nothing beam-specific needed for the numbers.
    /// </summary>
    private void TickBeam()
    {
        target.TakeDamage(data.damage);
    }

private void AcquireTarget()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, data.range, enemyMask);

        Vector3 keepPosition = Keep.Instance != null
            ? Keep.Instance.transform.position
            : transform.position;

        float bestScore = float.MaxValue;
        Health bestTarget = null;

        foreach (Collider hit in hits)
        {
            Health candidate = hit.GetComponentInParent<Health>();
            if (candidate == null || candidate.IsDead)
                continue;

            // Overkill prevention: in-flight projectiles have already
            // committed enough damage to finish this one - don't waste a shot.
            if (candidate.EffectiveHP <= 0)
                continue;

            // Danger score: real PATH distance to the Keep when available
            // (through a maze this differs enormously from the straight
            // line), straight-line as fallback for enemies without the
            // component.
            KeepThreatDistance threat = candidate.GetComponent<KeepThreatDistance>();
            float score = threat != null
                ? threat.PathDistanceToKeep
                : Vector3.Distance(candidate.transform.position, keepPosition);

            if (score < bestScore)
            {
                bestScore = score;
                bestTarget = candidate;
            }
        }

        target = bestTarget;
    }

private void TickCannonTurret()
    {
        if (cannonPivot == null || target == null || target.IsDead)
            return;

        Vector3 toTarget = target.transform.position - cannonPivot.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f)
            return;

        Vector3 localDir = cannonPivot.parent != null
            ? cannonPivot.parent.InverseTransformDirection(toTarget)
            : toTarget;
        float yaw = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg + cannonYawOffset;

        Quaternion desired = Quaternion.Euler(cannonRestTilt.x, yaw, cannonRestTilt.z);
        cannonPivot.localRotation = Quaternion.RotateTowards(cannonPivot.localRotation, desired, turretTurnSpeed * Time.deltaTime);
    }


    /// <summary>
    /// Swaps to attackMaterial while IsBeamActive (or, for a non-beam
    /// tower, while it has a live target at all - the closest generic
    /// equivalent of "currently engaged"), reverting to whatever the
    /// renderer originally had the instant that's no longer true. Only
    /// touches the renderer on an actual state CHANGE, not every frame.
    /// </summary>
    private void TickAttackMaterial()
    {
        if (attackMaterialRenderer == null || attackMaterial == null)
            return;

        bool shouldShowAttack = data != null && data.useBeamAttack
            ? IsBeamActive
            : (target != null && !target.IsDead);

        if (shouldShowAttack == isShowingAttackMaterial)
            return;

        isShowingAttackMaterial = shouldShowAttack;
        attackMaterialRenderer.sharedMaterial = shouldShowAttack ? attackMaterial : defaultMaterial;
    }

private void Fire()
    {
        if (data.projectilePrefab == null || firePoint == null)
            return;

        GameObject projectileObject = Instantiate(data.projectilePrefab, firePoint.position, Quaternion.identity);
        Projectile projectile = projectileObject.GetComponent<Projectile>();
        if (projectile != null)
            projectile.Init(target, data.damage, data.projectileSpeed, data.splashRadius, data.splashDamage, enemyMask, data.projectileArcHeight, data.impactEffectPrefab);
    }

public string DisplayName => data != null && !string.IsNullOrEmpty(data.buildingName) ? data.buildingName : "Tower";

public bool CanUpgradeToCannon => data.cannonUpgradeData != null;
    public int CannonUpgradeCost => data.cannonUpgradeData != null ? data.cannonUpgradeCost : 0;
    public bool TryUpgradeToCannon() => TryUpgradeTo(data.cannonUpgradeData, data.cannonUpgradeCost);

    public bool CanUpgradeToLens => data.lensUpgradeData != null;
    public int LensUpgradeCost => data.lensUpgradeData != null ? data.lensUpgradeCost : 0;
    public bool TryUpgradeToLens() => TryUpgradeTo(data.lensUpgradeData, data.lensUpgradeCost);

    /// <summary>
    /// Shared by both upgrade paths - identical swap-in-place mechanics,
    /// just a different target data/prefab/cost. Choosing either one
    /// naturally locks out the OTHER too: the new prefab's own TowerData
    /// has both cannonUpgradeData and lensUpgradeData left null, so
    /// CanUpgradeToCannon/CanUpgradeToLens both read false afterward
    /// without any extra bookkeeping - same choose-one guarantee
    /// GarrisonBuilding's specialization gets from its own enum check.
    /// </summary>
    private bool TryUpgradeTo(TowerData upgradeData, int cost)
    {
        if (upgradeData == null)
            return false;

        if (ResourceManager.Instance != null &&
            !ResourceManager.Instance.TrySpend(ResourceType.Shells, cost))
            return false;

        Vector3 position = transform.position;
        Quaternion rotation = transform.rotation;
        GameObject upgradedPrefab = upgradeData.prefab;

        Destroy(gameObject);

        GameObject newTower = Instantiate(upgradedPrefab, position, rotation);
        newTower.AddComponent<PlacedBuilding>().Init(upgradeData);

        return true;
    }

}
