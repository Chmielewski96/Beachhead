using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Main menu: Play loads the gameplay scene, Quit exits. Wired via
/// AddListener in Start - drag the Button references, no manual OnClick()
/// persistent-listener setup needed.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [SerializeField] private Button playButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private string gameplaySceneName = "SampleScene";

    private void Start()
    {
        if (playButton != null) playButton.onClick.AddListener(Play);
        if (quitButton != null) quitButton.onClick.AddListener(Quit);
    }

    private void Play()
    {
        SceneManager.LoadScene(gameplaySceneName);
    }

    private void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
