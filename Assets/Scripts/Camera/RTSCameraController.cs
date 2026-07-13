using UnityEngine;

/// <summary>
/// RTS camera rig controller: keyboard pan, optional screen-edge pan, scroll-wheel
/// zoom, Q/E rotation, and map-bounds clamping.
///
/// Setup: attach this to the "CameraRig" empty GameObject at the scene root.
/// The actual Camera must be a CHILD of this object - all pan/rotate/bounds
/// logic moves the rig itself; only zoom touches the camera's local position.
/// </summary>
public class RTSCameraController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The child Camera transform. Auto-found via Camera.main if left empty.")]
    [SerializeField] private Transform cameraTransform;

    [Header("Pan")]
    [SerializeField] private float panSpeed = 20f;
    [Tooltip("Keep OFF while testing in the Editor - the mouse constantly leaves the Game view. Flip ON for builds.")]
    [SerializeField] private bool edgePanEnabled = false;
    [SerializeField] private float edgePanBorderPixels = 15f;

    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 15f;
    [SerializeField] private float minZoomDistance = 8f;
    [SerializeField] private float maxZoomDistance = 30f;

    [Header("Rotation (Q/E)")]
    [SerializeField] private float rotationSpeed = 90f; // degrees per second

    [Header("Map Bounds")]
    [SerializeField] private Vector2 mapMin = new Vector2(-50f, -50f);
    [SerializeField] private Vector2 mapMax = new Vector2(50f, 50f);

    // Cached so zoom can scale distance along the camera's original viewing
    // angle without changing that angle.
    private Vector3 zoomDirection;
    private float currentZoomDistance;

    private void Awake()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        if (cameraTransform == null)
        {
            Debug.LogError("RTSCameraController: no camera assigned and Camera.main not found.");
            enabled = false;
            return;
        }

        zoomDirection = cameraTransform.localPosition.normalized;
        currentZoomDistance = cameraTransform.localPosition.magnitude;
    }

private void Start()
    {
        // Center on the Keep at game start, wherever it's actually placed -
        // Start (not Awake) so Keep.Awake has already set Instance regardless
        // of script execution order between the two.
        if (Keep.Instance != null)
        {
            Vector3 keepPos = Keep.Instance.transform.position;
            transform.position = new Vector3(keepPos.x, transform.position.y, keepPos.z);
        }
    }


    private void Update()
    {
        HandleRotation();
        HandlePan();
        HandleZoom();
        ClampToBounds();
    }

    private void HandleRotation()
    {
        float rotationInput = 0f;
        if (Input.GetKey(KeyCode.Q)) rotationInput -= 1f;
        if (Input.GetKey(KeyCode.E)) rotationInput += 1f;

        if (rotationInput != 0f)
            transform.Rotate(Vector3.up, rotationInput * rotationSpeed * Time.deltaTime, Space.World);
    }

    private void HandlePan()
    {
        float h = Input.GetAxisRaw("Horizontal"); // A/D, arrows
        float v = Input.GetAxisRaw("Vertical");   // W/S, arrows

        if (edgePanEnabled)
        {
            Vector3 mousePos = Input.mousePosition;

            if (mousePos.x <= edgePanBorderPixels) h = -1f;
            else if (mousePos.x >= Screen.width - edgePanBorderPixels) h = 1f;

            if (mousePos.y <= edgePanBorderPixels) v = -1f;
            else if (mousePos.y >= Screen.height - edgePanBorderPixels) v = 1f;
        }

        // Move relative to the rig's own flattened facing, so Q/E rotation
        // doesn't break "W = up on screen" expectations - same problem you
        // solved for third-person movement in Turtling.
        Vector3 forward = transform.forward; forward.y = 0f; forward.Normalize();
        Vector3 right = transform.right; right.y = 0f; right.Normalize();
        Vector3 moveDir = (forward * v + right * h);

        // Zoomed-out = faster pan, so distance never feels sluggish.
        float zoomFactor = Mathf.InverseLerp(minZoomDistance, maxZoomDistance, currentZoomDistance);
        float speedMultiplier = Mathf.Lerp(0.6f, 1.8f, zoomFactor);

        transform.position += moveDir * panSpeed * speedMultiplier * Time.deltaTime;
    }

    private void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f)
        {
            currentZoomDistance -= scroll * zoomSpeed;
            currentZoomDistance = Mathf.Clamp(currentZoomDistance, minZoomDistance, maxZoomDistance);
            cameraTransform.localPosition = zoomDirection * currentZoomDistance;
        }
    }

    private void ClampToBounds()
    {
        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(pos.x, mapMin.x, mapMax.x);
        pos.z = Mathf.Clamp(pos.z, mapMin.y, mapMax.y);
        transform.position = pos;
    }
}
