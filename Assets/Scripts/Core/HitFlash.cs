using System.Collections;
using UnityEngine;

/// <summary>
/// Red flash on damage via MaterialPropertyBlock - the Turtling technique,
/// now triggered by Health.OnDamaged instead of being called directly.
/// Clearing the property block afterward restores the original material
/// color without ever touching (or leaking) the shared material.
/// </summary>
public class HitFlash : MonoBehaviour
{
    [Tooltip("Auto-found in parents if left empty.")]
    [SerializeField] private Health health;
    [SerializeField] private Color flashColor = Color.red;
    [SerializeField] private float flashDuration = 0.12f;

    private Renderer[] renderers;
    private MaterialPropertyBlock propertyBlock;
    private Coroutine activeFlash;

    // Sustained tint (e.g. a Hunter's aiming glow) is independent of the
    // one-shot damage flash: a hit during aiming briefly shows the red
    // flash color, then settles back to the sustained tint automatically.
    private bool sustainedActive;
    private Color sustainedColor;
    private float sustainedIntensity; // 0 = material's own color, 1 = fully sustainedColor
    private Color[] baseColors; // each renderer's own color, cached once - the blend target for a fade

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

private void Awake()
    {
        if (health == null)
            health = GetComponentInParent<Health>();

        renderers = GetComponentsInChildren<Renderer>();
        propertyBlock = new MaterialPropertyBlock();

        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            Material mat = renderers[i].sharedMaterial;
            if (mat != null && mat.HasProperty(BaseColorId))
                baseColors[i] = mat.GetColor(BaseColorId);
            else
                baseColors[i] = Color.white;
        }
    }

    private void OnEnable()
    {
        if (health != null)
            health.OnDamaged += HandleDamaged;
    }

    private void OnDisable()
    {
        if (health != null)
            health.OnDamaged -= HandleDamaged;
    }

    private void HandleDamaged(int current, int max)
    {
        if (activeFlash != null)
            StopCoroutine(activeFlash);
        activeFlash = StartCoroutine(Flash());
    }

private IEnumerator Flash()
    {
        SetTint(flashColor, true);
        yield return new WaitForSeconds(flashDuration);

        // Settle back to whatever's underneath - the sustained tint if one
        // is still active, otherwise each renderer's own color.
        if (sustainedActive)
            ApplySustainedTint();
        else
            SetTint(Color.white, false);

        activeFlash = null;
    }

/// <summary>
    /// Held tint that persists until explicitly cleared - used for states
    /// like a Hunter's aiming glow, as opposed to the brief damage flash.
    /// A damage flash mid-sustain still shows its own color, then returns
    /// here rather than to the material's original color.
    /// </summary>
/// <summary>
    /// Held tint that persists until explicitly cleared - used for states
    /// like a Hunter's aiming glow, as opposed to the brief damage flash.
    /// Call every frame with an updated intensity to fade it in/out (e.g.
    /// aim progress 0->1); a damage flash mid-sustain still shows its own
    /// color, then returns here rather than to the material's own color.
    /// </summary>
    public void SetSustainedTint(Color color, float intensity = 1f)
    {
        sustainedActive = true;
        sustainedColor = color;
        sustainedIntensity = Mathf.Clamp01(intensity);

        // A damage flash coroutine already owns the renderer right now -
        // let it keep running; it'll settle into this tint when it ends.
        if (activeFlash == null)
            ApplySustainedTint();
    }

public void ClearSustainedTint()
    {
        sustainedActive = false;
        sustainedIntensity = 0f;

        if (activeFlash == null)
            SetTint(Color.white, false);
    }

/// <summary>Blends each renderer's own cached color toward sustainedColor by sustainedIntensity - the fade-in/out itself.</summary>
    private void ApplySustainedTint()
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            propertyBlock.Clear();
            Color blended = Color.Lerp(baseColors[i], sustainedColor, sustainedIntensity);
            propertyBlock.SetColor(BaseColorId, blended);
            renderers[i].SetPropertyBlock(propertyBlock);
        }
    }



    private void SetTint(Color color, bool apply)
    {
        foreach (Renderer r in renderers)
        {
            propertyBlock.Clear();
            if (apply)
                propertyBlock.SetColor(BaseColorId, color);
            r.SetPropertyBlock(propertyBlock);
        }
    }
}
