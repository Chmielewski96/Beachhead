using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Jellyfish enemy. While alive it's a normal (if simple) threat: drifts
/// toward the Keep, biting whatever's nearest - no CrabAI-style blocked-
/// path wall-hunting, just plain proximity, since a jellyfish stuck at a
/// wall will naturally have that wall in range anyway.
///
/// Its real identity is the death rush: once HP drops to rushHpThreshold
/// (well before actually dying), it arms itself, hunts down the nearest
/// BUILDING, and beelines for it - pulsing a warning glow the whole way.
/// On arrival it detonates, damaging every Health (buildings AND units)
/// caught in the blast. Reaching 0 HP before it gets there cancels the
/// explosion entirely and it just dies quietly - killing it in time IS
/// the counterplay, which is also why the rush doesn't disable its
/// collider: it stays fully targetable the whole time it's rushing.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Health))]
public class JellyfishAI : MonoBehaviour
{
    private enum State { MarchingToKeep, AttackingTarget }

    [Header("Aggro")]
    [SerializeField] private float aggroRadius = 6f;
    [SerializeField] private float scanInterval = 0.25f;
    [Tooltip("What the jellyfish will attack while alive: Building | Unit.")]
    [SerializeField] private LayerMask targetMask;
    [Tooltip("Which target layers count as mobile units - top priority, and can be chased a little.")]
    [SerializeField] private LayerMask unitMask;
    [Tooltip("How far a chased unit can get before the jellyfish gives up and resumes marching.")]
    [SerializeField] private float chaseLeashRadius = 10f;

    [Header("Attack (while alive)")]
    [SerializeField] private int damage = 4;
    [SerializeField] private float attackRange = 1.2f;
    [SerializeField] private float attackCooldown = 1.2f;

    [Header("Floating")]
    [Tooltip("Height above the NavMesh the jellyfish hovers at.")]
    [SerializeField] private float hoverHeight = 1.6f;
    [SerializeField] private float bobAmplitude = 0.18f;
    [SerializeField] private float bobSpeed = 1.6f;

    [Header("Death Rush")]
    [Tooltip("Once Health drops to this or lower, the jellyfish arms itself and starts hunting for a building to detonate against - reaching 0 HP before it goes off cancels the explosion entirely.")]
    [SerializeField] private int rushHpThreshold = 20;
    [Tooltip("How far to search for a structure to rush at, measured from the jellyfish's position once it arms.")]
    [SerializeField] private float explosionSearchRadius = 15f;
    [Tooltip("Blast radius when it detonates - every building's Health in range takes the hit.")]
    [SerializeField] private float explosionRadius = 3.5f;
    [SerializeField] private int explosionDamage = 40;
    [Tooltip("If no building is found immediately, how often to keep checking before giving up (and re-arming on the next damage tick).")]
    [SerializeField] private float targetSearchRetryInterval = 1f;
    [Tooltip("Total time to keep searching for a building before giving up on this arming.")]
    [SerializeField] private float maxTargetSearchTime = 8f;
    [Tooltip("Movement speed during the death rush - faster than its normal drifting pace for a jolt of urgency.")]
    [SerializeField] private float rushSpeed = 6f;
    [Tooltip("Safety net: detonates in place if it hasn't reached anything by this long (e.g. the path is fully sealed).")]
    [SerializeField] private float maxRushDuration = 6f;
    [Tooltip("Warning pulse color during the rush (while still traveling toward the target) - builds tension before the blast.")]
    [SerializeField] private Color rushGlowColor = new Color(1f, 0.25f, 0.15f);
    [Tooltip("How long it sits primed once it's reached its target (or timed out), strobing white/red, before actually detonating. Still fully killable this whole time - the longer window is more counterplay, not less.")]
    [SerializeField] private float primeDuration = 1.5f;
    [Tooltip("How fast the white/red strobe alternates during priming.")]
    [SerializeField] private float primeFlashInterval = 0.12f;
    [Tooltip("VFX prefab spawned at the explosion point (e.g. JellyExplode). Not required, but the blast plays silently without it.")]
    [SerializeField] private GameObject deathEffectPrefab;
    [Tooltip("How long to keep the spawned effect alive before cleaning it up - should comfortably cover its longest particle lifetime.")]
    [SerializeField] private float deathEffectLifetime = 2f;

    [Header("Death (no nearby structure)")]
    [SerializeField] private float toppleDuration = 0.4f;
    [SerializeField] private float corpseLingerTime = 1f;

    private NavMeshAgent agent;
    private Health selfHealth;
    private State state = State.MarchingToKeep;
    private Health targetHealth;
    private Collider targetCollider;
    private Collider keepCollider;
    private float scanTimer;
    private float attackTimer;
    private Renderer[] renderers;
    private Color[] baseColors;
    private MaterialPropertyBlock glowBlock;
    private Transform visualBob;
    private bool rushArmed;
    private bool isRushing;
    private bool hasExploded;
    private Coroutine rushRoutine;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        selfHealth = GetComponent<Health>();
        selfHealth.OnDeath += HandleDeath;
        selfHealth.OnDamaged += HandleDamaged;

        agent.avoidancePriority = Random.Range(30, 70);
        agent.baseOffset = hoverHeight; // NavMeshAgent's own hover-above-mesh support

        // Wrap every existing visual child in one bob group so the whole
        // model floats together, without touching the root transform that
        // the agent and collider actually live on.
        GameObject bobGO = new GameObject("VisualBob");
        bobGO.transform.SetParent(transform, false);
        Transform[] existingChildren = new Transform[transform.childCount - 1];
        int idx = 0;
        foreach (Transform child in transform)
        {
            if (child == bobGO.transform)
                continue;
            existingChildren[idx++] = child;
        }
        foreach (Transform child in existingChildren)
            child.SetParent(bobGO.transform, true);
        visualBob = bobGO.transform;

        renderers = GetComponentsInChildren<Renderer>();
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            Material mat = renderers[i].sharedMaterial;
            baseColors[i] = (mat != null && mat.HasProperty(BaseColorId)) ? mat.GetColor(BaseColorId) : Color.white;
        }
        glowBlock = new MaterialPropertyBlock();
    }

    private void OnDestroy()
    {
        if (selfHealth != null)
        {
            selfHealth.OnDeath -= HandleDeath;
            selfHealth.OnDamaged -= HandleDamaged;
        }
    }

    private void Start()
    {
        if (Keep.Instance != null)
            keepCollider = Keep.Instance.GetComponentInChildren<Collider>();

        MarchToKeep();
    }

private void Update()
    {
        // Gentle bob, independent of navigation - purely the visual child.
        if (visualBob != null)
            visualBob.localPosition = new Vector3(0f, Mathf.Sin(Time.time * bobSpeed) * bobAmplitude, 0f);

        // While dead, HandleDeath's own sequence owns things. While actively
        // rushing a chosen target, the coroutine owns the agent entirely.
        if (selfHealth.IsDead || isRushing)
            return;

        if (!agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
                MarchToKeep();
            }
            return;
        }

        switch (state)
        {
            case State.MarchingToKeep: TickMarching(); break;
            case State.AttackingTarget: TickAttacking(); break;
        }
    }

    private void TickMarching()
    {
        scanTimer -= Time.deltaTime;
        if (scanTimer <= 0f)
        {
            scanTimer = scanInterval;
            if (TryAcquireTarget())
                state = State.AttackingTarget;
        }
    }

    private void TickAttacking()
    {
        if (targetHealth == null || targetHealth.IsDead)
        {
            MarchToKeep();
            return;
        }

        bool targetIsUnit = ((1 << targetCollider.gameObject.layer) & unitMask) != 0;
        if (targetIsUnit)
        {
            float targetDistance = Vector3.Distance(transform.position, targetCollider.ClosestPoint(transform.position));
            if (targetDistance > chaseLeashRadius)
            {
                if (!TryAcquireTarget())
                    MarchToKeep();
                return;
            }
        }

        Vector3 closestPoint = targetCollider.ClosestPoint(transform.position);
        float distance = Vector3.Distance(transform.position, closestPoint);

        if (distance > attackRange)
        {
            agent.isStopped = false;
            if (!agent.pathPending && (agent.destination - closestPoint).sqrMagnitude > 0.25f)
                agent.SetDestination(closestPoint);
        }
        else
        {
            agent.isStopped = true;
            attackTimer -= Time.deltaTime;
            if (attackTimer <= 0f)
            {
                attackTimer = attackCooldown;
                targetHealth.TakeDamage(damage);
            }
        }
    }

    private bool TryAcquireTarget()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, aggroRadius, targetMask);

        if (TryFindNearest(hits, unitMask, out Health unitHealth, out Collider unitCollider))
        {
            SetTarget(unitHealth, unitCollider);
            return true;
        }

        if (keepCollider != null && Keep.Instance != null)
        {
            Health keepHealth = Keep.Instance.GetComponent<Health>();
            if (keepHealth != null && !keepHealth.IsDead)
            {
                float keepDistance = Vector3.Distance(transform.position, keepCollider.ClosestPoint(transform.position));
                if (keepDistance <= aggroRadius)
                {
                    SetTarget(keepHealth, keepCollider);
                    return true;
                }
            }
        }

        // No path-blocked reasoning needed here (unlike CrabAI): a
        // jellyfish stuck at a wall will already have that wall inside
        // aggroRadius, so plain proximity naturally finds it.
        int buildingMask = targetMask.value & ~unitMask.value;
        if (TryFindNearest(hits, buildingMask, out Health buildingHealth, out Collider buildingCollider))
        {
            SetTarget(buildingHealth, buildingCollider);
            return true;
        }

        return false;
    }

    private bool TryFindNearest(Collider[] hits, LayerMask filter, out Health bestHealth, out Collider bestCollider)
    {
        float bestDistance = float.MaxValue;
        bestHealth = null;
        bestCollider = null;

        foreach (Collider hit in hits)
        {
            if (((1 << hit.gameObject.layer) & filter) == 0)
                continue;

            Health candidate = hit.GetComponentInParent<Health>();
            if (candidate == null || candidate.IsDead)
                continue;

            float distance = Vector3.Distance(transform.position, hit.ClosestPoint(transform.position));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestHealth = candidate;
                bestCollider = hit;
            }
        }

        return bestHealth != null;
    }

    private void SetTarget(Health health, Collider colliderIn)
    {
        targetHealth = health;
        targetCollider = colliderIn;
        state = State.AttackingTarget;
        attackTimer = attackCooldown * 0.5f;
    }

    private void MarchToKeep()
    {
        state = State.MarchingToKeep;
        targetHealth = null;
        targetCollider = null;

        if (Keep.Instance != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.SetDestination(Keep.Instance.transform.position);
        }
    }

/// <summary>
    /// Reaching 0 HP ALWAYS means a quiet death now - the explosion only
    /// happens if the rush (armed at rushHpThreshold) actually completes.
    /// Dying while a rush is in flight is exactly the intended counterplay:
    /// the player finished it off before it reached anything, so it just
    /// topples like any other kill.
    /// </summary>
    private void HandleDeath()
    {
        if (hasExploded)
            return; // Explode() already finalized everything - nothing more to do

        if (rushRoutine != null)
        {
            StopCoroutine(rushRoutine);
            rushRoutine = null;
        }
        isRushing = false;
        SetGlow(0f);

        if (agent.enabled && agent.isOnNavMesh)
            agent.isStopped = true;
        agent.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>())
            c.enabled = false;

        StartCoroutine(DeathSequence());
    }

/// <summary>
    /// Arms the death rush once HP drops to the threshold - well before
    /// actual death. Latches so it only triggers once per arming; if the
    /// search below gives up empty-handed, it un-latches so a later damage
    /// tick can try again (e.g. the player has since built something closer).
    /// </summary>
    private void HandleDamaged(int current, int max)
    {
        if (rushArmed || selfHealth.IsDead || current > rushHpThreshold)
            return;

        rushArmed = true;
        rushRoutine = StartCoroutine(RushAndExplode());
    }


/// <summary>
    /// Phase 1: search for a building to detonate against, without
    /// disturbing normal combat - the jellyfish keeps fighting/marching
    /// as usual and can still be killed outright (which cancels this).
    /// Phase 2: once found, take over the agent and beeline for it,
    /// pulsing a warning glow, until arrival or a timeout - then detonate.
    /// </summary>
    private IEnumerator RushAndExplode()
    {
        int buildingMask = targetMask.value & ~unitMask.value;
        Health rushTargetHealth = null;
        Collider rushTargetCollider = null;
        float searchElapsed = 0f;

        while (rushTargetHealth == null)
        {
            if (selfHealth.IsDead)
                yield break; // died mid-search - HandleDeath already cleaned up

            Collider[] hits = Physics.OverlapSphere(transform.position, explosionSearchRadius, buildingMask);
            TryFindNearest(hits, buildingMask, out rushTargetHealth, out rushTargetCollider);

            if (rushTargetHealth != null)
                break;

            searchElapsed += targetSearchRetryInterval;
            if (searchElapsed >= maxTargetSearchTime)
            {
                rushArmed = false; // give up for now - a later damage tick can re-arm and try again
                yield break;
            }

            yield return new WaitForSeconds(targetSearchRetryInterval);
        }

        isRushing = true;
        if (!agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit navHit, 3f, NavMesh.AllAreas))
                agent.Warp(navHit.position);
            else
            {
                isRushing = false;
                rushArmed = false;
                yield break; // can't navigate at all - stay armed for a future opportunity
            }
        }

        agent.speed = rushSpeed;
        agent.isStopped = false;
        agent.SetDestination(rushTargetCollider.ClosestPoint(transform.position));

        float elapsed = 0f;
        while (elapsed < maxRushDuration)
        {
            if (selfHealth.IsDead)
                yield break; // killed mid-rush - the whole point of the counterplay

            elapsed += Time.deltaTime;

            if (rushTargetHealth == null || rushTargetHealth.IsDead)
            {
                yield return PrimeThenExplode();
                yield break;
            }

            Vector3 closestPoint = rushTargetCollider.ClosestPoint(transform.position);
            float distance = Vector3.Distance(transform.position, closestPoint);

            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 10f);
            SetGlow(pulse);

            if (distance <= explosionRadius * 0.6f)
            {
                yield return PrimeThenExplode();
                yield break;
            }

            if (!agent.pathPending && (agent.destination - closestPoint).sqrMagnitude > 0.25f)
                agent.SetDestination(closestPoint);

            yield return null;
        }

        yield return PrimeThenExplode();
    }

/// <summary>
    /// Damages EVERYTHING with Health in the blast - buildings and units
    /// alike - even though only buildings were eligible as the rush's
    /// destination. Finalizes with a real TakeDamage so bounty/OnDeath
    /// listeners fire normally, same as any other kill.
    /// </summary>
    private void Explode()
    {
        hasExploded = true;
        isRushing = false;

        Collider[] hits = Physics.OverlapSphere(transform.position, explosionRadius, targetMask);
        HashSet<Health> alreadyHit = new HashSet<Health>();

        foreach (Collider hit in hits)
        {
            Health victim = hit.GetComponentInParent<Health>();
            if (victim == null || victim.IsDead || victim == selfHealth || alreadyHit.Contains(victim))
                continue;

            alreadyHit.Add(victim);
            victim.TakeDamage(explosionDamage);
        }

        SetGlow(0f);

        if (deathEffectPrefab != null)
        {
            GameObject effect = Instantiate(deathEffectPrefab, transform.position, Quaternion.identity);
            Destroy(effect, deathEffectLifetime);
        }

        if (!selfHealth.IsDead)
            selfHealth.TakeDamage(selfHealth.Current); // finalize death - bounty etc. fire normally

        Destroy(gameObject);
    }

private void SetGlow(float intensity)
    {
        SetGlow(intensity, rushGlowColor);
    }

    private void SetGlow(float intensity, Color color)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;

            glowBlock.Clear();
            if (intensity > 0f)
                glowBlock.SetColor(BaseColorId, Color.Lerp(baseColors[i], color, intensity));
            renderers[i].SetPropertyBlock(glowBlock);
        }
    }

/// <summary>
    /// Unlike SetGlow (which blends toward a color, fading in from the
    /// jellyfish's own base color), this snaps straight to the given
    /// color with no blend - what makes the priming strobe read as a
    /// sharp, unambiguous white/red alarm instead of a soft pulse.
    /// </summary>
    private void SetFlatColor(Color color)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;

            glowBlock.Clear();
            glowBlock.SetColor(BaseColorId, color);
            renderers[i].SetPropertyBlock(glowBlock);
        }
    }

/// <summary>
    /// Held state between arrival/timeout and the actual blast: strobes
    /// white/red for primeDuration, fully killable the entire time (the
    /// counterplay window just got longer, not narrower). Dying here
    /// cancels the explosion exactly like dying mid-rush does - HandleDeath
    /// already stopped this coroutine by the time it would resume.
    /// </summary>
    private IEnumerator PrimeThenExplode()
    {
        agent.isStopped = true;

        float elapsed = 0f;
        float flashTimer = 0f;
        bool showWhite = true;
        SetFlatColor(Color.white);

        while (elapsed < primeDuration)
        {
            if (selfHealth.IsDead)
                yield break; // killed while primed - explosion cancelled

            elapsed += Time.deltaTime;
            flashTimer += Time.deltaTime;

            if (flashTimer >= primeFlashInterval)
            {
                flashTimer = 0f;
                showWhite = !showWhite;
                SetFlatColor(showWhite ? Color.white : rushGlowColor);
            }

            yield return null;
        }

        Explode();
    }



    private IEnumerator DeathSequence()
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
