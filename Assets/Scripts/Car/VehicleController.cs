using System;
using System.Collections.Generic;
using UnityEngine;

public enum EngineState { Off, Starting, Running }

[Serializable]
public class VehicleWheel
{
    public WheelCollider collider;
    [Tooltip("Visual mesh. Its pivot must be at the CENTRE of the wheel (or put the mesh inside a centred empty object and drag the empty).")]
    public Transform mesh;
    [Tooltip("Front wheels: turns with the steering wheel.")]
    public bool steer;
    [Tooltip("Receives motor torque.")]
    public bool drive;
    [Tooltip("Gets handbrake torque and loses sideways grip (usually the rear wheels).")]
    public bool handbrake;
    [Tooltip("Fix for imported meshes with a different orientation. Try (0,0,90) or (0,180,0) if the wheel looks sideways/mirrored.")]
    public Vector3 meshRotationOffset;

    [NonSerialized] public float gripFactor = 1f;
}

/// <summary>
/// Realistic-ish vehicle using WheelColliders. Drives anywhere (no track / surface logic).
/// - Suspension (spring, damper) is computed from the vehicle mass and Hz / damping ratio in CarData.
/// - Anti-roll bar, downforce, air drag.
/// - Engine states: Off -> Starting (ignition) -> Running.
/// - Nitro with bar, speed and torque multipliers.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CarInput))]
public class VehicleController : MonoBehaviour
{
    [Header("Data")]
    public CarData data;
    [Tooltip("Max speed multiplier (useful for AI difficulty).")]
    public float speedScale = 1f;

    [Header("Wheels (order doesn't matter)")]
    public VehicleWheel[] wheels;

    [Header("Steering")]
    [Tooltip("How fast the steering wheel goes to full lock (and back). Lower = smoother.")]
    public float steerResponse = 5f;

    [Header("Engine")]
    public bool startEngineOnStart = true;
    [Tooltip("Seconds the ignition takes. EngineSound can set this automatically from the ignition clip length.")]
    public float ignitionTime = 1.5f;

    [Header("Nitro")]
    public float startingNitro = 0f;
    public float minNitroToStart = 5f;

    public event Action IgnitionStarted;
    public event Action EngineStarted;
    public event Action EngineStopped;
    public event Action NitroStarted;
    public event Action NitroStopped;

    public EngineState EngineState { get; private set; } = EngineState.Off;
    public bool EngineRunning => EngineState == EngineState.Running;
    public bool IsGrounded { get; private set; }
    public bool NitroActive { get; private set; }
    public float NitroAmount { get; private set; }
    public float NitroNormalized => data ? NitroAmount / data.maxNitro : 0f;
    public float ForwardSpeed { get; private set; }
    public float NormalizedSpeed => data ? Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / (data.maxSpeed * speedScale)) : 0f;
    public Rigidbody Body => rb;

    Rigidbody rb;
    CarInput input;
    float smoothedSteer;
    float startTimer;
    int driveCount;
    readonly List<Vector2Int> antiRollPairs = new List<Vector2Int>();

    Vector3 Vel
    {
        get
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }
    }

    // ------------------------------------------------------------------ setup

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        input = GetComponent<CarInput>();

        if (data == null)
        {
            Debug.LogError("VehicleController: assign a CarData.", this);
            enabled = false;
            return;
        }

        rb.mass = data.mass;
        rb.centerOfMass = data.centerOfMassOffset;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        ApplySuspension();
        BuildAntiRollPairs();

        NitroAmount = Mathf.Clamp(startingNitro, 0f, data.maxNitro);
    }

    void Start()
    {
        if (startEngineOnStart) StartEngine();
    }

    /// <summary>
    /// Spring and damper from physics: k = m * (2*pi*f)^2, c = 2 * zeta * sqrt(k * m),
    /// where m is the mass each wheel carries. Right-click the component > Apply Suspension to retune in play mode.
    /// </summary>
    [ContextMenu("Apply Suspension")]
    public void ApplySuspension()
    {
        if (data == null || wheels == null) return;

        int count = 0;
        foreach (var w in wheels) if (w != null && w.collider != null) count++;
        if (count == 0) return;

        float sprungMass = data.mass / count;
        float omega = 2f * Mathf.PI * data.suspensionFrequency;
        float spring = sprungMass * omega * omega;
        float damper = 2f * data.dampingRatio * Mathf.Sqrt(spring * sprungMass);

        driveCount = 0;
        foreach (var w in wheels)
        {
            if (w == null || w.collider == null) continue;
            WheelCollider wc = w.collider;

            wc.mass = data.wheelMass;
            wc.suspensionDistance = data.suspensionDistance;
            wc.forceAppPointDistance = data.forceAppPointDistance;
            wc.wheelDampingRate = data.wheelDampingRate;

            wc.suspensionSpring = new JointSpring
            {
                spring = spring,
                damper = damper,
                targetPosition = data.suspensionTarget
            };

            WheelFrictionCurve f = wc.forwardFriction;
            f.stiffness = data.forwardStiffness;
            wc.forwardFriction = f;

            WheelFrictionCurve s = wc.sidewaysFriction;
            s.stiffness = data.sidewaysStiffness;
            wc.sidewaysFriction = s;

            w.gripFactor = 1f;
            if (w.drive) driveCount++;
        }

        if (driveCount == 0)
            Debug.LogWarning("VehicleController: no wheel is marked as Drive.", this);
    }

    // Pairs wheels on the same axle (same Z, opposite sides) for the anti-roll bar.
    void BuildAntiRollPairs()
    {
        antiRollPairs.Clear();
        if (wheels == null) return;
        bool[] used = new bool[wheels.Length];

        for (int i = 0; i < wheels.Length; i++)
        {
            if (used[i] || wheels[i] == null || wheels[i].collider == null) continue;
            Vector3 a = transform.InverseTransformPoint(wheels[i].collider.transform.position);

            for (int j = i + 1; j < wheels.Length; j++)
            {
                if (used[j] || wheels[j] == null || wheels[j].collider == null) continue;
                Vector3 b = transform.InverseTransformPoint(wheels[j].collider.transform.position);

                if (Mathf.Abs(a.z - b.z) < 0.25f && a.x * b.x < 0f)
                {
                    antiRollPairs.Add(new Vector2Int(i, j));
                    used[i] = used[j] = true;
                    break;
                }
            }
        }
    }

    // ------------------------------------------------------------------ update

    void Update()
    {
        if (input.toggleEngine)
        {
            input.toggleEngine = false;
            ToggleEngine();
        }
    }

    void LateUpdate()
    {
        UpdateWheelVisuals();
    }

    void UpdateWheelVisuals()
    {
        foreach (var w in wheels)
        {
            if (w == null || w.collider == null || w.mesh == null) continue;
            w.collider.GetWorldPose(out Vector3 pos, out Quaternion rot);
            w.mesh.SetPositionAndRotation(pos, rot * Quaternion.Euler(w.meshRotationOffset));
        }
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        Vector3 vel = Vel;
        ForwardSpeed = Vector3.Dot(vel, transform.forward);

        int grounded = 0;
        foreach (var w in wheels)
            if (w != null && w.collider != null && w.collider.isGrounded) grounded++;
        IsGrounded = grounded >= Mathf.Min(2, wheels.Length);

        UpdateEngineState(dt);
        UpdateNitro(dt);
        ApplyDrive(dt);
        ApplySteering(dt);
        ApplyAntiRoll();
        ApplyAeroForces(vel);
    }

    // ------------------------------------------------------------------ engine

    void UpdateEngineState(float dt)
    {
        if (EngineState != EngineState.Starting) return;

        startTimer -= dt;
        if (startTimer <= 0f)
        {
            EngineState = EngineState.Running;
            EngineStarted?.Invoke();
        }
    }

    public void StartEngine()
    {
        if (EngineState != EngineState.Off) return;
        EngineState = EngineState.Starting;
        startTimer = Mathf.Max(0.05f, ignitionTime);
        IgnitionStarted?.Invoke();
    }

    public void StopEngine()
    {
        if (EngineState == EngineState.Off) return;
        StopNitro();
        EngineState = EngineState.Off;
        EngineStopped?.Invoke();
    }

    public void ToggleEngine()
    {
        if (EngineState == EngineState.Off) StartEngine();
        else StopEngine();
    }

    // ------------------------------------------------------------------ driving

    void ApplyDrive(float dt)
    {
        float maxForward = data.maxSpeed * speedScale * (NitroActive ? data.nitroSpeedMultiplier : 1f);
        float maxReverse = data.maxReverseSpeed;

        float motor = 0f;
        float brake = 0f;

        if (EngineRunning)
        {
            float thr = NitroActive ? 1f : input.throttle;

            if (thr > 0.01f)
            {
                if (ForwardSpeed < -0.5f)
                    brake = data.brakeTorque * thr; // moving backwards: brake first
                else
                {
                    float fade = Mathf.Clamp01((maxForward - ForwardSpeed) / (maxForward * 0.1f));
                    motor = data.motorTorque * thr * fade * (NitroActive ? data.nitroTorqueMultiplier : 1f);
                }
            }
            else if (thr < -0.01f)
            {
                if (ForwardSpeed > 0.5f)
                    brake = data.brakeTorque * -thr; // moving forward: brake
                else
                {
                    float fade = Mathf.Clamp01((maxReverse + ForwardSpeed) / (maxReverse * 0.2f));
                    motor = data.motorTorque * 0.5f * thr * fade;
                }
            }
            else
            {
                brake = Mathf.Abs(ForwardSpeed) < 0.3f ? data.brakeTorque * 0.3f : data.engineBrakeTorque;
            }
        }
        else
        {
            // engine off / starting: just coast (and hold still on slopes)
            brake = Mathf.Abs(ForwardSpeed) < 0.3f ? data.brakeTorque * 0.3f : data.engineBrakeTorque;
        }

        float motorPerWheel = driveCount > 0 ? motor / driveCount : 0f;

        foreach (var w in wheels)
        {
            if (w == null || w.collider == null) continue;

            w.collider.motorTorque = w.drive ? motorPerWheel : 0f;

            bool hb = input.handbrake && w.handbrake;
            w.collider.brakeTorque = hb ? Mathf.Max(brake, data.handbrakeTorque) : brake;

            // handbrake wheels lose sideways grip (drift / slide)
            if (w.handbrake)
            {
                float target = hb ? data.handbrakeGripMultiplier : 1f;
                w.gripFactor = Mathf.MoveTowards(w.gripFactor, target, dt * 8f);
                WheelFrictionCurve s = w.collider.sidewaysFriction;
                s.stiffness = data.sidewaysStiffness * w.gripFactor;
                w.collider.sidewaysFriction = s;
            }
        }
    }

    void ApplySteering(float dt)
    {
        smoothedSteer = Mathf.MoveTowards(smoothedSteer, input.steer, steerResponse * dt);

        float maxAngle = Mathf.Lerp(data.maxSteerAngle, data.highSpeedSteerAngle, NormalizedSpeed);
        float angle = smoothedSteer * maxAngle;

        foreach (var w in wheels)
            if (w != null && w.collider != null && w.steer)
                w.collider.steerAngle = angle;
    }

    // Anti-roll bar: transfers load between the two wheels of an axle.
    void ApplyAntiRoll()
    {
        foreach (Vector2Int p in antiRollPairs)
        {
            WheelCollider l = wheels[p.x].collider;
            WheelCollider r = wheels[p.y].collider;

            bool groundedL = l.GetGroundHit(out WheelHit hitL);
            bool groundedR = r.GetGroundHit(out WheelHit hitR);

            float travelL = groundedL
                ? (-l.transform.InverseTransformPoint(hitL.point).y - l.radius) / l.suspensionDistance
                : 1f;
            float travelR = groundedR
                ? (-r.transform.InverseTransformPoint(hitR.point).y - r.radius) / r.suspensionDistance
                : 1f;

            float force = (travelL - travelR) * data.antiRollForce;

            if (groundedL) rb.AddForceAtPosition(l.transform.up * -force, l.transform.position);
            if (groundedR) rb.AddForceAtPosition(r.transform.up * force, r.transform.position);
        }
    }

    void ApplyAeroForces(Vector3 vel)
    {
        float speed = vel.magnitude;
        if (speed < 0.1f) return;

        rb.AddForce(-vel.normalized * (data.drag * speed * speed));

        if (IsGrounded && data.downforce > 0f)
            rb.AddForce(-transform.up * (data.downforce * speed));
    }

    // ------------------------------------------------------------------ nitro

    void UpdateNitro(float dt)
    {
        bool wants = input.nitro && EngineRunning;

        if (!NitroActive && wants && NitroAmount >= minNitroToStart)
        {
            NitroActive = true;
            NitroStarted?.Invoke();
        }

        if (NitroActive)
        {
            if (!wants || NitroAmount <= 0f) StopNitro();
            else NitroAmount = Mathf.Max(0f, NitroAmount - data.nitroDrainPerSecond * dt);
        }
    }

    public void StopNitro()
    {
        if (!NitroActive) return;
        NitroActive = false;
        NitroStopped?.Invoke();
    }

    public void AddNitro(float amount)
    {
        NitroAmount = Mathf.Min(data.maxNitro, NitroAmount + amount);
    }

    // ------------------------------------------------------------------ utilities

    /// <summary>Puts the car back on its wheels (keeps position and heading).</summary>
    public void ResetUpright()
    {
        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.ProjectOnPlane(transform.up, Vector3.up);
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;

        ResetToPose(transform.position + Vector3.up * 1.2f, Quaternion.LookRotation(fwd.normalized, Vector3.up));
    }

    public void ResetToPose(Vector3 position, Quaternion rotation)
    {
        StopNitro();
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = Vector3.zero;
#else
        rb.velocity = Vector3.zero;
#endif
        rb.angularVelocity = Vector3.zero;
        rb.position = position;
        rb.rotation = rotation;
        transform.SetPositionAndRotation(position, rotation);
        Physics.SyncTransforms();
    }
}