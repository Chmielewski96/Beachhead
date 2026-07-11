using UnityEngine;

/// <summary>
/// A finite, typed resource deposit (Beachhead: sand piles). Workers call
/// Harvest to take from it; it returns how much was actually taken (which
/// may be less than asked when nearly empty) and destroys itself when
/// depleted - workers detect the vanished node via their null-check.
/// </summary>
public class ResourceDeposit : MonoBehaviour
{
    [SerializeField] private ResourceType resourceType = ResourceType.Sand;
    [SerializeField] private int remaining = 200;

    public ResourceType ResourceType => resourceType;
    public bool IsEmpty => remaining <= 0;

    public int Harvest(int amount)
    {
        int taken = Mathf.Min(amount, remaining);
        remaining -= taken;

        if (IsEmpty)
            Destroy(gameObject);

        return taken;
    }
}
