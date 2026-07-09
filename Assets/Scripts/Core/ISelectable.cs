/// <summary>
/// Anything the SelectionManager can select and deselect - a Unit today,
/// a Building later (Phase 3). The manager doesn't need to know which.
/// </summary>
public interface ISelectable
{
    void OnSelected();
    void OnDeselected();
}
