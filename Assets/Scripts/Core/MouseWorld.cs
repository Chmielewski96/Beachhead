using UnityEngine;

/// <summary>
/// Static helper for converting mouse screen position into world-space info.
/// Used everywhere a click needs to ask "what's under the cursor?" - ground
/// point for movement orders, or a specific object for selection/targeting.
/// </summary>
public static class MouseWorld
{
    // Generous ray length so it works at any camera zoom/tilt.
    private const float MaxRayDistance = 500f;

    /// <summary>
    /// Raycasts from the mouse into the world against the given layer mask
    /// (typically just the Ground layer) and returns the hit point.
    /// </summary>
    public static bool TryGetGroundPoint(LayerMask groundMask, out Vector3 point)
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, MaxRayDistance, groundMask))
        {
            point = hit.point;
            return true;
        }

        point = Vector3.zero;
        return false;
    }

    /// <summary>
    /// Raycasts from the mouse against the given layer mask (e.g. Unit,
    /// Building, ResourceNode) and returns whatever collider was hit.
    /// </summary>
    public static bool TryGetObjectUnderMouse(LayerMask mask, out Collider hitCollider)
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, MaxRayDistance, mask))
        {
            hitCollider = hit.collider;
            return true;
        }

        hitCollider = null;
        return false;
    }
}
