using UnityEngine;

/// <summary>
/// Engine sound: ignition clip, idle loop, and RPM layers (one loop per RPM range).
/// Reads the vehicle by itself (VehicleController + CarInput on the same object).
///
/// Flow: Ignition (key turn) -> engine catches -> idle loop -> rev loops fade in as RPM rises.
/// Engine off = silence (plays the optional shutdown clip).
/// </summary>
public class EngineSound : MonoBehaviour
{
    [System.Serializable]
    public class Loop
    {
        public AudioClip clip;
        [Tooltip("RPM this clip was RECORDED at. If wrong, the pitch will be off.")]
        public float rpm;
    }

    [Header("One-shots")]
    [Tooltip("Played when the engine is started (key turn + cranking).")]
    public AudioClip ignitionClip;
    [Tooltip("Optional: played when the engine is turned off.")]
    public AudioClip shutdownClip;
    [Tooltip("Set the vehicle's ignition time from the ignition clip length, so the engine 'catches' as the clip ends.")]
    public bool syncIgnitionTime = true;
    [Tooltip("Seconds trimmed from the clip length (engine catches slightly before the clip ends).")]
    public float ignitionTailTrim = 0.2f;
    [Range(0f, 1f)] public float ignitionVolume = 0.9f;

    [Header("Idle")]
    [Tooltip("Seamless loop of the engine idling.")]
    public AudioClip idleLoop;
    [Range(0f, 1f)] public float idleVolume = 0.8f;
    [Tooltip("Pitch of the idle loop at idle RPM (1 = as recorded).")]
    public float idlePitch = 1f;
    [Tooltip("How much the idle pitch follows the RPM while still in the idle range.")]
    [Range(0f, 1f)] public float idlePitchFollow = 0.3f;
    [Tooltip("RPM above idle where the idle loop has fully faded into the rev loops.")]
    public float idleBlendRange = 1200f;
    [Tooltip("Fade-in time of the engine sound after the engine catches (s).")]
    public float engineFadeIn = 0.3f;
    [Tooltip("Fade-out time when the engine stops (s).")]
    public float engineFadeOut = 0.25f;

    [Header("Rev loops, lowest RPM first")]
    public Loop[] loops;
    [Header("Auto-fill (right-click the component > Fill RPMs From Step)")]
    public float firstRpm = 1000f;
    public float rpmStep = 1000f;

    [Header("Read from the vehicle automatically")]
    public bool autoFromCar = true;
    public float idleRpm = 900f;
    public float maxRpm = 7000f;
    [Min(1)] public int gearCount = 5;
    [Range(0f, 0.6f)]
    [Tooltip("How much of the RPM range is lost at each gear change. 0 = no shifts.")]
    public float shiftDropFraction = 0.25f;
    [Range(0.2f, 1f)]
    [Tooltip("Fraction of max RPM when revving while stopped.")]
    public float revWhenStopped = 0.55f;
    public float nitroRpmBonus = 600f;
    [Tooltip("Approximate acceleration (m/s²) of the vehicle. Limits how fast the RPM can rise from being pushed or hitting things.")]
    public float approxAcceleration = 9f;
    [Range(0f, 1f)]
    [Tooltip("How much RPM can rise from speed NOT coming from the engine when you are not accelerating.")]
    public float coastRiseFactor = 0.15f;

    [Header("Values (filled automatically, or set from another script)")]
    public float rpm = 900f;
    [Range(0f, 1f)] public float throttle = 0f;

    [Header("Smoothing")]
    public float rpmRiseSmoothing = 4f;
    public float rpmFallSmoothing = 3f;
    public float throttleSmoothing = 4f;

    [Header("Mix")]
    [Range(0f, 1f)] public float offThrottleVolume = 0.6f;
    [Range(0f, 1f)] public float masterVolume = 0.7f;
    [Range(0.05f, 1f)] public float crossfadeWidth = 0.3f;
    [Range(0.05f, 1f)] public float maxPitchShift = 0.4f;

    [Header("3D sound")]
    [Tooltip("0 = 2D (always audible, player car). 1 = 3D (AI / parked cars).")]
    [Range(0f, 1f)] public float spatialBlend = 0f;
    public float minDistance = 8f;
    public float maxDistance = 80f;

    AudioSource[] sources;
    AudioSource idleSource;
    AudioSource fxSource;
    float[] weights;
    float smoothRpm;
    float smoothThrottle;
    float drivenSpeed;
    float engineLevel;   // 0 = off, 1 = running
    VehicleController car;
    CarInput carInput;

    [ContextMenu("Fill RPMs From Step")]
    void FillRpms()
    {
        for (int i = 0; i < loops.Length; i++)
            loops[i].rpm = firstRpm + i * rpmStep;
    }

    void Awake()
    {
        car = GetComponentInParent<VehicleController>();
        carInput = GetComponentInParent<CarInput>();

        if (autoFromCar && (car == null || carInput == null))
            Debug.LogWarning("EngineSound: VehicleController/CarInput not found. Put EngineSound on the vehicle object.", this);

        // Awake runs before any Start, so the controller sees the right ignition time.
        if (car != null && syncIgnitionTime && ignitionClip != null)
            car.ignitionTime = Mathf.Max(0.1f, ignitionClip.length - ignitionTailTrim);
    }

    AudioSource CreateSource(AudioClip clip, bool loop)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.clip = clip;
        s.loop = loop;
        s.volume = 0f;
        s.playOnAwake = false;
        s.spatialBlend = spatialBlend;
        s.rolloffMode = AudioRolloffMode.Linear;
        s.minDistance = minDistance;
        s.maxDistance = maxDistance;
        s.dopplerLevel = 0f;
        return s;
    }

    void Start()
    {
        if (loops == null) loops = new Loop[0];

        if (loops.Length > 1 && loops[loops.Length - 1].rpm <= loops[0].rpm)
        {
            Debug.LogWarning("EngineSound: loop RPMs were not filled in; using First Rpm / Rpm Step.", this);
            FillRpms();
        }

        smoothRpm = idleRpm;
        sources = new AudioSource[loops.Length];
        weights = new float[loops.Length];

        for (int i = 0; i < loops.Length; i++)
        {
            if (loops[i].clip == null)
                Debug.LogWarning("EngineSound: loop " + i + " has no AudioClip.", this);
            sources[i] = CreateSource(loops[i].clip, true);
            sources[i].Play(); // all start together so they stay in sync
        }

        if (idleLoop != null)
        {
            idleSource = CreateSource(idleLoop, true);
            idleSource.Play();
        }

        fxSource = CreateSource(null, false);

        if (car != null)
        {
            car.IgnitionStarted += OnIgnition;
            car.EngineStopped += OnEngineStopped;

            // the controller may have started the ignition before we subscribed
            if (car.EngineState == EngineState.Starting)
                OnIgnition();
            else if (car.EngineState == EngineState.Running)
                engineLevel = 1f;
        }
    }

    void OnDestroy()
    {
        if (car == null) return;
        car.IgnitionStarted -= OnIgnition;
        car.EngineStopped -= OnEngineStopped;
    }

    void OnIgnition()
    {
        smoothRpm = idleRpm;
        drivenSpeed = 0f;
        if (fxSource == null || ignitionClip == null) return;
        fxSource.Stop();
        fxSource.clip = ignitionClip;
        fxSource.volume = ignitionVolume;
        fxSource.Play();
    }

    void OnEngineStopped()
    {
        if (fxSource == null) return;
        fxSource.Stop(); // cancels the ignition if it was still cranking
        if (shutdownClip == null) return;
        fxSource.clip = shutdownClip;
        fxSource.volume = ignitionVolume;
        fxSource.Play();
    }

    // RPM + throttle from the vehicle. The arcade/realistic car has no gearbox, so gears are simulated.
    void ReadFromCar(float dt)
    {
        if (car == null || carInput == null || car.data == null) return;

        float idle = idleRpm;
        float max = maxRpm;

        float actualSpeed = Mathf.Abs(car.ForwardSpeed);
        bool running = car.EngineRunning;
        float thr = running ? (car.NitroActive ? 1f : Mathf.Clamp01(carInput.throttle)) : 0f;

        float accelRate = approxAcceleration * (car.NitroActive ? car.data.nitroTorqueMultiplier : 1f);
        float riseRate = accelRate * Mathf.Lerp(coastRiseFactor, 1.25f, thr);

        if (actualSpeed > drivenSpeed)
            drivenSpeed = Mathf.MoveTowards(drivenSpeed, actualSpeed, riseRate * dt);
        else
            drivenSpeed = actualSpeed;

        float baseMax = Mathf.Max(0.1f, car.data.maxSpeed * car.speedScale);
        float speedNorm = Mathf.Clamp01(drivenSpeed / baseMax);

        int gears = Mathf.Max(1, gearCount);
        float x = speedNorm * gears;
        int gear = Mathf.Min(gears - 1, Mathf.FloorToInt(x));
        throttle = thr;

        float launchRpm = Mathf.Lerp(idle, max * revWhenStopped, smoothThrottle);
        float low = gear == 0 ? launchRpm : Mathf.Lerp(idle, max, 1f - shiftDropFraction);
        float target = Mathf.Lerp(low, max, x - gear);
        if (car.NitroActive) target += nitroRpmBonus;

        rpm = running ? target : idle;
    }

    void Update()
    {
        if (sources == null) return;

        float dt = Time.deltaTime;

        if (autoFromCar) ReadFromCar(dt);

        // engine on/off level (fades everything except the one-shots)
        bool running = car == null || car.EngineRunning;
        float fadeTime = running ? engineFadeIn : engineFadeOut;
        engineLevel = Mathf.MoveTowards(engineLevel, running ? 1f : 0f, dt / Mathf.Max(0.01f, fadeTime));

        // RPM rises fast, falls slowly
        float k = rpm > smoothRpm ? rpmRiseSmoothing : rpmFallSmoothing;
        smoothRpm = Mathf.Lerp(smoothRpm, rpm, 1f - Mathf.Exp(-k * dt));

        smoothThrottle = Mathf.MoveTowards(smoothThrottle, throttle, throttleSmoothing * dt);

        // idle <-> rev loops blend (equal power)
        float blend = idleLoop == null
            ? 1f
            : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(idleRpm + 50f, idleRpm + Mathf.Max(100f, idleBlendRange), smoothRpm));
        float idleGain = Mathf.Cos(blend * Mathf.PI * 0.5f);
        float revGain = Mathf.Sin(blend * Mathf.PI * 0.5f);

        if (idleSource != null)
        {
            idleSource.volume = idleGain * idleVolume * masterVolume * engineLevel;
            float follow = Mathf.InverseLerp(idleRpm, idleRpm + Mathf.Max(100f, idleBlendRange), smoothRpm);
            idleSource.pitch = idlePitch * (1f + follow * idlePitchFollow);
        }

        UpdateRevLoops(revGain);
    }

    void UpdateRevLoops(float revGain)
    {
        if (loops.Length == 0) return;

        System.Array.Clear(weights, 0, weights.Length);
        int last = loops.Length - 1;

        if (smoothRpm <= loops[0].rpm)
        {
            weights[0] = 1f;
        }
        else if (smoothRpm >= loops[last].rpm)
        {
            weights[last] = 1f;
        }
        else
        {
            int lo = 0;
            while (lo < last - 1 && smoothRpm >= loops[lo + 1].rpm) lo++;

            float t = Mathf.InverseLerp(loops[lo].rpm, loops[lo + 1].rpm, smoothRpm);
            float w = Mathf.Max(0.02f, crossfadeWidth);
            float f = Mathf.Clamp01((t - (0.5f - w * 0.5f)) / w);
            weights[lo] = Mathf.Cos(f * Mathf.PI * 0.5f);
            weights[lo + 1] = Mathf.Sin(f * Mathf.PI * 0.5f);
        }

        float throttleVol = Mathf.Lerp(offThrottleVolume, 1f, smoothThrottle);
        float minPitch = 1f / (1f + maxPitchShift);
        float maxPitch = 1f + maxPitchShift;

        for (int i = 0; i < loops.Length; i++)
        {
            sources[i].volume = weights[i] * throttleVol * masterVolume * revGain * engineLevel;

            if (weights[i] > 0.001f)
                sources[i].pitch = Mathf.Clamp(smoothRpm / Mathf.Max(1f, loops[i].rpm), minPitch, maxPitch);
        }
    }
}