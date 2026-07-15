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

    [Header("Cannon Turret (optional - leave empty for a non-turreted tower)")]
    [Tooltip("The rotating part - assign the tilted 'Cannon' child on the Cannon upgrade. Only its YAW is driven; whatever tilt you sculpted into its rest rotation is preserved every frame.")]
    [SerializeField] private Transform cannonPivot;
    [SerializeField] private float turretTurnSpeed = 180f;
    [Tooltip("Correction applied to the computed yaw - the code assumes the barrel's modeled forward axis points at the target BEFORE this offset. Tune this instead of the Cannon's own Transform: TickCannonTurret overwrites the yaw every frame, so a manual edit in the Inspector gets silently discarded the instant Play mode starts.")]
    [SerializeField] private float cannonYawOffset = 90f;

    private Health target;
    private float retargetTimer;
    private float fireTimer;
    private Vector3 cannonRestTilt;

    private void Awake()
    {
        if (cannonPivot != null)
            cannonRestTilt = cannonPivot.localEulerAngles;
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

        if (target == null || target.IsDead)
            return;

        fireTimer -= Time.deltaTime;
        if (fireTimer <= 0f)
        {
            fireTimer = data.fireCooldown;
            Fire();
        }
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

    public bool TryUpgradeToCannon()
    {
        if (!CanUpgradeToCannon)
            return false;

        if (ResourceManager.Instance != null &&
            !ResourceManager.Instance.TrySpend(ResourceType.Shells, data.cannonUpgradeCost))
            return false;

        Vector3 position = transform.position;
        Quaternion rotation = transform.rotation;
        GameObject upgradedPrefab = data.cannonUpgradeData.prefab;
        TowerData upgradedData = data.cannonUpgradeData;

        Destroy(gameObject);

        GameObject newTower = Instantiate(upgradedPrefab, position, rotation);
        newTower.AddComponent<PlacedBuilding>().Init(upgradedData);

        return true;
    }

}
