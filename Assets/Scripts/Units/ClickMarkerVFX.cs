using UnityEngine;

/// <summary>
/// Simple shrink-and-destroy feedback effect for movement orders. Attach
/// to a small flattened ring/disc prefab (with its Collider removed) - it
/// handles its own lifetime, nothing else needs to reference it directly.
/// </summary>
public class ClickMarkerVFX : MonoBehaviour
{
    [SerializeField] private float lifetime = 0.5f;
    [SerializeField] private float startScaleMultiplier = 1.5f;
    [SerializeField] private float endScaleMultiplier = 0.1f;

    private float elapsed;
    private Vector3 baseScale;


private void Start()
    {
        // Preserve whatever flattened proportions you set in the Inspector -
        // we scale relative to that, not to a uniform cube.
        baseScale = transform.localScale;
        transform.localScale = baseScale * startScaleMultiplier;
    }

private void Update()
    {
        elapsed += Time.deltaTime;
        float t = elapsed / lifetime;

        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        transform.localScale = baseScale * Mathf.Lerp(startScaleMultiplier, endScaleMultiplier, t);
    }
}
