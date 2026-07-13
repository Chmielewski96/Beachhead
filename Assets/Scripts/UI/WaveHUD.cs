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

    private Coroutine announcementRoutine;

    private void Start()
    {
        if (announcementLabel != null)
            announcementLabel.alpha = 0f;

        if (WaveManager.Instance != null)
        {
            WaveManager.Instance.OnBuildPhaseStarted += HandleBuildPhaseStarted;
            WaveManager.Instance.OnCombatPhaseStarted += HandleCombatPhaseStarted;
            WaveManager.Instance.OnWaveCleared += HandleWaveCleared;
            WaveManager.Instance.OnVictory += HandleVictory;
        }
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
    }

    private void HandleCombatPhaseStarted(int waveNumber)
    {
        Announce("Wave " + waveNumber + "!");
    }

    private void HandleWaveCleared(int waveNumber)
    {
        // Build-phase-started handles the 'Wave cleared!' announcement so the
        // two never overlap; this handler exists for future hooks (audio etc).
    }

    private void HandleVictory()
    {
        Announce("VICTORY!");
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

        yield return new WaitForSeconds(announcementHoldTime);

        float elapsed = 0f;
        while (elapsed < announcementFadeTime)
        {
            elapsed += Time.deltaTime;
            announcementLabel.alpha = 1f - (elapsed / announcementFadeTime);
            yield return null;
        }

        announcementLabel.alpha = 0f;
        announcementRoutine = null;
    }
}
