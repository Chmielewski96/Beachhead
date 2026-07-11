using UnityEngine;

/// <summary>
/// Homing projectile: flies toward its target each frame, applies damage
/// on arrival. If the target dies mid-flight, it continues to the last
/// known position and fizzles - looks natural and avoids null chasing.
/// </summary>
public class Projectile : MonoBehaviour
{
    private Health target;
    private int damage;
    private float speed;
    private Vector3 lastKnownPosition;
    private float lifetime;

    private const float HitDistance = 0.4f;
    private const float AimHeightOffset = 0.5f; // aim at the body, not the feet
    private const float MaxLifetime = 6f;       // safety net against orphans

public void Init(Health targetHealth, int damageAmount, float projectileSpeed)
    {
        target = targetHealth;
        damage = damageAmount;
        speed = projectileSpeed;
        lastKnownPosition = target.transform.position + Vector3.up * AimHeightOffset;

        // Overkill prevention: commit this projectile's damage on the target
        // immediately, so towers scanning between now and impact can see the
        // target is already accounted for and pick someone else.
        target.ReservePendingDamage(damage);
    }

private void OnDestroy()
    {
        // Release the reservation no matter HOW this projectile ends - hit,
        // fizzle, or lifetime expiry all pass through here exactly once.
        // (If the target's GameObject is already gone, there's nothing to
        // release the reservation from, which is fine.)
        if (target != null)
            target.ReleasePendingDamage(damage);
    }


    private void Update()
    {
        lifetime += Time.deltaTime;
        if (lifetime > MaxLifetime)
        {
            Destroy(gameObject);
            return;
        }

        // Track a live target; a dead/destroyed one leaves its last position.
        if (target != null && !target.IsDead)
            lastKnownPosition = target.transform.position + Vector3.up * AimHeightOffset;

        transform.position = Vector3.MoveTowards(transform.position, lastKnownPosition, speed * Time.deltaTime);

        if (Vector3.Distance(transform.position, lastKnownPosition) <= HitDistance)
        {
            if (target != null && !target.IsDead)
                target.TakeDamage(damage);

            Destroy(gameObject);
        }
    }
}
