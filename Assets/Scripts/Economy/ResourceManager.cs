using System;
using UnityEngine;

/// <summary>
/// The shell stockpile. Two events drive everything downstream: the UI
/// counter listens to OnShellsChanged (never polls), and failure feedback
/// listens to OnSpendFailed. Spenders just call TrySpend and respect the
/// answer - nobody pokes the UI directly, the UI finds out on its own.
/// </summary>
public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance { get; private set; }

    [SerializeField] private int startingShells = 100;

    public int Shells { get; private set; }

    /// <summary>Fired on every balance change, with the new total.</summary>
    public event Action<int> OnShellsChanged;

    /// <summary>Fired when a TrySpend is refused - drive 'can't afford' feedback off this.</summary>
    public event Action OnSpendFailed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        Shells = startingShells;
    }

    private void Start()
    {
        // Announce the starting balance once, so any UI that subscribed
        // during its own Start gets initialized without polling.
        OnShellsChanged?.Invoke(Shells);
    }

    public bool TrySpend(int amount)
    {
        if (amount < 0 || Shells < amount)
        {
            OnSpendFailed?.Invoke();
            return false;
        }

        Shells -= amount;
        OnShellsChanged?.Invoke(Shells);
        return true;
    }

    public void Add(int amount)
    {
        if (amount <= 0)
            return;

        Shells += amount;
        OnShellsChanged?.Invoke(Shells);
    }
}
