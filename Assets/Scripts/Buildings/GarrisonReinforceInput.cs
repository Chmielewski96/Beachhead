using UnityEngine;

/// <summary>
/// TEMPORARY until the Phase 9 build/selection UI: press U with the mouse
/// over a garrison building to buy a Reinforce upgrade (+1 max garrison,
/// costs shells). Insufficient shells flashes the shell counter via the
/// usual OnSpendFailed event - no extra wiring needed.
/// </summary>
public class GarrisonReinforceInput : MonoBehaviour
{
    [SerializeField] private LayerMask buildingMask;

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.U))
            return;

        if (!MouseWorld.TryGetObjectUnderMouse(buildingMask, out Collider hit))
            return;

        GarrisonBuilding garrison = hit.GetComponentInParent<GarrisonBuilding>();
        if (garrison == null)
            return;

        if (garrison.TryReinforce())
            Debug.Log("Garrison reinforced: cap is now " + garrison.CurrentCap);
        else if (!garrison.CanReinforce)
            Debug.Log("Garrison already at maximum reinforcement.");
    }
}
