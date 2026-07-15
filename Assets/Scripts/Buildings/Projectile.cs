using System.Collections.Generic;
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
    private float splashRadius;
    private int splashDamage;
    private LayerMask splashMask;
    private float arcHeight;
    private GameObject impactEffectPrefab;
    private Vector3 lastKnownPosition;
    private Vector3 flightPosition; // the real homing position, BEFORE the visual arc offset - hit-detection reads this, never transform.position
    private float initialDistance;
    private float lifetime;

    private const float HitDistance = 0.4f;
    private const float AimHeightOffset = 0.5f; // aim at the body, not the feet
    private const float MaxLifetime = 6f;       // safety net against orphans

public void Init(Health targetHealth, int damageAmount, float projectileSpeed, float splashRadiusAmount = 0f, int splashDamageAmount = 0, LayerMask splashLayerMask = default, float arcHeightAmount = 0f, GameObject impactEffect = null)
    {
        impactEffectPrefab = impactEffect;
        target = targetHealth;
        damage = damageAmount;
        speed = projectileSpeed;
        splashRadius = splashRadiusAmount;
        splashDamage = splashDamageAmount;
        splashMask = splashLayerMask;
        arcHeight = arcHeightAmount;
        lastKnownPosition = target.transform.position + Vector3.up * AimHeightOffset;

        flightPosition = transform.position;
        initialDistance = Mathf.Max(0.01f, Vector3.Distance(flightPosition, lastKnownPosition));

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

/// <summary>
    /// Cannon-style AoE: everything else with Health within splashRadius of
    /// the impact point takes splashDamage - the primary target is excluded
    /// (it already took the full hit above) and no single victim is ever
    /// double-counted even if it has multiple colliders.
    /// </summary>
    private void ApplySplashDamage()
    {
        Collider[] hits = Physics.OverlapSphere(lastKnownPosition, splashRadius, splashMask);
        HashSet<Health> alreadyHit = new HashSet<Health>();
        if (target != null)
            alreadyHit.Add(target); // already took the direct hit

        foreach (Collider hit in hits)
        {
            Health victim = hit.GetComponentInParent<Health>();
            if (victim == null || victim.IsDead || alreadyHit.Contains(victim))
                continue;

            alreadyHit.Add(victim);
            victim.TakeDamage(splashDamage);
        }
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

        flightPosition = Vector3.MoveTowards(flightPosition, lastKnownPosition, speed * Time.deltaTime);

        // Purely cosmetic lob: a sine bump over the course of the flight,
        // peaking at the midpoint - conveys weight (a heavy cannonball lofted
        // by gravity) without touching the actual homing math above at all.
        // Hit-detection below reads flightPosition, which never sees this
        // offset, so the arc can never change WHEN something gets hit.
        if (arcHeight > 0f)
        {
            float remainingForArc = Vector3.Distance(flightPosition, lastKnownPosition);
            float progress = Mathf.Clamp01(1f - remainingForArc / initialDistance);
            float arcOffset = Mathf.Sin(progress * Mathf.PI) * arcHeight;
            transform.position = flightPosition + Vector3.up * arcOffset;
        }
        else
        {
            transform.position = flightPosition;
        }

        if (Vector3.Distance(flightPosition, lastKnownPosition) <= HitDistance)
        {
            if (target != null && !target.IsDead)
                target.TakeDamage(damage);

            if (splashRadius > 0f)
                ApplySplashDamage();

            if (impactEffectPrefab != null)
            {
                GameObject effect = Instantiate(impactEffectPrefab, flightPosition, Quaternion.identity);
                Destroy(effect, 2f); // covers the effect's own ~1.3s duration+lifetime comfortably
            }

            Destroy(gameObject);
        }
    }
}
