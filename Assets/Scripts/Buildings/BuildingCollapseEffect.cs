using System.Collections;
using UnityEngine;

/// <summary>
/// Building-specific death responder: shakes, then sinks straight down out
/// of sight before the GameObject is actually destroyed - much clearer
/// than the plain instant-vanish DestroyOnDeath that units use, which is
/// why this is a SEPARATE component rather than an edit to DestroyOnDeath
/// (that script is shared with every unit prefab too; changing it there
/// would put this same shake-and-sink on dying soldiers and workers).
///
/// All animation happens on a wrapper transform holding the building's
/// original visual children - the root (and therefore the Collider and
/// any NavMeshObstacle) never moves, so the carved NavMesh hole and
/// physics footprint stay exactly where they were until the real Destroy
/// at the end, and other systems reading the root's position mid-collapse
/// aren't confused by it sinking.
/// </summary>
[RequireComponent(typeof(Health))]
public class BuildingCollapseEffect : MonoBehaviour
{
    [Header("Shake")]
    [SerializeField] private float shakeDuration = 0.3f;
    [SerializeField] private float shakeMagnitude = 0.12f;

    [Header("Sink")]
    [SerializeField] private float sinkDuration = 0.7f;
    [SerializeField] private float sinkDepth = 2.5f;

    [Header("Cleanup")]
    [Tooltip("Extra pause after fully sunk, before the GameObject is actually destroyed.")]
    [SerializeField] private float destroyDelay = 0.1f;

    private Health health;
    private Transform visualRoot;

    private void Awake()
    {
        health = GetComponent<Health>();
        health.OnDeath += HandleDeath;

        // Same wrapper trick as the Jellyfish's float bob: gather every
        // pre-existing child under one group so shake/sink only moves the
        // visuals, never the root the Collider/NavMeshObstacle live on.
        GameObject wrapper = new GameObject("CollapseVisualRoot");
        wrapper.transform.SetParent(transform, false);

        Transform[] existingChildren = new Transform[transform.childCount - 1];
        int idx = 0;
        foreach (Transform child in transform)
        {
            if (child == wrapper.transform)
                continue;
            existingChildren[idx++] = child;
        }
        foreach (Transform child in existingChildren)
            child.SetParent(wrapper.transform, true);

        visualRoot = wrapper.transform;
    }

    private void OnDestroy()
    {
        if (health != null)
            health.OnDeath -= HandleDeath;
    }

    private void HandleDeath()
    {
        // Stop counting as a live target immediately - a building mid-collapse
        // shouldn't still be attackable or block projectiles/units.
        foreach (Collider c in GetComponentsInChildren<Collider>())
            c.enabled = false;

        StartCoroutine(CollapseSequence());
    }

    private IEnumerator CollapseSequence()
    {
        // Phase 1: shake in place - the "structural failure" tell before it goes.
        float elapsed = 0f;
        while (elapsed < shakeDuration)
        {
            elapsed += Time.deltaTime;
            Vector2 jitter = Random.insideUnitCircle * shakeMagnitude;
            visualRoot.localPosition = new Vector3(jitter.x, 0f, jitter.y);
            yield return null;
        }

        // Phase 2: sink straight down, jitter fading out over the first half
        // so the collapse reads as one continuous motion rather than two
        // disconnected effects stitched together.
        elapsed = 0f;
        while (elapsed < sinkDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / sinkDuration;

            float fadingShake = shakeMagnitude * Mathf.Clamp01(1f - t * 2f);
            Vector2 jitter = fadingShake > 0f ? Random.insideUnitCircle * fadingShake : Vector2.zero;

            // Ease-in (t*t): starts slow, accelerates - reads as sinking
            // under its own weight rather than a uniform elevator descent.
            float depth = sinkDepth * (t * t);

            visualRoot.localPosition = new Vector3(jitter.x, -depth, jitter.y);
            yield return null;
        }

        visualRoot.localPosition = new Vector3(0f, -sinkDepth, 0f);

        if (destroyDelay > 0f)
            yield return new WaitForSeconds(destroyDelay);

        Destroy(gameObject);
    }
}
