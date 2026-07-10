using UnityEngine;

/// <summary>
/// The Keep - the structure the whole game is about defending. Exposes a
/// singleton so enemies know where to march, and subscribes its own death
/// behavior to the shared Health component (the proper GameManager with a
/// lose screen arrives in Phase 9; for now, death logs Game Over).
/// </summary>
[RequireComponent(typeof(Health))]
public class Keep : MonoBehaviour
{
    public static Keep Instance { get; private set; }

    private Health health;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Two Keeps in the scene - destroying the extra one.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        health = GetComponent<Health>();
        health.OnDeath += HandleDeath;
    }

    private void OnDestroy()
    {
        if (health != null)
            health.OnDeath -= HandleDeath;
        if (Instance == this)
            Instance = null;
    }

    private void HandleDeath()
    {
        Debug.Log("GAME OVER - the Keep has fallen!");
        // Phase 9: GameManager.Instance.TriggerDefeat();
    }
}
