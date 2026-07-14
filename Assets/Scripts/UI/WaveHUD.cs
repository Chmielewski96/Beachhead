using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Wave status HUD: a persistent status line (build countdown / enemies
/// remaining) and a big center-screen announcement that fades ("Wave 3
/// incoming!", "Wave cleared!", "VICTORY!"). Purely event-driven off
/// WaveManager, except the countdown text which reads the public timer.
/// </summary>
public class WaveHUD : MonoBehaviour
{
    [Tooltip("Persistent status line, e.g. top-center: 'Wave 3 in 0:27' / 'Wave 3 - enemies: 12'.")]
    [SerializeField] private TMP_Text statusLabel;
    [Tooltip("Big center-screen text that appears briefly and fades.")]
    [SerializeField] private TMP_Text announcementLabel;
    [SerializeField] private float announcementHoldTime = 1.5f;
    [SerializeField] private float announcementFadeTime = 1f;

    [Header("Skip Build Phase")]
    [Tooltip("Shown only during the Build phase - lets the player call the wave in early (small shell bonus, see WaveManager.SkipBuildPhase).")]
    [SerializeField] private UnityEngine.UI.Button skipBuildButton;

    [Header("Boss Health Bar (final wave only)")]
    [Tooltip("Occupies the SAME screen slot as the announcement text on the final wave - there's no 'Wave N!' flash for a one-enemy boss wave, this replaces it for the whole fight.")]
    [SerializeField] private CanvasGroup bossHealthBarGroup;
    [SerializeField] private TMP_Text bossNameLabel;
    [SerializeField] private UnityEngine.UI.Slider bossHealthSlider;
    [SerializeField] private string bossDisplayName = "GIANT PALM CRAB";

    private Coroutine announcementRoutine;
    private Health trackedBossHealth;

private void Start()
    {
        if (announcementLabel != null)
            announcementLabel.alpha = 0f;

        if (bossHealthBarGroup != null)
            bossHealthBarGroup.alpha = 0f;

        if (WaveManager.Instance != null)
        {
            WaveManager.Instance.OnBuildPhaseStarted += HandleBuildPhaseStarted;
            WaveManager.Instance.OnCombatPhaseStarted += HandleCombatPhaseStarted;
            WaveManager.Instance.OnWaveCleared += HandleWaveCleared;
            WaveManager.Instance.OnVictory += HandleVictory;
        }

        if (skipBuildButton != null)
            skipBuildButton.onClick.AddListener(HandleSkipClicked);
    }

    private void OnDestroy()
    {
        if (WaveManager.Instance != null)
        {
            WaveManager.Instance.OnBuildPhaseStarted -= HandleBuildPhaseStarted;
            WaveManager.Instance.OnCombatPhaseStarted -= HandleCombatPhaseStarted;
            WaveManager.Instance.OnWaveCleared -= HandleWaveCleared;
            WaveManager.Instance.OnVictory -= HandleVictory;
        }

        UnsubscribeBossHealth();
    }

    private void Update()
    {
        if (statusLabel == null || WaveManager.Instance == null)
            return;

        var wm = WaveManager.Instance;
        switch (wm.CurrentPhase)
        {
            case WaveManager.Phase.Build:
                int seconds = Mathf.CeilToInt(wm.BuildTimeRemaining);
                statusLabel.text = "Wave " + wm.CurrentWaveNumber + "/" + wm.TotalWaves
                    + " in " + (seconds / 60) + ":" + (seconds % 60).ToString("00");
                break;
            case WaveManager.Phase.Combat:
                statusLabel.text = "Wave " + wm.CurrentWaveNumber + "/" + wm.TotalWaves
                    + " - enemies: " + wm.EnemiesAlive;
                break;
            case WaveManager.Phase.Victory:
                statusLabel.text = "All waves cleared!";
                break;
        }
    }

private void HandleBuildPhaseStarted(int waveNumber, float duration)
    {
        if (waveNumber > 1)
            Announce("Wave cleared!");

        if (skipBuildButton != null)
            skipBuildButton.gameObject.SetActive(true);
    }

private void HandleCombatPhaseStarted(int waveNumber)
    {
        // The final wave is a single boss, not a swarm - a flashing 'Wave 11!'
        // reads as an anticlimax for a one-enemy encounter. Show its health
        // bar in the exact same screen slot instead, for the whole fight.
        if (WaveManager.Instance != null && waveNumber == WaveManager.Instance.TotalWaves)
            ShowBossHealthBar();
        else
            Announce("Wave " + waveNumber + "!");

        if (skipBuildButton != null)
            skipBuildButton.gameObject.SetActive(false);
    }

    private void HandleWaveCleared(int waveNumber)
    {
        // Build-phase-started handles the 'Wave cleared!' announcement so the
        // two never overlap; this handler exists for future hooks (audio etc).
    }

private void HandleVictory()
    {
        HideBossHealthBar(); // safety net - the boss's own OnDeath already hides it
        Announce("VICTORY!");

        if (skipBuildButton != null)
            skipBuildButton.gameObject.SetActive(false);
    }

/// <summary>
    /// Finds the boss already spawned by WaveManager, binds its Health to
    /// the slider, and reveals the group. If the boss can't be found (a
    /// bad wave/prefab setup), this quietly does nothing rather than show
    /// an empty bar - the normal announcement is skipped either way, which
    /// is an acceptable trade for how this is only ever reached once, on
    /// the last wave.
    /// </summary>
    private void ShowBossHealthBar()
    {
        if (bossHealthBarGroup == null || bossHealthSlider == null)
            return;

        BossRammerAI boss = FindFirstObjectByType<BossRammerAI>();
        if (boss == null)
            return;

        Health bossHealth = boss.GetComponent<Health>();
        if (bossHealth == null)
            return;

        UnsubscribeBossHealth(); // in case of a leftover subscription somehow
        trackedBossHealth = bossHealth;
        trackedBossHealth.OnDamaged += HandleBossDamaged;
        trackedBossHealth.OnDeath += HandleBossDeath;

        if (bossNameLabel != null)
            bossNameLabel.text = bossDisplayName;

        bossHealthSlider.maxValue = trackedBossHealth.Max;
        bossHealthSlider.value = trackedBossHealth.Current;

        bossHealthBarGroup.alpha = 1f;
    }

    private void HandleBossDamaged(int current, int max)
    {
        if (bossHealthSlider == null)
            return;

        bossHealthSlider.maxValue = max;
        bossHealthSlider.value = current;
    }

    private void HandleBossDeath()
    {
        HideBossHealthBar();
    }

    private void HideBossHealthBar()
    {
        UnsubscribeBossHealth();

        if (bossHealthBarGroup != null)
            bossHealthBarGroup.alpha = 0f;
    }

    private void UnsubscribeBossHealth()
    {
        if (trackedBossHealth == null)
            return;

        trackedBossHealth.OnDamaged -= HandleBossDamaged;
        trackedBossHealth.OnDeath -= HandleBossDeath;
        trackedBossHealth = null;
    }


    private void HandleSkipClicked()
    {
        // Same modal-tool discipline as every other input path in the
        // project - a click during the intro or while paused shouldn't
        // reach through, even though this is a direct Button.onClick rather
        // than a MouseWorld raycast.
        if (PauseManager.Instance != null && PauseManager.Instance.IsPaused)
            return;
        if (IntroSequence.Instance != null && IntroSequence.Instance.IsIntroActive)
            return;

        if (WaveManager.Instance != null)
            WaveManager.Instance.SkipBuildPhase();
    }

    private void Announce(string message)
    {
        if (announcementLabel == null)
            return;

        if (announcementRoutine != null)
            StopCoroutine(announcementRoutine);
        announcementRoutine = StartCoroutine(AnnounceRoutine(message));
    }

private IEnumerator AnnounceRoutine(string message)
    {
        announcementLabel.text = message;
        announcementLabel.alpha = 1f;

        yield return new WaitForSecondsRealtime(announcementHoldTime);

        // Unscaled: the VICTORY announcement fires from the same event
        // chain that sets Time.timeScale = 0, so scaled deltaTime here
        // would freeze the fade forever instead of ever completing it -
        // same root cause as the EndScreenUI fade.
        float elapsed = 0f;
        while (elapsed < announcementFadeTime)
        {
            elapsed += Time.unscaledDeltaTime;
            announcementLabel.alpha = 1f - (elapsed / announcementFadeTime);
            yield return null;
        }

        announcementLabel.alpha = 0f;
        announcementRoutine = null;
    }
}
