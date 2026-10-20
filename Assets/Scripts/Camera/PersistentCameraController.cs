using UnityEngine;

public class PersistentCameraController : MonoBehaviour
{
    public static PersistentCameraController Instance;

    [Header("Targeting")]
    public Transform target;
    public string playerTag = "Player"; // Legacy fallback: only used until a local player registers

    private Transform _localTarget;

    [Header("Zoom Settings (Scroll Wheel)")]
    public float zoomSpeed = 5f;
    public float minZoom = 2f;
    public float maxZoom = 15f;

    [Header("Rotation Settings (Arrow Keys)")]
    public float rotationSpeed = 100f;

    [Header("Pitch Constraints")]
    public float minPitch = -35f;
    public float maxPitch = 35f;

    private float currentYaw = 0f;
    private float currentPitch = 0f;

    private Camera cam;

    void Awake()
    {
        // Singleton pattern to ensure this rig survives scene loads and prevents duplicates
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        cam = GetComponentInChildren<Camera>();
        if (cam != null)
        {
            cam.orthographic = true;
        }

        currentYaw = transform.eulerAngles.y;
        currentPitch = transform.eulerAngles.x;
    }

    void Update()
    {
        FindTargetIfNeeded();
        HandleZoom();

        if (target != null)
        {
            HandleRotation();
        }
    }

    void LateUpdate()
    {
        // Snap the pivot point to the player's exact position after all movement is calculated
        if (target != null)
        {
            transform.position = target.position;
        }
    }

    void HandleZoom()
    {
        if (cam == null) return;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0.0f)
        {
            // Modify the orthographic size to zoom in and out
            cam.orthographicSize -= scroll * zoomSpeed;
            cam.orthographicSize = Mathf.Clamp(cam.orthographicSize, minZoom, maxZoom);
        }
    }

    void HandleRotation()
    {
        float horizontalInput = 0f;
        float verticalInput = 0f;

        if (Input.GetKey(KeyCode.LeftArrow)) horizontalInput = -1f;
        if (Input.GetKey(KeyCode.RightArrow)) horizontalInput = 1f;
        if (Input.GetKey(KeyCode.UpArrow)) verticalInput = 1f;
        if (Input.GetKey(KeyCode.DownArrow)) verticalInput = -1f;

        if (horizontalInput != 0f || verticalInput != 0f)
        {
            currentYaw += horizontalInput * rotationSpeed * Time.deltaTime;
            currentPitch += verticalInput * rotationSpeed * Time.deltaTime;
            currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);

            transform.rotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
        }
    }

    void FindTargetIfNeeded()
    {
        // The authoritative answer: the local owner's player registers itself here (see
        // LocalPlayerCameraTarget). It wins outright, so a second player in the zone can never
        // steal the camera.
        if (_localTarget != null)
        {
            target = _localTarget;
            return;
        }

        // Fallback for a scene with no local player yet (offline main menu, a spectator). A tag
        // search can return any player, so it is a last resort rather than the primary path.
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag(playerTag);
            if (player != null)
            {
                target = player.transform;
            }
        }
    }

    /// <summary>
    /// Binds the camera to <paramref name="player"/> — the local owner's transform — overriding the
    /// tag fallback. Called by the player prefab's <c>LocalPlayerCameraTarget</c> when this client
    /// owns the player.
    /// </summary>
    public void SetLocalTarget(Transform player)
    {
        _localTarget = player;
        target = player;
    }

    /// <summary>
    /// Clears a local binding, e.g. when this client stops owning a player. The camera then falls
    /// back to whatever the scene offers rather than following a stale transform.
    /// </summary>
    public void ClearLocalTarget()
    {
        // Drop the tracked target too: leaving it set would let the destroyed player's transform
        // linger, and UnbindCamera runs before the object is gone so target would point at a
        // soon-to-die transform.
        if (target == _localTarget)
            target = null;
        _localTarget = null;
    }
}
