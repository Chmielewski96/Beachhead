using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the full game loop: Build phase (countdown, player prepares) ->
/// Combat phase (spawn the wave, track deaths via Health.OnDeath) -> wave
/// cleared -> next build phase -> ... -> Victory after the last wave.
/// Everything downstream (HUD, announcements, the Phase 9 GameManager) is
/// event-driven - this manager never touches UI directly.
/// </summary>
public class WaveManager : MonoBehaviour
{
    public static WaveManager Instance { get; private set; }

    public enum Phase { Build, Combat, Victory }

    [SerializeField] private WaveData[] waves;
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private float scatterRadius = 3f;
    [Tooltip("Shell reward for starting a wave early - classic TD risk/reward.")]
    [SerializeField] private int earlyStartShellBonus = 10;

    public Phase CurrentPhase { get; private set; } = Phase.Build;
    /// <summary>1-based number of the wave being prepared for or fought.</summary>
    public int CurrentWaveNumber { get; private set; }
    public int TotalWaves => waves != null ? waves.Length : 0;
    public float BuildTimeRemaining { get; private set; }
    public int EnemiesAlive { get; private set; }

    public event Action<int, float> OnBuildPhaseStarted; // (waveNumber, duration)
    public event Action<int> OnCombatPhaseStarted;       // (waveNumber)
    public event Action<int> OnWaveCleared;              // (waveNumber)
    public event Action OnVictory;
    public event Action<int> OnEnemiesAliveChanged;      // (aliveCount)

    private int waveIndex;

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
        if (waves == null || waves.Length == 0)
        {
            Debug.LogWarning("WaveManager: no waves assigned.");
            enabled = false;
            return;
        }

        BeginBuildPhase(0);
    }

private void Update()
    {
        if (CurrentPhase != Phase.Build)
            return;

        // The intro sequence deliberately holds this countdown so the
        // player doesn't lose real build-phase seconds to the opening
        // camera flythrough - the wave counter UI stays hidden until this
        // unlocks, so the reveal and the clock actually starting line up.
        if (IntroSequence.Instance != null && !IntroSequence.Instance.WaveTimerUnlocked)
            return;

        BuildTimeRemaining -= Time.deltaTime;
        if (BuildTimeRemaining <= 0f)
            StartCombatPhase();
    }

    /// <summary>Player-triggered early start (Phase 9 gives it a button; until then, any caller works). Pays a small shell bonus.</summary>
    public void SkipBuildPhase()
    {
        if (CurrentPhase != Phase.Build)
            return;

        if (ResourceManager.Instance != null && earlyStartShellBonus > 0)
            ResourceManager.Instance.Add(ResourceType.Shells, earlyStartShellBonus);

        StartCombatPhase();
    }

    private void BeginBuildPhase(int index)
    {
        waveIndex = index;
        CurrentWaveNumber = index + 1;
        CurrentPhase = Phase.Build;
        BuildTimeRemaining = waves[index].buildPhaseDuration;
        OnBuildPhaseStarted?.Invoke(CurrentWaveNumber, BuildTimeRemaining);
    }

    private void StartCombatPhase()
    {
        CurrentPhase = Phase.Combat;
        SpawnWave(waves[waveIndex]);
        OnCombatPhaseStarted?.Invoke(CurrentWaveNumber);
    }

    private void SpawnWave(WaveData wave)
    {
        // Resolve which spawn points this wave uses (empty list = all).
        List<Transform> activePoints = new List<Transform>();
        if (wave.spawnPointIndices != null && wave.spawnPointIndices.Length > 0)
        {
            foreach (int i in wave.spawnPointIndices)
                if (i >= 0 && i < spawnPoints.Length)
                    activePoints.Add(spawnPoints[i]);
        }
        if (activePoints.Count == 0)
            activePoints.AddRange(spawnPoints);

        foreach (WaveData.SpawnEntry entry in wave.entries)
        {
            if (entry.enemyPrefab == null)
                continue;

            for (int i = 0; i < entry.count; i++)
            {
                Transform point = activePoints[UnityEngine.Random.Range(0, activePoints.Count)];
                Vector2 scatter = UnityEngine.Random.insideUnitCircle * scatterRadius;
                Vector3 position = point.position + new Vector3(scatter.x, 0f, scatter.y);

                GameObject enemy = Instantiate(entry.enemyPrefab, position, Quaternion.identity);

                Health health = enemy.GetComponent<Health>();
                if (health != null)
                {
                    health.OnDeath += HandleEnemyDeath;
                    EnemiesAlive++;
                }
            }
        }

        OnEnemiesAliveChanged?.Invoke(EnemiesAlive);
    }

    private void HandleEnemyDeath()
    {
        EnemiesAlive = Mathf.Max(0, EnemiesAlive - 1);
        OnEnemiesAliveChanged?.Invoke(EnemiesAlive);

        if (CurrentPhase != Phase.Combat || EnemiesAlive > 0)
            return;

        OnWaveCleared?.Invoke(CurrentWaveNumber);

        if (waveIndex + 1 >= waves.Length)
        {
            CurrentPhase = Phase.Victory;
            OnVictory?.Invoke();
            Debug.Log("VICTORY - all waves cleared!");
            // Phase 9: GameManager.Instance.TriggerVictory();
        }
        else
        {
            BeginBuildPhase(waveIndex + 1);
        }
    }
}
