using UnityEngine;

/// <summary>
/// Makes a building clickable in the selection system - the ISelectable
/// interface finally earning its keep beyond units. Single-click only
/// (drag-box remains units-only), toggles a ring marker child.
/// </summary>
public class BuildingSelectable : MonoBehaviour, ISelectable
{
    [Tooltip("Ring marker child shown while selected (LineRenderer + RingRenderer), disabled by default.")]
    [SerializeField] private GameObject selectionRing;

    private void Awake()
    {
        if (selectionRing != null)
            selectionRing.SetActive(false);
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
