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

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        if (health == null)
            health = GetComponentInParent<Health>();

        renderers = GetComponentsInChildren<Renderer>();
        propertyBlock = new MaterialPropertyBlock();
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
        SetTint(Color.white, false); // clearing the block restores the material's own color
        activeFlash = null;
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
