using UnityEngine;

public class PersistentCameraController : MonoBehaviour
{
    public static PersistentCameraController Instance;

    [Header("Targeting")]
    public Transform target;
    public string playerTag = "Player"; // Used to auto-find the player in new scenes

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
        // If the scene changes or target is destroyed, automatically find the new player
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag(playerTag);
            if (player != null)
            {
                target = player.transform;
            }
        }
    }
}
