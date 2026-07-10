using UnityEngine;

/// <summary>
/// The simplest possible death responder: object disappears when its
/// Health dies. Used by walls (destroying the GameObject also removes the
/// NavMeshObstacle, so the carved hole in the NavMesh heals and enemies
/// can path through the gap - no extra code needed).
/// </summary>
[RequireComponent(typeof(Health))]
public class DestroyOnDeath : MonoBehaviour
{
    [SerializeField] private float delay = 0f;

    private Health health;

    private void Awake()
    {
        health = GetComponent<Health>();
        health.OnDeath += HandleDeath;
    }

    private void OnDestroy()
    {
        if (health != null)
            health.OnDeath -= HandleDeath;
    }

    private void HandleDeath()
    {
        Destroy(gameObject, delay);
    }
}
