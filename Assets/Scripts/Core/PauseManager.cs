using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Esc/P toggles pause via Time.timeScale. Esc backs off if a modal tool
/// (building placement, demolish mode) is already using Esc to cancel
/// itself - otherwise pressing Esc while placing a wall would BOTH cancel
/// placement AND open the pause menu in the same frame. P has no such
/// conflict and always works - the WebGL-safe fallback the plan calls for
/// (browsers reserve Esc to exit fullscreen).
///
/// Also auto-pauses on OnApplicationFocus(false) - the classic WebGL
/// cursor-escape-on-click-away issue from Turtling, ported unchanged.
/// Both paths refuse to act once GameManager reports the game has ended,
/// so Esc/P/focus-loss can't un-freeze a finished game.
/// </summary>
public class PauseManager : MonoBehaviour
{
    public static PauseManager Instance { get; private set; }
    [SerializeField] private GameObject pausePanelRoot;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button mainMenuButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    public bool IsPaused { get; private set; }

private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (pausePanelRoot != null)
            pausePanelRoot.SetActive(false);

        if (resumeButton != null) resumeButton.onClick.AddListener(Resume);
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(GoToMainMenu);
        if (restartButton != null) restartButton.onClick.AddListener(Restart);
        if (quitButton != null) quitButton.onClick.AddListener(QuitGame);
    }

    private void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.HasEnded)
            return;

        bool modalToolActive =
            (BuildingPlacer.Instance != null && BuildingPlacer.Instance.IsPlacing) ||
            (DemolishInput.Instance != null && DemolishInput.Instance.IsDemolishing);

        if (Input.GetKeyDown(KeyCode.Escape) && !modalToolActive)
            TogglePause();

        if (Input.GetKeyDown(KeyCode.P))
            TogglePause();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus && !IsPaused && (GameManager.Instance == null || !GameManager.Instance.HasEnded))
            Pause();
    }

    private void TogglePause()
    {
        if (IsPaused) Resume();
        else Pause();
    }

    private void Pause()
    {
        IsPaused = true;
        Time.timeScale = 0f;
        if (pausePanelRoot != null)
            pausePanelRoot.SetActive(true);
    }

    private void Resume()
    {
        IsPaused = false;
        Time.timeScale = 1f;
        if (pausePanelRoot != null)
            pausePanelRoot.SetActive(false);
    }

    private void GoToMainMenu()
    {
        Time.timeScale = 1f;
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
