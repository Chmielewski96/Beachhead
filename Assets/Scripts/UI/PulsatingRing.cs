using UnityEngine;

/// <summary>
/// Pulsating color animation on top of a RingRenderer - used to mark wave
/// spawn points, so the player can see at a glance where enemies will come
/// from and build defenses accordingly. RingRenderer still owns the ring's
/// shape (radius/segments); this only drives the color/alpha pulse each
/// frame, overriding whatever static color RingRenderer set at Awake.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class PulsatingRing : MonoBehaviour
{
    [SerializeField] private Color baseColor = new Color(1f, 0.15f, 0.15f);
    [SerializeField] private float pulseSpeed = 2f;
    [SerializeField] private float minAlpha = 0.55f; // was 0.25 - dipping that low let strongly-colored backgrounds (e.g. the water) optically blend with the red and read as pink/magenta
    [SerializeField] private float maxAlpha = 1f;

    private LineRenderer line;

    private void Awake()
    {
        line = GetComponent<LineRenderer>();
    }

    private void Update()
    {
        float wave = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f; // 0..1
        Color c = baseColor;
        c.a = Mathf.Lerp(minAlpha, maxAlpha, wave);
        line.startColor = c;
        line.endColor = c;
    }
}
