using UnityEngine;

/// <summary>
/// Input box of the vehicle. The player, an AI (or mobile UI buttons) write here;
/// the VehicleController only reads.
/// </summary>
public class CarInput : MonoBehaviour
{
    [Range(-1f, 1f)] public float throttle; // 1 = accelerate, -1 = brake / reverse
    [Range(-1f, 1f)] public float steer;    // -1 = left, 1 = right
    public bool handbrake;
    public bool nitro;

    /// <summary>Set to true for one press; the controller consumes it (turns the engine on/off).</summary>
    public bool toggleEngine;

    public void Clear()
    {
        throttle = 0f;
        steer = 0f;
        handbrake = false;
        nitro = false;
        toggleEngine = false;
    }
}