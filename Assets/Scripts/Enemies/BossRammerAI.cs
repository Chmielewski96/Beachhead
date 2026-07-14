using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The boss's movement AI - deliberately NOT CrabAI. Every other crab
/// paths around obstacles via NavMeshAgent and only fights a building when
/// its path is genuinely blocked. This is the opposite: a straight line
/// to the Keep, full stop, with zero pathfinding and zero avoidance -
/// anything (building or unit) that ends up in the way just takes
/// continuous damage until it's gone, while the boss keeps walking
/// through the space it occupied. Retrofitting "ignore all obstacles"
/// into the shared CrabAI would have meant touching logic every other
/// crab in the game depends on; a dedicated script keeps this one boss's
/// very different behavior fully contained.
///
/// No NavMeshAgent is used for movement (Transform is driven directly),
/// but nothing else in the project requires one on this GameObject -
/// Tower/soldier targeting and KeepThreatDistance all work off Health and
/// Collider alone.
/// </summary>
[RequireComponent(typeof(Health))]
public class BossRammerAI : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float speed = 1.2f;
    [Tooltip("Stomping gait: moves for this long, then stops, then repeats - independent of anything in its path.")]
    [SerializeField] private float gaitMoveDuration = 2f;
    [SerializeField] private float gaitPauseDuration = 1f;
    [SerializeField] private float turnSpeed = 90f; // degrees/second - faces its travel direction

    [Header("Demolition")]
    [Tooltip("Anything with Health on these layers gets crushed on contact - buildings AND units alike, whatever is actually in the way.")]
    [SerializeField] private LayerMask demolishMask;
    [Tooltip("Which of the layers above count as buildings specifically - a building in range halts movement entirely until it's destroyed. Units in demolishMask but NOT in this mask get trampled without ever stopping the advance.")]
    [SerializeField] private LayerMask unitMask;
    [Tooltip("Engage range - how close a building needs to be before the boss halts and starts attacking it. Deliberately generous: this is a siege monster, not a brawler, so it doesn't need to be pressed right up against the wall.")]
    [SerializeField] private float demolishRadius = 6f;
    [Tooltip("Blast radius for the actual damage tick - bigger than the engage range on purpose, so buildings standing NEXT TO the one directly in front of him also take the hit, not just whatever triggered the stop.")]
    [SerializeField] private float aoeRadius = 9f;
    [SerializeField] private int damage = 100;
    [Tooltip("How long the wind-up lasts before the hit actually lands - shaking and flashing white the whole time. The next wind-up starts immediately after release if something's still in range.")]
    [SerializeField] private float windupDuration = 1f;
    [SerializeField] private float shakeMagnitude = 0.18f;
    [Tooltip("Flash color, ramping from alpha 0 at the start of the wind-up to full intensity at the exact instant of release.")]
    [SerializeField] private Color windupFlashColor = Color.white;
    [Tooltip("How far he lunges forward at the very end of the wind-up, right as the hit lands - the 'ramming into the wall' motion. Moves the ROOT (real, permanent progress toward the Keep), not just the visual model.")]
    [SerializeField] private float dashDistance = 1.5f;
    [SerializeField] private float dashDuration = 0.15f;
    [Tooltip("Rest period after a hit lands before the next wind-up is allowed to begin - without this he'd immediately start charging the next attack.")]
    [SerializeField] private float postAttackCooldown = 1.5f;

    [Header("Death")]
    [SerializeField] private float toppleDuration = 0.4f;
    [SerializeField] private float corpseLingerTime = 1f;

    private Health selfHealth;
    private float gaitTimer;
    private bool gaitPaused;
    private Renderer[] renderers;
    private Color[] baseColors;
    private MaterialPropertyBlock flashBlock;
    private Transform shakeTarget;
    private Vector3 shakeRestPosition;
    private Coroutine windupRoutine;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        selfHealth = GetComponent<Health>();
        selfHealth.OnDeath += HandleDeath;

        gaitTimer = gaitMoveDuration;

        renderers = GetComponentsInChildren<Renderer>();
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            Material mat = renderers[i].sharedMaterial;
            baseColors[i] = (mat != null && mat.HasProperty(BaseColorId)) ? mat.GetColor(BaseColorId) : Color.white;
        }
        flashBlock = new MaterialPropertyBlock();

        // Shake the VISUAL model only, never the root - the root's position
        // is what TickGait's straight-line math is built on, and jittering
        // it directly would permanently skew the boss's path with every
        // frame of shake instead of just looking like a shake.
        shakeTarget = transform.Find("Model");
        if (shakeTarget == null)
            shakeTarget = transform;
        shakeRestPosition = shakeTarget.localPosition;
    }

    private void OnDestroy()
    {
        if (selfHealth != null)
            selfHealth.OnDeath -= HandleDeath;
    }

private void Update()
    {
        if (selfHealth.IsDead)
            return;

        // A building in range plants its feet completely - no movement,
        // no gait ticking (the pause-for-a-fight time shouldn't eat into
        // the stomp gait's own rhythm) - until that building is rubble.
        // Units alone never stop it; they're in demolishMask too so they
        // still take damage on the tick below, just without halting the
        // advance the way a building does.
        if (!IsBuildingBlocking())
            TickGait();

        TryStartWindup();
    }

private bool IsBuildingBlocking()
    {
        int buildingMask = demolishMask.value & ~unitMask.value;
        if (buildingMask == 0)
            return false;

        Collider[] hits = Physics.OverlapSphere(transform.position, demolishRadius, buildingMask);
        foreach (Collider hit in hits)
        {
            Health candidate = hit.GetComponentInParent<Health>();
            if (candidate != null && !candidate.IsDead)
                return true;
        }
        return false;
    }


    private void TickGait()
    {
        gaitTimer -= Time.deltaTime;
        if (gaitTimer <= 0f)
        {
            gaitPaused = !gaitPaused;
            gaitTimer = gaitPaused ? gaitPauseDuration : gaitMoveDuration;
        }

        if (gaitPaused)
            return;

        Vector3 keepPosition = Keep.Instance != null ? Keep.Instance.transform.position : transform.position;
        Vector3 direction = keepPosition - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            return;
        direction.Normalize();

        // Straight-line translation - no NavMesh, no obstacle avoidance.
        // This is the entire point: nothing steers it around anything.
        transform.position += direction * speed * Time.deltaTime;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
    }

/// <summary>
    /// Starts a new wind-up-then-strike cycle if one isn't already running
    /// and something is actually in range to hit. The coroutine re-checks
    /// range itself when it finishes, so this only ever needs to kick off
    /// the FIRST cycle - it doesn't re-trigger mid-wind-up.
    /// </summary>
    private void TryStartWindup()
    {
        if (windupRoutine != null)
            return;

        Collider[] hits = Physics.OverlapSphere(transform.position, aoeRadius, demolishMask);
        if (hits.Length == 0)
            return;

        windupRoutine = StartCoroutine(WindupAndStrike());
    }

    /// <summary>
    /// Shakes and flashes white (alpha 0 -> full) for windupDuration, then
    /// releases: applies the AoE damage at the exact instant the flash
    /// peaks, snaps the flash back to zero, and immediately starts the
    /// next wind-up if anything's still standing in range.
    /// </summary>
private System.Collections.IEnumerator WindupAndStrike()
    {
        float elapsed = 0f;
        while (elapsed < windupDuration)
        {
            if (selfHealth.IsDead)
            {
                shakeTarget.localPosition = shakeRestPosition;
                SetGlow(0f);
                windupRoutine = null;
                yield break;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / windupDuration);

            Vector2 jitter = Random.insideUnitCircle * shakeMagnitude;
            shakeTarget.localPosition = shakeRestPosition + new Vector3(jitter.x, 0f, jitter.y);
            SetGlow(t);

            yield return null;
        }

        shakeTarget.localPosition = shakeRestPosition;
        SetGlow(1f); // exact peak at the moment of release

        yield return Dash();

        ApplyDemolishDamage();
        SetGlow(0f); // snap back down immediately after the hit lands

        // Rest before the next wind-up can begin. windupRoutine stays
        // non-null through this wait, which is exactly what keeps
        // TryStartWindup from firing again early - movement itself isn't
        // affected by this wait at all; if the target died, TickGait
        // resumes immediately since IsBuildingBlocking() no longer holds.
        yield return new WaitForSeconds(postAttackCooldown);

        windupRoutine = null; // next Update's TryStartWindup begins the next cycle, if anything remains
    }

    /// <summary>
    /// The ramming lunge: a quick, easing-out shove of the ROOT (real,
    /// permanent progress toward the Keep - this is why it's on the root
    /// and not shakeTarget) along the same forward direction TickGait uses.
    /// Runs after the shake/glow loop ends but before damage applies, so
    /// the hit visually lands right as the lunge completes.
    /// </summary>
    private System.Collections.IEnumerator Dash()
    {
        Vector3 keepPosition = Keep.Instance != null ? Keep.Instance.transform.position : transform.position;
        Vector3 direction = keepPosition - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            yield break;
        direction.Normalize();

        Vector3 start = transform.position;
        Vector3 end = start + direction * dashDistance;
        float elapsed = 0f;

        while (elapsed < dashDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / dashDuration);
            float eased = 1f - (1f - t) * (1f - t); // ease-out - fast start, settling into the hit
            transform.position = Vector3.Lerp(start, end, eased);
            yield return null;
        }

        transform.position = end;
    }

    private void ApplyDemolishDamage()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, aoeRadius, demolishMask);
        if (hits.Length == 0)
            return;

        HashSet<Health> alreadyHit = new HashSet<Health>();

        foreach (Collider hit in hits)
        {
            Health victim = hit.GetComponentInParent<Health>();
            if (victim == null || victim.IsDead || victim == selfHealth || alreadyHit.Contains(victim))
                continue;

            alreadyHit.Add(victim);
            victim.TakeDamage(damage);
        }
    }



private void SetGlow(float intensity)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;

            flashBlock.Clear();
            if (intensity > 0f)
                flashBlock.SetColor(BaseColorId, Color.Lerp(baseColors[i], windupFlashColor, intensity));
            renderers[i].SetPropertyBlock(flashBlock);
        }
    }

    private void HandleDeath()
    {
        foreach (Collider c in GetComponentsInChildren<Collider>())
            c.enabled = false;

        StartCoroutine(DeathSequence());
    }

    private System.Collections.IEnumerator DeathSequence()
    {
        Quaternion start = transform.rotation;
        Quaternion end = start * Quaternion.Euler(0f, 0f, 90f);
        float elapsed = 0f;

        while (elapsed < toppleDuration)
        {
            elapsed += Time.deltaTime;
            transform.rotation = Quaternion.Slerp(start, end, elapsed / toppleDuration);
            yield return null;
        }

        Destroy(gameObject, corpseLingerTime);
    }
}
