using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Building health bar: hidden by default, pops fully visible the instant
/// the building takes damage, holds for visibleDuration, then fades out -
/// as opposed to WorldHealthBar (units/enemies), which is always on
/// screen. Buildings sit still and are numerous, so a permanently-visible
/// bar on every wall segment would be constant background clutter; only
/// showing it right after a hit draws the eye exactly when it matters.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class BuildingHealthBar : MonoBehaviour
{
    [Tooltip("Auto-found in parents if left empty.")]
    [SerializeField] private Health health;
    [SerializeField] private Slider slider;
    [Tooltip("How long the bar stays fully visible after the most recent hit before it starts fading.")]
    [SerializeField] private float visibleDuration = 5f;
    [SerializeField] private float fadeDuration = 0.6f;

    private CanvasGroup canvasGroup;
    private float hideTimer;

    private void Awake()
    {
        if (health == null)
            health = GetComponentInParent<Health>();

        canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f; // hidden until the first hit
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
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
        // Initialize the fill level even while hidden, so the very first
        // reveal already shows the correct proportion instead of a snap.
        slider.maxValue = health.Max;
        slider.value = health.Current;
    }

    private void HandleDamaged(int current, int max)
    {
        slider.maxValue = max;
        slider.value = current;

        canvasGroup.alpha = 1f; // snap back to fully visible - a fresh hit interrupts any fade in progress
        hideTimer = visibleDuration;
    }

    private void Update()
    {
        if (canvasGroup.alpha <= 0f)
            return; // already fully hidden - nothing to tick

        if (hideTimer > 0f)
        {
            hideTimer -= Time.deltaTime;
            return;
        }

        canvasGroup.alpha = Mathf.Max(0f, canvasGroup.alpha - Time.deltaTime / fadeDuration);
    }

    private void LateUpdate()
    {
        if (Camera.main != null)
            transform.rotation = Camera.main.transform.rotation;
    }
}
