using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// World-space health bar: subscribes to a Health's OnDamaged event and
/// billboards toward the camera. Direct port of the Turtling pattern,
/// now event-driven instead of being poked by the health owner.
/// </summary>
public class WorldHealthBar : MonoBehaviour
{
    [Tooltip("Auto-found in parents if left empty.")]
    [SerializeField] private Health health;
    [SerializeField] private Slider slider;

    private void Awake()
    {
        if (health == null)
            health = GetComponentInParent<Health>();
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

    private void Start()
    {
        // Initialize after Health.Awake has set Current.
        slider.maxValue = health.Max;
        slider.value = health.Current;
    }

    private void HandleDamaged(int current, int max)
    {
        slider.maxValue = max;
        slider.value = current;
    }

    private void LateUpdate()
    {
        if (Camera.main != null)
            transform.rotation = Camera.main.transform.rotation;
    }
}
