using UnityEngine;

/// <summary>
/// Awards resources when this thing dies - Beachhead's shell economy:
/// crabs carry a shell bounty, killing them is the ONLY shell income.
/// Pure event subscriber: Health doesn't know bounties exist, and this
/// script is the third different death responder sharing the same event
/// (Keep = game over, wall = vanish, crab = topple + bounty).
/// </summary>
[RequireComponent(typeof(Health))]
public class ResourceBounty : MonoBehaviour
{
    [SerializeField] private ResourceType resourceType = ResourceType.Shells;
    [SerializeField] private int amount = 5;

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
        if (ResourceManager.Instance != null)
            ResourceManager.Instance.Add(resourceType, amount);
    }
}
