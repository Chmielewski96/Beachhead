using UnityEngine;

/// <summary>
/// Pure plumbing between Tower's beam state and a LineRenderer - reads
/// IsBeamActive/BeamStartPoint/BeamEndPoint every frame and nothing else.
/// All the actual VISUAL character (material, color, width, glow, texture
/// scroll) lives on the LineRenderer/material themselves, not here.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class TowerBeamVisual : MonoBehaviour
{
    [SerializeField] private Tower tower;

    private LineRenderer line;

    private void Awake()
    {
        line = GetComponent<LineRenderer>();
        if (tower == null)
            tower = GetComponentInParent<Tower>();
    }

    private void Update()
    {
        if (tower == null)
            return;

        bool active = tower.IsBeamActive;
        if (line.enabled != active)
            line.enabled = active;

        if (!active)
            return;

        line.SetPosition(0, tower.BeamStartPoint);
        line.SetPosition(1, tower.BeamEndPoint);
    }
}
