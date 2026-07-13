using UnityEngine;

/// <summary>
/// TEMPORARY until the Phase 9 UI: press U with the mouse over the Keep to
/// recruit a worker. Deliberately the same key and pattern as the garrison
/// Reinforce - 'U over a building = upgrade it' stays one consistent rule.
/// The two input scripts never conflict: each only acts if its own
/// component type is under the cursor.
/// </summary>
public class KeepRecruitInput : MonoBehaviour
{
    [SerializeField] private LayerMask buildingMask;

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.U))
            return;

        if (!MouseWorld.TryGetObjectUnderMouse(buildingMask, out Collider hit))
            return;

        KeepWorkerRecruiter recruiter = hit.GetComponentInParent<KeepWorkerRecruiter>();
        if (recruiter != null)
            recruiter.TryRecruit();
    }
}
