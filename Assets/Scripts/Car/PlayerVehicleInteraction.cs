using UnityEngine;

/// <summary>
/// Put this on the PLAYER (next to FPSController). Press F near a car to get in, F again to get out.
///
/// Getting in:  the camera leaves the player (keeps its world pose), the on-foot scripts and the
///              player's visuals are turned off, ThirdPersonCarCamera + the car's PlayerCarInput turn on.
/// Getting out: the camera returns to the player with its original local pose, and the player is
///              placed beside the car.
///
/// Setup:
///  - Add ThirdPersonCarCamera (and SpeedMotionBlur, optional) to the Main Camera and leave them DISABLED.
///  - Leave PlayerCarInput DISABLED on every car (this script enables it for the car you enter).
/// </summary>
public class PlayerVehicleInteraction : MonoBehaviour
{
    [Header("References")]
    public FPSController fps;
    public CharacterController characterController;
    public Camera playerCamera;
    public ThirdPersonCarCamera carCamera;
    [Tooltip("Optional.")]
    public SpeedMotionBlur motionBlur;

    [Header("Interaction")]
    public KeyCode interactKey = KeyCode.F;

    [Header("Engine")]
    [Tooltip("Turns the engine on/off while driving.")]
    public KeyCode engineKey = KeyCode.E;
    [Tooltip("Start the engine (ignition sound) automatically when you get in. Untick to start it yourself with the engine key.")]
    public bool startEngineOnEnter = false;
    [Tooltip("Turn the engine off when you get out of the car.")]
    public bool stopEngineOnExit = true;
    public float enterRange = 3.5f;
    [Tooltip("Layers that contain the cars (their colliders). Everything by default.")]
    public LayerMask vehicleMask = ~0;
    [Tooltip("Exit distance to the left of the car (m).")]
    public float exitSideDistance = 2.2f;

    public VehicleController CurrentVehicle { get; private set; }

    // saved camera state, to restore on exit
    Transform camParent;
    Vector3 camLocalPos;
    Quaternion camLocalRot;
    float camFov;

    Renderer[] renderers;
    CarInput drivingInput;

    void Awake()
    {
        if (fps == null) fps = GetComponent<FPSController>();
        if (characterController == null) characterController = GetComponent<CharacterController>();
        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>(true);
        if (carCamera == null && playerCamera != null) carCamera = playerCamera.GetComponent<ThirdPersonCarCamera>();
        if (motionBlur == null && playerCamera != null) motionBlur = playerCamera.GetComponent<SpeedMotionBlur>();

        renderers = GetComponentsInChildren<Renderer>(true);

        if (fps == null) Debug.LogError("PlayerVehicleInteraction: FPSController not found on the player.", this);
        if (characterController == null) Debug.LogError("PlayerVehicleInteraction: CharacterController not found.", this);
        if (playerCamera == null) Debug.LogError("PlayerVehicleInteraction: no Camera found under the player.", this);
        if (carCamera == null) Debug.LogError("PlayerVehicleInteraction: add ThirdPersonCarCamera to the player's camera (leave it disabled).", this);
    }

    void Update()
    {
        if (Input.GetKeyDown(interactKey))
        {
            if (CurrentVehicle == null) TryEnter();
            else Exit();
            return;
        }

        if (CurrentVehicle != null) DriveInput();
    }

    [Header("Debug")]
    public bool showDebug = true;

    void OnGUI()
    {
        if (!showDebug) return;

        string s;
        if (CurrentVehicle == null)
        {
            s = "ON FOOT\nPress " + interactKey + " within " + enterRange + " m of a car.";
        }
        else
        {
            CarInput ci = drivingInput;
            s = "DRIVING: " + CurrentVehicle.name +
                "\nEngine: " + CurrentVehicle.EngineState +
                "\nThrottle: " + (ci ? ci.throttle.ToString("0.0") : "no CarInput") +
                "   Steer: " + (ci ? ci.steer.ToString("0.0") : "-") +
                "\nSpeed: " + CurrentVehicle.ForwardSpeed.ToString("0.0") + " m/s" +
                "\nGrounded: " + CurrentVehicle.IsGrounded;
        }

        GUI.Box(new Rect(10, 10, 300, 110), GUIContent.none);
        GUI.Label(new Rect(18, 14, 290, 104), s);
    }

    // Writes the keyboard straight into the car's CarInput (old Input class, same as FPSController).
    // W/S = throttle/reverse, A/D = steer, Shift = nitro, Space = handbrake, E = engine, R = flip upright.
    void DriveInput()
    {
        if (drivingInput == null) return;

        drivingInput.throttle = Input.GetAxisRaw("Vertical");
        drivingInput.steer = Input.GetAxisRaw("Horizontal");
        drivingInput.nitro = Input.GetKey(KeyCode.LeftShift);
        drivingInput.handbrake = Input.GetKey(KeyCode.Space);
        if (Input.GetKeyDown(engineKey)) drivingInput.toggleEngine = true;
        if (Input.GetKeyDown(KeyCode.R)) CurrentVehicle.ResetUpright();
    }

    void TryEnter()
    {
        VehicleController best = null;
        float bestDist = float.MaxValue;

        Collider[] cols = Physics.OverlapSphere(transform.position, enterRange, vehicleMask,
            QueryTriggerInteraction.Ignore);
        foreach (Collider c in cols)
        {
            VehicleController v = c.GetComponentInParent<VehicleController>();
            if (v == null) continue;

            float d = (v.transform.position - transform.position).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = v; }
        }

        if (best == null)
        {
            Debug.LogWarning("PlayerVehicleInteraction: no VehicleController within " + enterRange +
                             " m. Found " + cols.Length + " colliders. Check: car collider layer is in Vehicle Mask, " +
                             "colliders are not Triggers, VehicleController is on the car root.", this);
            return;
        }

        if (fps == null || characterController == null || playerCamera == null || carCamera == null)
        {
            Debug.LogError("PlayerVehicleInteraction: missing references, can't enter (see errors at Start).", this);
            return;
        }

        Enter(best);
    }

    void Enter(VehicleController vehicle)
    {
        CurrentVehicle = vehicle;

        // remember where the camera lives on the player
        camParent = playerCamera.transform.parent;
        camLocalPos = playerCamera.transform.localPosition;
        camLocalRot = playerCamera.transform.localRotation;
        camFov = playerCamera.fieldOfView;

        // camera leaves the player (world pose is kept)
        playerCamera.transform.SetParent(null, true);

        // turn the player "off" without turning the camera off
        fps.enabled = false;
        characterController.enabled = false;
        foreach (Renderer r in renderers) r.enabled = false;
        transform.SetParent(vehicle.transform, true); // rides along inside the car

        // turn the car "on"
        carCamera.target = vehicle;
        carCamera.enabled = true;
        carCamera.SnapBehindTarget();
        if (motionBlur != null) motionBlur.enabled = true;

        // this script feeds the car's CarInput; PlayerCarInput (if present) is not needed
        var oldInput = vehicle.GetComponent<PlayerCarInput>();
        if (oldInput != null) oldInput.enabled = false;
        drivingInput = vehicle.GetComponent<CarInput>();

        if (startEngineOnEnter) vehicle.StartEngine();
    }

    void Exit()
    {
        VehicleController vehicle = CurrentVehicle;
        CurrentVehicle = null;

        // stop controlling the car
        drivingInput = null;
        vehicle.GetComponent<CarInput>().Clear();
        if (stopEngineOnExit) vehicle.StopEngine();

        carCamera.enabled = false;
        if (motionBlur != null) motionBlur.enabled = false;

        // put the player beside the car (controller must be OFF while teleporting)
        transform.SetParent(null, true);
        Vector3 exitPos = vehicle.transform.position - vehicle.transform.right * exitSideDistance + Vector3.up * 0.5f;
        float yaw = vehicle.transform.eulerAngles.y;
        transform.SetPositionAndRotation(exitPos, Quaternion.Euler(0f, yaw, 0f));

        // bring the camera back exactly as it was
        playerCamera.transform.SetParent(camParent, false);
        playerCamera.transform.localPosition = camLocalPos;
        playerCamera.transform.localRotation = camLocalRot;
        playerCamera.fieldOfView = camFov;

        foreach (Renderer r in renderers) r.enabled = true;
        characterController.enabled = true;
        fps.enabled = true;
    }
}