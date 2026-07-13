using UnityEngine;

/// <summary>
/// Draws a thin flat ring using a LineRenderer circle - the placeholder-art
/// answer to "I want a ring, not a disc" (Unity has no torus primitive).
/// Used for building selection markers and the node-order marker. Rebuilds
/// in OnValidate so radius/width edits preview live in the editor.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
[ExecuteAlways]
public class RingRenderer : MonoBehaviour
{
    [SerializeField] private float radius = 2f;
    [SerializeField] private float lineWidth = 0.12f;
    [SerializeField] private Color color = Color.white;
    [SerializeField] private int segments = 48;

    private void Awake()
    {
        Rebuild();
    }

    private void OnValidate()
    {
        Rebuild();
    }

    public void SetRadius(float newRadius)
    {
        radius = newRadius;
        Rebuild();
    }

    private void Rebuild()
    {
        LineRenderer line = GetComponent<LineRenderer>();
        if (line == null)
            return;

        line.loop = true;
        line.useWorldSpace = false; // so parent scaling (ClickMarkerVFX) works
        line.widthMultiplier = lineWidth;

        // Without corner/cap smoothing, a THICK LineRenderer loop renders as
        // a faceted polygon with a visible seam at the closing joint - only
        // became obvious once width went from 0.12 to 1.5 for the spawn
        // rings. Rounds every segment joint, including the loop closure.
        line.numCornerVertices = 8;
        line.numCapVertices = 8;
        line.startColor = color;
        line.endColor = color;
        line.positionCount = segments;

        for (int i = 0; i < segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
    }
}
