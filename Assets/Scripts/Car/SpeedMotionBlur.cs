using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(ThirdPersonCarCamera))]
public class SpeedMotionBlur : MonoBehaviour
{
    [Tooltip("Optional. Leave empty to create a global Volume automatically.")]
    public Volume volume;

    [Header("High speed")]
    [Range(0f, 1f)] public float startAtSpeed = 0.55f;   // blur starts at 55% of max speed
    public float speedIntensity = 0.5f;                   // intensity at max speed
    public float speedClamp = 0.1f;

    [Header("Nitro")]
    public float nitroIntensity = 1f;
    public float nitroClamp = 0.25f;

    [Header("Response")]
    public float rampUp = 6f;
    public float rampDown = 3f;

    ThirdPersonCarCamera carCamera;
    MotionBlur blur;
    float intensity;
    float clamp;

    void Awake()
    {
        carCamera = GetComponent<ThirdPersonCarCamera>();
    }

    void Start()
    {
        if (volume == null)
        {
            var go = new GameObject("SpeedMotionBlurVolume");
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10;
            volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
        }

        if (!volume.profile.TryGet(out blur))
            blur = volume.profile.Add<MotionBlur>(true);

        blur.intensity.overrideState = true;
        blur.clamp.overrideState = true;
        blur.intensity.value = 0f;
        blur.clamp.value = speedClamp;
        clamp = speedClamp;

        var cam = GetComponent<Camera>();
        if (cam != null) cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
    }

    void Update()
    {
        VehicleController car = carCamera.target;
        if (car == null || blur == null) return;

        float targetIntensity = Mathf.InverseLerp(startAtSpeed, 1f, car.NormalizedSpeed) * speedIntensity;
        float targetClamp = speedClamp;

        if (car.NitroActive)
        {
            targetIntensity = nitroIntensity;
            targetClamp = nitroClamp;
        }

        float k = targetIntensity > intensity ? rampUp : rampDown;
        float t = 1f - Mathf.Exp(-k * Time.deltaTime);
        intensity = Mathf.Lerp(intensity, targetIntensity, t);
        clamp = Mathf.Lerp(clamp, targetClamp, t);

        blur.intensity.value = intensity;
        blur.clamp.value = clamp;
    }
}