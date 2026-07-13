using UnityEngine;

/// <summary>
/// One wave, as data. Ten of these assets define the whole campaign - and
/// the acid test of the data-driven design: adding a new enemy type or
/// rebalancing the difficulty curve should touch ZERO code, only these
/// assets and prefabs.
/// </summary>
[CreateAssetMenu(menuName = "Beachhead/Wave Data")]
public class WaveData : ScriptableObject
{
    [System.Serializable]
    public class SpawnEntry
    {
        public GameObject enemyPrefab;
        public int count = 5;
    }

    [Tooltip("What spawns in this wave.")]
    public SpawnEntry[] entries;

    [Tooltip("Build-phase countdown BEFORE this wave arrives.")]
    public float buildPhaseDuration = 30f;

    [Tooltip("Indices into WaveManager's spawn point array that this wave uses. Leave EMPTY to use all of them.")]
    public int[] spawnPointIndices;
}
