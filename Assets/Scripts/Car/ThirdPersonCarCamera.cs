using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// GTA-style third-person vehicle camera.
/// - Mouse (or right stick) orbits freely around the car.
/// - When you stop moving the mouse and the car is moving forward, the camera swings back behind it.
/// - Always keeps the world "up" (no rolling with the car), so flips and slopes feel natural.
/// - Pulls back with speed, widens FOV with speed and nitro, and never goes through walls.
/// Put it on the Main Camera and drag the player's vehicle into Target.
/// </summary>
[RequireComponent(typeof(Camera))]
public class ThirdPersonCarCamera : MonoBehaviour
{
    public VehicleController target;

    [Header("Framing")]
    [Tooltip("Height of the point the camera orbits, above the car's position.")]
    public float height = 1.6f;
    public float distance = 6.5f;
    [Tooltip("Extra distance at top speed.")]
    public float speedDistanceBonus = 1.8f;
    [Tooltip("Over-the-shoulder offset. 0 = centred behind the car.")]
    public float shoulderOffset = 0f;

    [Header("Orbit")]
    [Tooltip("Degrees per mouse pixel.")]
    public float mouseSensitivity = 0.15f;
    [Tooltip("Degrees per second at full stick.")]
    public float stickSensitivity = 140f;
    public bool invertY = false;
    public float defaultPitch = 12f;
    public float minPitch = -8f;
    public float maxPitch = 60f;
    public bool lockCursor = true;

    [Header("Auto recenter (swing behind the car)")]
    [Tooltip("Seconds without camera input before it starts returning behind the car.")]
    public float recenterDelay = 1.5f;
    public float recenterSpeed = 2.5f;
    [Tooltip("Only recenter when the car moves forward faster than this (m/s).")]
    public float recenterMinSpeed = 3f;

    [Header("Smoothing")]
    [Tooltip("Vertical smoothing of the orbit point (removes suspension bounce from the camera).")]
    public float verticalSmoothTime = 0.12f;

    [Header("Collision")]
    public LayerMask collisionMask = ~0;
    public float collisionRadius = 0.3f;
    public float minDistance = 1.2f;
    [Tooltip("How fast the camera returns to full distance after a wall is gone.")]
    public float collisionRecover = 4f;

    [Header("FOV")]
    public float baseFov = 60f;
    public float speedFovBonus = 12f;
    public float nitroFovBonus = 12f;
    public float fovSmooth = 4f;

    Camera cam;
    float yaw;
    float pitch;
    float lastInputTime;
    float currentDistance;
    Vector3 pivot;
    float pivotYVelocity;
    bool initialised;
    readonly RaycastHit[] hitBuffer = new RaycastHit[8];

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    void OnEnable()
    {
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void Initialise()
    {
        yaw = target.transform.eulerAngles.y;
        pitch = defaultPitch;
        pivot = target.transform.position + Vector3.up * height;
        currentDistance = distance;
        lastInputTime = Time.time;
        initialised = true;
    }

    void LateUpdate()
    {
        if (target == null) return;
        if (!initialised) Initialise();

        float dt = Time.deltaTime;

        // ---------- orbit input ----------
        Vector2 look = ReadLook(dt);
        if (look.sqrMagnitude > 0.0001f) lastInputTime = Time.time;

        yaw += look.x;
        pitch += invertY ? look.y : -look.y;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        // ---------- auto recenter ----------
        if (Time.time - lastInputTime > recenterDelay && target.ForwardSpeed > recenterMinSpeed)
        {
            float t = 1f - Mathf.Exp(-recenterSpeed * dt);
            yaw = Mathf.LerpAngle(yaw, target.transform.eulerAngles.y, t);
            pitch = Mathf.Lerp(pitch, defaultPitch, t);
        }

        // ---------- orbit point (XZ locked to the car, Y smoothed) ----------
        Vector3 wanted = target.transform.position + Vector3.up * height;
        pivot.x = wanted.x;
        pivot.z = wanted.z;
        pivot.y = Mathf.SmoothDamp(pivot.y, wanted.y, ref pivotYVelocity, verticalSmoothTime);

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 back = rot * Vector3.back;
        Vector3 origin = pivot + rot * Vector3.right * shoulderOffset;

        float wantedDistance = distance + speedDistanceBonus * target.NormalizedSpeed;

        // ---------- collision ----------
        float allowed = wantedDistance;
        int n = Physics.SphereCastNonAlloc(origin, collisionRadius, back, hitBuffer, wantedDistance,
            collisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = hitBuffer[i];
            if (h.distance <= 0f) continue; // started inside something
            if (h.collider.attachedRigidbody == target.Body) continue; // ignore the car itself
            if (h.distance < allowed) allowed = h.distance;
        }
        allowed = Mathf.Max(minDistance, allowed);

        if (allowed < currentDistance)
            currentDistance = allowed; // snap in so we never see through the wall
        else
            currentDistance = Mathf.Lerp(currentDistance, allowed, 1f - Mathf.Exp(-collisionRecover * dt));

        transform.SetPositionAndRotation(origin + back * currentDistance, rot);

        // ---------- FOV ----------
        float targetFov = baseFov
                          + speedFovBonus * target.NormalizedSpeed
                          + (target.NitroActive ? nitroFovBonus : 0f);
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 1f - Mathf.Exp(-fovSmooth * dt));
    }

    // x = yaw delta (degrees), y = pitch delta (degrees, positive = look up)
    Vector2 ReadLook(float dt)
    {
        if (lockCursor && Cursor.lockState != CursorLockMode.Locked) return Vector2.zero;

#if ENABLE_INPUT_SYSTEM
        Vector2 result = Vector2.zero;
        if (Mouse.current != null)
            result += Mouse.current.delta.ReadValue() * mouseSensitivity;
        if (Gamepad.current != null)
            result += Gamepad.current.rightStick.ReadValue() * stickSensitivity * dt;
        return result;
#else
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * (mouseSensitivity * 10f);
#endif
    }

    /// <summary>Call this to point the camera behind the car instantly (e.g. after respawn).</summary>
    public void SnapBehindTarget()
    {
        if (target == null) return;
        yaw = target.transform.eulerAngles.y;
        pitch = defaultPitch;
        lastInputTime = Time.time;
    }
}