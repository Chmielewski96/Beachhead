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

    private Health target;
    private float retargetTimer;
    private float fireTimer;

    private void Update()
    {
        retargetTimer -= Time.deltaTime;
        if (retargetTimer <= 0f)
        {
            retargetTimer = retargetInterval;
            AcquireTarget();
        }

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

        // 'Most dangerous' = closest to the Keep. Fall back to closest to
        // the tower if there's no Keep in the scene.
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

            float score = Vector3.Distance(candidate.transform.position, keepPosition);
            if (score < bestScore)
            {
                bestScore = score;
                bestTarget = candidate;
            }
        }

        target = bestTarget;
    }

    private void Fire()
    {
        if (data.projectilePrefab == null || firePoint == null)
            return;

        GameObject projectileObject = Instantiate(data.projectilePrefab, firePoint.position, Quaternion.identity);
        Projectile projectile = projectileObject.GetComponent<Projectile>();
        if (projectile != null)
            projectile.Init(target, data.damage, data.projectileSpeed);
    }
}
