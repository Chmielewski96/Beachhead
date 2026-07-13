using UnityEngine;

/// <summary>
/// Tracks overall game state - Playing, Victory, or Defeat - by
/// subscribing to WaveManager.OnVictory and the Keep's own Health.OnDeath
/// (one more subscriber to the same event that ResourceBounty, DestroyOnDeath,
/// and HitFlash already listen to elsewhere - Health still has no idea any
/// of them exist). Freezes time on either outcome so the world stops mid-
/// action instead of enemies/towers continuing to churn behind the end screen.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState { Playing, Victory, Defeat }

    public GameState CurrentState { get; private set; } = GameState.Playing;
    public bool HasEnded => CurrentState != GameState.Playing;

    public event System.Action OnVictory;
    public event System.Action OnDefeat;

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
        if (WaveManager.Instance != null)
            WaveManager.Instance.OnVictory += HandleVictory;

        if (Keep.Instance != null)
        {
            Health keepHealth = Keep.Instance.GetComponent<Health>();
            if (keepHealth != null)
                keepHealth.OnDeath += HandleDefeat;
        }
    }

    private void OnDestroy()
    {
        if (WaveManager.Instance != null)
            WaveManager.Instance.OnVictory -= HandleVictory;

        if (Keep.Instance != null)
        {
            Health keepHealth = Keep.Instance.GetComponent<Health>();
            if (keepHealth != null)
                keepHealth.OnDeath -= HandleDefeat;
        }
    }

    private void HandleVictory()
    {
        if (HasEnded)
            return;

        CurrentState = GameState.Victory;
        Time.timeScale = 0f;
        OnVictory?.Invoke();
    }

    private void HandleDefeat()
    {
        if (HasEnded)
            return;

        CurrentState = GameState.Defeat;
        Time.timeScale = 0f;
        OnDefeat?.Invoke();
    }
}
