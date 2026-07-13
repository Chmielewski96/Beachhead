using System.Collections;
using UnityEngine;

/// <summary>
/// One-time opening sequence: camera starts zoomed far out over the whole
/// map, slowly eases in to settle on the Keep, then the main HUD fades in
/// and player input unlocks. After a further deliberate pause (attention
/// stays on the Keep and the worker already gathering on its own), the
/// wave counter fades in separately and ONLY THEN does WaveManager's
/// build-phase countdown actually start ticking - see the guard in
/// WaveManager.Update(). PauseManager is completely untouched by this and
/// keeps working normally throughout: every timer in here runs on
/// Time.deltaTime like everything else in the project, so pressing pause
/// mid-intro freezes it exactly like it freezes anything else, no special
/// casing needed.
/// </summary>
public class IntroSequence : MonoBehaviour
{
    public static IntroSequence Instance { get; private set; }

    [Header("Camera Flythrough")]
    [SerializeField] private float introZoomDistance = 220f;
    [SerializeField] private float zoomInDuration = 5f;
    [SerializeField] private AnimationCurve zoomEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Main HUD (resource counters, build menu, etc.)")]
    [SerializeField] private CanvasGroup mainHudGroup;
    [SerializeField] private float mainHudFadeDuration = 1f;

    [Header("Wave Counter (deliberately delayed)")]
    [Tooltip("Extra pause after the main HUD appears, before the wave counter reveals itself and the build-phase timer actually starts ticking.")]
    [SerializeField] private float delayBeforeWaveCounter = 2.5f;
    [SerializeField] private CanvasGroup waveHudGroup;
    [SerializeField] private float waveHudFadeDuration = 0.75f;

    /// <summary>True for the whole sequence - input scripts gate off this, alongside their existing PauseManager check.</summary>
    public bool IsIntroActive { get; private set; } = true;

    /// <summary>True only once the wave counter has revealed itself - WaveManager gates its build-phase countdown on this.</summary>
    public bool WaveTimerUnlocked { get; private set; } = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (mainHudGroup != null) mainHudGroup.alpha = 0f;
        if (waveHudGroup != null) waveHudGroup.alpha = 0f;
    }

    private void Start()
    {
        StartCoroutine(RunSequence());
    }

    private IEnumerator RunSequence()
    {
        yield return StartCoroutine(ZoomIn());

        if (mainHudGroup != null)
            yield return StartCoroutine(FadeGroup(mainHudGroup, mainHudFadeDuration));

        // Camera framing and the main HUD are settled - hand control to the player.
        IsIntroActive = false;

        yield return new WaitForSeconds(delayBeforeWaveCounter);

        WaveTimerUnlocked = true;
        if (waveHudGroup != null)
            yield return StartCoroutine(FadeGroup(waveHudGroup, waveHudFadeDuration));
    }

    private IEnumerator ZoomIn()
    {
        RTSCameraController cam = RTSCameraController.Instance;
        if (cam == null)
            yield break;

        cam.AcceptInput = false;

        float startZoom = introZoomDistance;
        float endZoom = cam.CurrentZoomDistance; // whatever the rig's normal resting zoom already is
        cam.SetZoomDistance(startZoom);

        float elapsed = 0f;
        while (elapsed < zoomInDuration)
        {
            elapsed += Time.deltaTime;
            float t = zoomEase.Evaluate(Mathf.Clamp01(elapsed / zoomInDuration));
            cam.SetZoomDistance(Mathf.Lerp(startZoom, endZoom, t));
            yield return null;
        }

        cam.SetZoomDistance(endZoom);
        cam.AcceptInput = true;
    }

    private IEnumerator FadeGroup(CanvasGroup group, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            group.alpha = Mathf.Clamp01(elapsed / duration);
            yield return null;
        }
        group.alpha = 1f;
    }
}
