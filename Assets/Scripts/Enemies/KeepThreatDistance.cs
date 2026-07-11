using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Caches this enemy's REAL distance to the Keep - the NavMesh path length,
/// not the straight line. Through a maze those differ enormously, and
/// towers using straight-line distance shoot the geometrically-close crab
/// that still has the whole labyrinth to walk. Recalculated on a slow,
/// per-instance-staggered timer because path queries aren't free; towers
/// read the cached value as their danger score.
/// </summary>
public class KeepThreatDistance : MonoBehaviour
{
    [SerializeField] private float updateInterval = 0.5f;

    public float PathDistanceToKeep { get; private set; } = float.MaxValue;

    private NavMeshPath path;
    private float timer;

    private void OnEnable()
    {
        // Stagger the first update so a whole wave spawned on one frame
        // doesn't run all its path queries on the same frame forever after.
        timer = Random.Range(0f, updateInterval);
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f)
            return;

        timer = updateInterval;
        Recalculate();
    }

    private void Recalculate()
    {
        if (Keep.Instance == null)
        {
            PathDistanceToKeep = float.MaxValue;
            return;
        }

        // Same endpoint-snapping as CrabAI's blocked check, for the same
        // reasons: wall-huggers stand on carve-eroded edges, and the Keep's
        // center is never on the mesh at all.
        Vector3 start = transform.position;
        if (NavMesh.SamplePosition(start, out NavMeshHit startHit, 2f, NavMesh.AllAreas))
            start = startHit.position;

        Vector3 keepTarget = Keep.Instance.transform.position;
        if (NavMesh.SamplePosition(keepTarget, out NavMeshHit keepHit, 15f, NavMesh.AllAreas))
            keepTarget = keepHit.position;

        if (path == null)
            path = new NavMeshPath();

        if (!NavMesh.CalculatePath(start, keepTarget, NavMesh.AllAreas, path) || path.corners.Length < 2)
        {
            // No path at all (off-mesh hiccup): fall back to straight-line so
            // this enemy still ranks somewhere sensible instead of 'infinitely safe'.
            PathDistanceToKeep = Vector3.Distance(transform.position, keepTarget);
            return;
        }

        float length = 0f;
        for (int i = 1; i < path.corners.Length; i++)
            length += Vector3.Distance(path.corners[i - 1], path.corners[i]);

        // A partial path (walled off) measures distance to the blockade,
        // which still ranks 'nearer the breach point' as more dangerous -
        // exactly what a defending tower should care about.
        PathDistanceToKeep = length;
    }
}
