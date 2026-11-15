using UnityEngine;

/// <summary>
/// Vehicle data. Create one asset per vehicle: Assets > Create > Vehicles > Vehicle Data.
/// All vehicles use the same VehicleController; only these numbers change.
/// </summary>
[CreateAssetMenu(fileName = "NewVehicleData", menuName = "Vehicles/Vehicle Data")]
public class CarData : ScriptableObject
{
    [Header("Info")]
    public string displayName = "Car";
    public GameObject modelPrefab;

    [Header("Body")]
    public float mass = 1400f;
    [Tooltip("Lower the centre of mass (local space) so the car doesn't roll over easily.")]
    public Vector3 centerOfMassOffset = new Vector3(0f, -0.4f, 0f);
    [Tooltip("Pushes the car into the road with speed (N per m/s). 0 = off.")]
    public float downforce = 30f;
    [Tooltip("Air drag. Force = drag * speed^2 (N).")]
    public float drag = 0.35f;

    [Header("Engine / Brakes")]
    [Tooltip("Top speed in m/s (45 m/s = ~160 km/h).")]
    public float maxSpeed = 45f;
    [Tooltip("Top reverse speed in m/s.")]
    public float maxReverseSpeed = 9f;
    [Tooltip("TOTAL motor torque (Nm), split between the wheels marked as Drive. " +
             "Roughly: mass * wanted acceleration * wheel radius. 1400 * 8 * 0.35 = ~3900.")]
    public float motorTorque = 3900f;
    [Tooltip("Foot brake torque PER WHEEL (Nm).")]
    public float brakeTorque = 2500f;
    [Tooltip("Handbrake torque PER handbrake wheel (Nm).")]
    public float handbrakeTorque = 6000f;
    [Tooltip("Light braking when you release the throttle (Nm per wheel).")]
    public float engineBrakeTorque = 150f;

    [Header("Steering")]
    [Tooltip("Max steering angle at low speed (degrees).")]
    public float maxSteerAngle = 32f;
    [Tooltip("Steering angle at top speed (degrees). Lower = more stable.")]
    public float highSpeedSteerAngle = 7f;

    [Header("Suspension (computed automatically from mass)")]
    [Tooltip("Wheel Collider 'Suspension Distance' in metres (total travel).")]
    public float suspensionDistance = 0.25f;
    [Tooltip("Body bounce frequency in Hz. 1.0 = soft/floaty, 1.5 = normal car, 2.5 = stiff sports car.")]
    public float suspensionFrequency = 1.5f;
    [Tooltip("Damping ratio. 0.2 = bouncy, 0.4 = normal, 0.7+ = very stiff/no bounce.")]
    public float dampingRatio = 0.45f;
    [Range(0f, 1f)]
    [Tooltip("Spring 'Target Position'. 0.5 = rest in the middle of the travel.")]
    public float suspensionTarget = 0.5f;
    [Tooltip("Anti-roll bar strength. Higher = less body roll in corners.")]
    public float antiRollForce = 5000f;
    public float wheelMass = 20f;
    [Tooltip("How far below the wheel centre the tyre force is applied. Small value (0 - 0.2) = more stable.")]
    public float forceAppPointDistance = 0.1f;
    [Tooltip("Rotational damping of the wheel. Helps stop wheels from spinning forever.")]
    public float wheelDampingRate = 0.25f;

    [Header("Tyres")]
    public float forwardStiffness = 1.6f;
    [Tooltip("Sideways grip. Higher = less sliding. Lower = drifty.")]
    public float sidewaysStiffness = 1.7f;
    [Range(0.1f, 1f)]
    [Tooltip("Sideways grip multiplier on handbrake wheels while the handbrake is held.")]
    public float handbrakeGripMultiplier = 0.45f;

    [Header("Nitro")]
    public float maxNitro = 100f;
    public float nitroDrainPerSecond = 25f;
    public float nitroSpeedMultiplier = 1.3f;
    public float nitroTorqueMultiplier = 2f;
}