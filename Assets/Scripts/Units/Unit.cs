using UnityEngine;

/// <summary>
/// Marks a Unit as selectable and owns its selection-ring visual.
/// SelectionManager just calls OnSelected/OnDeselected - it doesn't know or
/// care that a Unit happens to be a capsule with a ring underneath.
/// </summary>
public class Unit : MonoBehaviour, ISelectable
{
    [Tooltip("Child object shown while selected - a flattened cylinder/quad at the unit's feet, with its Collider removed.")]
    [SerializeField] private GameObject selectionRing;

    private void Awake()
    {
        if (selectionRing != null)
            selectionRing.SetActive(false);
    }

private void OnDestroy()
    {
        if (SelectionManager.Instance != null)
            SelectionManager.Instance.RemoveFromSelection(this);
    }


    public void OnSelected()
    {
        if (selectionRing != null)
            selectionRing.SetActive(true);
    }

    public void OnDeselected()
    {
        if (selectionRing != null)
            selectionRing.SetActive(false);
    }
}
