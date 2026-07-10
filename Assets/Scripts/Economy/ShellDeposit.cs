using UnityEngine;

/// <summary>
/// A finite shell deposit. Workers call Harvest to take shells; it returns
/// how many were actually taken (which may be less than asked when nearly
/// empty), and the node destroys itself when depleted - workers detect the
/// vanished node via their null-check and go idle.
/// </summary>
public class ShellDeposit : MonoBehaviour
{
    [SerializeField] private int shellsRemaining = 200;

    public bool IsEmpty => shellsRemaining <= 0;

    public int Harvest(int amount)
    {
        int taken = Mathf.Min(amount, shellsRemaining);
        shellsRemaining -= taken;

        if (IsEmpty)
            Destroy(gameObject);

        return taken;
    }
}
