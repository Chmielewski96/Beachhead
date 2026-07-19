using System;
using System.Collections;
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
    [Header("Skip Build Phase Reward - scales with how early you skip, not a flat amount")]
    [Tooltip("Flat MAX reward if skipped within this many seconds of the build phase starting.")]
    [SerializeField] private float skipBonusEarlyWindow = 3f;
    [SerializeField] private int skipBonusMaxShells = 20;
    [Tooltip("Flat MIN reward if skipped within this many seconds of the build phase ending anyway.")]
    [SerializeField] private float skipBonusLateWindow = 1f;
    [SerializeField] private int skipBonusMinShells = 1;

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

    // Guards the wave-clear check while a staggered spawn is still running:
    // without it, towers killing the first trickle of enemies before the
    // rest have spawned would drop EnemiesAlive to 0 and prematurely end
    // the wave (starting the next build phase WHILE enemies keep spawning).
    private bool isSpawning;
    private Coroutine spawnRoutine;

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

        if (ResourceManager.Instance != null)
        {
            int bonus = CalculateSkipBonus();
            if (bonus > 0)
                ResourceManager.Instance.Add(ResourceType.Shells, bonus);
        }

        StartCombatPhase();
    }

    /// <summary>
    /// Flat skipBonusMaxShells for the first skipBonusEarlyWindow seconds
    /// of the build phase, flat skipBonusMinShells for the last
    /// skipBonusLateWindow seconds, and a straight linear ramp between the
    /// two everywhere in between - so skipping the instant the button
    /// appears is worth meaningfully more than skipping with one second
    /// left anyway, instead of both paying the same flat reward.
    /// </summary>
    private int CalculateSkipBonus()
    {
        float totalDuration = waves[waveIndex].buildPhaseDuration;
        float elapsed = totalDuration - BuildTimeRemaining;

        // Degenerate case: a wave too short for both windows to fit without
        // overlapping - fall back to a single straight ramp across the
        // whole duration rather than let InverseLerp see a shrunk/negative
        // range.
        if (totalDuration <= skipBonusEarlyWindow + skipBonusLateWindow)
        {
            float wholeRampT = totalDuration > 0f ? Mathf.Clamp01(elapsed / totalDuration) : 0f;
            return Mathf.RoundToInt(Mathf.Lerp(skipBonusMaxShells, skipBonusMinShells, wholeRampT));
        }

        if (elapsed <= skipBonusEarlyWindow)
            return skipBonusMaxShells;

        if (BuildTimeRemaining <= skipBonusLateWindow)
            return skipBonusMinShells;

        float t = Mathf.InverseLerp(skipBonusEarlyWindow, totalDuration - skipBonusLateWindow, elapsed);
        return Mathf.RoundToInt(Mathf.Lerp(skipBonusMaxShells, skipBonusMinShells, t));
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

        if (spawnRoutine != null)
            StopCoroutine(spawnRoutine);
        spawnRoutine = StartCoroutine(SpawnWaveRoutine(waves[waveIndex]));

        OnCombatPhaseStarted?.Invoke(CurrentWaveNumber);
    }

/// <summary>
    /// Staggered spawning: one enemy every spawnInterval seconds, in a
    /// SHUFFLED order (entries interleave - crabs, brutes, and jellyfish
    /// arrive mixed together rather than sorted by type). Introduced when
    /// inter-unit avoidance was turned off: with overlap allowed, an
    /// everything-at-once spawn stacks into a single simultaneous blob
    /// that hits the defenses as one spike.
    /// </summary>
    private IEnumerator SpawnWaveRoutine(WaveData wave)
    {
        isSpawning = true;

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

        // Flatten all entries into one list, then Fisher-Yates shuffle so
        // the composition stays mixed for the whole spawn window.
        List<GameObject> spawnQueue = new List<GameObject>();
        foreach (WaveData.SpawnEntry entry in wave.entries)
        {
            if (entry.enemyPrefab == null)
                continue;
            for (int i = 0; i < entry.count; i++)
                spawnQueue.Add(entry.enemyPrefab);
        }
        for (int i = spawnQueue.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            GameObject tmp = spawnQueue[i];
            spawnQueue[i] = spawnQueue[j];
            spawnQueue[j] = tmp;
        }

        foreach (GameObject prefab in spawnQueue)
        {
            Transform point = activePoints[UnityEngine.Random.Range(0, activePoints.Count)];
            Vector2 scatter = UnityEngine.Random.insideUnitCircle * scatterRadius;
            Vector3 position = point.position + new Vector3(scatter.x, 0f, scatter.y);

            GameObject enemy = Instantiate(prefab, position, Quaternion.identity);

            Health health = enemy.GetComponent<Health>();
            if (health != null)
            {
                health.OnDeath += HandleEnemyDeath;
                EnemiesAlive++;
                OnEnemiesAliveChanged?.Invoke(EnemiesAlive);
            }

            if (wave.spawnInterval > 0f)
                yield return new WaitForSeconds(wave.spawnInterval);
        }

        isSpawning = false;
        spawnRoutine = null;

        // Edge case: the player killed every spawned enemy BEFORE the
        // spawn window finished - the death handler was suppressed by
        // isSpawning, so the clear check has to happen here instead.
        CheckWaveCleared();
    }

private void HandleEnemyDeath()
    {
        EnemiesAlive = Mathf.Max(0, EnemiesAlive - 1);
        OnEnemiesAliveChanged?.Invoke(EnemiesAlive);

        CheckWaveCleared();
    }

private void CheckWaveCleared()
    {
        // isSpawning: the wave isn't over while stragglers are still
        // arriving, no matter what the alive count momentarily says.
        if (CurrentPhase != Phase.Combat || isSpawning || EnemiesAlive > 0)
            return;

        OnWaveCleared?.Invoke(CurrentWaveNumber);

        if (waveIndex + 1 >= waves.Length)
        {
            CurrentPhase = Phase.Victory;
            OnVictory?.Invoke();
            Debug.Log("VICTORY - all waves cleared!");
        }
        else
        {
            BeginBuildPhase(waveIndex + 1);
        }
    }

}
