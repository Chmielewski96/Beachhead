using UnityEngine;

/// <summary>
/// Press U while the Keep is SELECTED to recruit a worker. Selection-based
/// rather than hover-based. Deliberately the same key and pattern as the
/// garrison Reinforce - 'U on the selected building = upgrade it' stays one
/// consistent rule. The two input scripts never conflict: each only acts
/// if its own component type is on the selected building.
/// </summary>
public class KeepRecruitInput : MonoBehaviour
{
private void Update()
    {
        if (PauseManager.Instance != null && PauseManager.Instance.IsPaused)
            return;

        if (IntroSequence.Instance != null && IntroSequence.Instance.IsIntroActive)
            return;

        if (!Input.GetKeyDown(KeyCode.U))
            return;

        if (SelectionManager.Instance == null || SelectionManager.Instance.SelectedBuilding == null)
            return;

        BuildingSelectable building = SelectionManager.Instance.SelectedBuilding;
        KeepWorkerRecruiter recruiter = building.GetComponentInParent<KeepWorkerRecruiter>();
        if (recruiter == null)
            recruiter = building.GetComponentInChildren<KeepWorkerRecruiter>();
        if (recruiter != null)
            recruiter.TryRecruit();
    }
}
