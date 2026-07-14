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
    [Tooltip("Unlimited nodes never deplete and never self-destruct - 'remaining' is ignored entirely. Kept as a toggle so a future map can still place finite deposits.")]
    [SerializeField] private bool unlimitedStock = true;
    [SerializeField] private int remaining = 200;

    public ResourceType ResourceType => resourceType;
    public bool IsEmpty => !unlimitedStock && remaining <= 0;

    // One worker per node: whoever claims it owns it for their whole
    // gather loop (including the walk to the Keep and back). A destroyed
    // worker's claim evaporates automatically - Unity's fake-null makes
    // the destroyed reference compare equal to null.
    private WorkerAI claimedBy;

    /// <summary>True if a DIFFERENT living worker already owns this node.</summary>
    public bool IsClaimedByOther(WorkerAI worker)
    {
        return claimedBy != null && claimedBy != worker;
    }

    public void Claim(WorkerAI worker)
    {
        claimedBy = worker;
    }

    /// <summary>Only the current owner can release - a late Release from a
    /// worker that already switched nodes must not evict the new owner.</summary>
    public void Release(WorkerAI worker)
    {
        if (claimedBy == worker)
            claimedBy = null;
    }

public int Harvest(int amount)
    {
        if (unlimitedStock)
            return amount; // bottomless - always yields the full ask

        int taken = Mathf.Min(amount, remaining);
        remaining -= taken;

        if (IsEmpty)
            Destroy(gameObject);

        return taken;
    }
}
