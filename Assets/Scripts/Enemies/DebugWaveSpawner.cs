using UnityEngine;

/// <summary>
/// TEMPORARY Phase 4 tool: press T to spawn a batch of crabs at a random
/// spawn point. The real data-driven WaveManager (ScriptableObject waves,
/// build phases, escalation) replaces this in Phase 8 - don't extend this
/// script, it exists only to make the combat loop testable right now.
/// </summary>
public class DebugWaveSpawner : MonoBehaviour
{
    [SerializeField] private GameObject crabPrefab;
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private int crabsPerWave = 5;
    [Tooltip("Random scatter around the spawn point so crabs don't stack.")]
    [SerializeField] private float scatterRadius = 3f;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
            SpawnWave();
    }

    private void SpawnWave()
    {
        if (crabPrefab == null || spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning("DebugWaveSpawner: assign the crab prefab and at least one spawn point.");
            return;
        }

        Transform spawnPoint = spawnPoints[Random.Range(0, spawnPoints.Length)];

        for (int i = 0; i < crabsPerWave; i++)
        {
            Vector2 scatter = Random.insideUnitCircle * scatterRadius;
            Vector3 position = spawnPoint.position + new Vector3(scatter.x, 0f, scatter.y);
            Instantiate(crabPrefab, position, Quaternion.identity);
        }
    }
}
