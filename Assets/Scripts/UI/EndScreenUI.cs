using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Victory/Defeat overlay. Subscribes to GameManager's events and shows
/// itself with the right headline. Buttons are wired here via AddListener
/// in Start - drag the Button references in the Inspector, no manual
/// OnClick() persistent-listener setup needed.
/// </summary>
public class EndScreenUI : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text titleLabel;
    [SerializeField] private Button mainMenuButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("Fade")]
    [Tooltip("CanvasGroup on this panel itself, so it fades IN instead of popping instantly.")]
    [SerializeField] private CanvasGroup panelCanvasGroup;
    [Tooltip("The always-on gameplay HUD (resource counters, build menu, info panel, demolish hint) - fades OUT as this panel fades in.")]
    [SerializeField] private CanvasGroup mainHudGroup;
    [Tooltip("Wave status/announcement - fades out alongside the main HUD.")]
    [SerializeField] private CanvasGroup waveHudGroup;
    [SerializeField] private float hudFadeOutDuration = 0.6f;
    [SerializeField] private float panelFadeInDuration = 0.6f;

private void Start()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = 0f;
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = false;
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnVictory += HandleVictory;
            GameManager.Instance.OnDefeat += HandleDefeat;
        }

        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(GoToMainMenu);
        if (restartButton != null) restartButton.onClick.AddListener(Restart);
        if (quitButton != null) quitButton.onClick.AddListener(QuitGame);
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnVictory -= HandleVictory;
            GameManager.Instance.OnDefeat -= HandleDefeat;
        }
    }

    private void HandleVictory() => Show("VICTORY!");
    private void HandleDefeat() => Show("DEFEAT");

private void Show(string title)
    {
        if (titleLabel != null)
            titleLabel.text = title;
        if (panelRoot != null)
            panelRoot.SetActive(true);

        StartCoroutine(FadeSequence());
    }

    /// <summary>
    /// Fades the gameplay HUD out while fading this panel in. Must use
    /// UNSCALED time - GameManager sets Time.timeScale = 0 BEFORE firing
    /// the event that leads here, so a coroutine using regular deltaTime
    /// would never progress and the fade would just freeze on frame one.
    /// </summary>
    private IEnumerator FadeSequence()
    {
        if (mainHudGroup != null) { mainHudGroup.interactable = false; mainHudGroup.blocksRaycasts = false; }
        if (waveHudGroup != null) { waveHudGroup.interactable = false; waveHudGroup.blocksRaycasts = false; }
        if (panelCanvasGroup != null) { panelCanvasGroup.interactable = true; panelCanvasGroup.blocksRaycasts = true; }

        float startMainHud = mainHudGroup != null ? mainHudGroup.alpha : 0f;
        float startWaveHud = waveHudGroup != null ? waveHudGroup.alpha : 0f;
        float startPanel = panelCanvasGroup != null ? panelCanvasGroup.alpha : 0f;

        float duration = Mathf.Max(hudFadeOutDuration, panelFadeInDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;

            if (mainHudGroup != null)
                mainHudGroup.alpha = Mathf.Lerp(startMainHud, 0f, Mathf.Clamp01(elapsed / hudFadeOutDuration));
            if (waveHudGroup != null)
                waveHudGroup.alpha = Mathf.Lerp(startWaveHud, 0f, Mathf.Clamp01(elapsed / hudFadeOutDuration));
            if (panelCanvasGroup != null)
                panelCanvasGroup.alpha = Mathf.Lerp(startPanel, 1f, Mathf.Clamp01(elapsed / panelFadeInDuration));

            yield return null;
        }

        if (mainHudGroup != null) mainHudGroup.alpha = 0f;
        if (waveHudGroup != null) waveHudGroup.alpha = 0f;
        if (panelCanvasGroup != null) panelCanvasGroup.alpha = 1f;
    }

    // Buttons work fine at Time.timeScale = 0 - UI/EventSystem input isn't
    // time-scaled, only Update-driven gameplay logic is.

    private void GoToMainMenu()
    {
        Time.timeScale = 1f; // scenes should always load at normal speed
        SceneManager.LoadScene(mainMenuSceneName);
    }

    private void Restart()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
