using UnityEngine;

/// <summary>
/// Aim down sights. Only works while the held item's category is Weapon AND its prefab has a WeaponSights.
///
///   Right mouse (hold, or toggle with "Toggle Aim")   aim
///   Shift while aimed                                  hold your breath (steadies the sights, then you gasp)
///
/// How it behaves:
/// - The raise is a spring, not a canned animation: heavier guns settle slower, and a hard landing
///   knocks you off the sights.
/// - Sights line up with the eye through WeaponSights' anchor; the camera FOV narrows and mouse sensitivity
///   follows the zoom, so the same hand motion moves the view the same visual distance.
/// - Aiming blocks sprinting and slows walking. Aiming itself is free; only holding your breath costs stamina.
/// - The sights wander with breathing and muscle tremor. Tired arms, a heavy gun, moving and standing all
///   change how much. Crouching steadies you. Holding your breath nearly freezes it, but only for a few
///   seconds, then there is a cooldown before you can do it again. If you run dry you gasp: the sway jumps
///   above normal and then settles back to normal breathing gradually, in a straight line.
/// - Not possible while: reaching for an item, inspecting, winding up a throw, pressed against a wall,
///   or while a UI/menu owns the cursor.
///
/// Put this on the Player root next to HeldItemSlot. Runs just before HeldItemSlot so the pose is current.
/// </summary>
[DefaultExecutionOrder(40)]
public class WeaponADS : MonoBehaviour
{
    [Header("References (found automatically if empty)")]
    public MouseLook look;
    public PlayerMotor motor;
    public PlayerStamina stamina;
    public HeldItemSlot slot;

    [Header("Input")]
    [Tooltip("Off = hold right mouse to aim. On = click to start aiming, click again to stop.")]
    public bool toggleAim = false;

    [Header("Raise / Lower")]
    [Tooltip("Springiness. 1 = no overshoot. Lower = the gun settles onto the sights with a small bounce.")]
    [Range(0.4f, 1f)] public float dampingRatio = 0.78f;
    [Tooltip("Extra raise time per kg above 2.5 kg (0.12 = a 6.5 kg gun takes ~50% longer).")]
    public float heavySlowdownPerKg = 0.12f;
    [Tooltip("Lowering the gun is quicker than raising it.")]
    [Range(0.3f, 1f)] public float lowerTimeFactor = 0.7f;

    [Header("Restrictions")]
    [Tooltip("Can't aim when the gun is pushed back by a wall by more than this (metres).")]
    public float wallBlockPush = 0.1f;
    [Tooltip("Landing faster than this (m/s) knocks you off the sights.")]
    public float knockOffLandSpeed = 3f;
    public float landingLockout = 0.45f;
    [Tooltip("How hard a landing yanks the gun off the sights.")]
    public float landingKick = 0.35f;

    [Header("Sway")]
    [Tooltip("Base wobble of the sights in degrees at rest, with a medium-weight rifle and fresh arms.")]
    public float swayDegrees = 0.35f;
    [Tooltip("Extra sway at full walking speed (1 = doubles it).")]
    public float movingSwayGain = 1.2f;
    [Tooltip("Extra sway when stamina is gone (2.5 = 3.5x as much).")]
    public float fatigueSwayGain = 2.5f;
    [Range(0.3f, 1f)] public float crouchSteadiness = 0.7f;

    [Header("Hold Breath")]
    [Tooltip("Seconds you can hold your breath when fresh. Shorter when tired.")]
    public float breathHoldSeconds = 5f;
    [Tooltip("Reserve recovered per second while breathing normally.")]
    public float breathRecoverRate = 0.6f;
    [Range(0f, 1f)] public float holdSwayMultiplier = 0.12f;
    [Tooltip("Seconds you must wait after a breath hold ends (released or run dry) before you can hold again.")]
    public float holdBreathCooldown = 4f;
    [Tooltip("Gasp intro: seconds the sway takes to rise smoothly from the held-breath calm up to the gasp peak. Short = sharper gasp.")]
    public float gaspRiseSeconds = 0.25f;
    [Tooltip("Gasp recovery: seconds the sway takes to return from the peak to normal breathing, in a straight line.")]
    public float gaspSeconds = 2.5f;
    [Tooltip("Sway multiplier at the very start of the gasp (1 = normal).")]
    public float gaspSwayMultiplier = 2.2f;
    [Tooltip("Stamina per second spent while holding your breath.")]
    public float holdBreathStaminaPerSecond = 0.8f;

    [Header("Camera")]
    public float fovFollowSpeed = 18f;

    // ---- Public state ----
    /// <summary>True while the player wants to aim and is allowed to.</summary>
    public bool IsAiming => wantsAim;
    /// <summary>0 = hip, 1 = fully on the sights.</summary>
    public float AimBlend => aimBlend;
    public bool IsHoldingBreath => holdingBreath;
    /// <summary>0..1 of the held-breath reserve left (for a HUD).</summary>
    public float BreathReserve01 => Mathf.Clamp01(breathReserve / Mathf.Max(0.01f, breathHoldSeconds));
    /// <summary>Seconds left before you can hold your breath again (0 = ready).</summary>
    public float BreathCooldownRemaining => Mathf.Max(0f, breathCooldown);

    Camera cam;
    Pickupable cachedItem;
    WeaponSights sights;

    bool wantsAim;
    float aimBlend, aimVel, fovBlend;
    float lockoutUntil;

    float breathReserve, gaspTimer, breathCooldown, breathPhase, swayAmp;
    bool holdingBreath;

    float baseFov = 60f, fovMult = 1f;
    bool fovOverridden;

    void Awake()
    {
        if (motor == null) motor = GetComponent<PlayerMotor>();
        if (stamina == null) stamina = GetComponent<PlayerStamina>();
        if (slot == null) slot = GetComponent<HeldItemSlot>();
        if (look == null && slot != null) look = slot.look;
        if (look == null) look = GetComponentInChildren<MouseLook>();
        if (look != null)
        {
            cam = look.GetComponent<Camera>();
            if (cam == null) cam = look.GetComponentInChildren<Camera>();
        }
        breathReserve = breathHoldSeconds;
    }

    void OnEnable()
    {
        if (motor != null) motor.Landed += OnLanded;
    }

    void OnDisable()
    {
        if (motor != null)
        {
            motor.Landed -= OnLanded;
            motor.SprintBlocked = false;
            motor.SpeedMultiplier = 1f;
        }
        if (cam != null && fovOverridden) cam.fieldOfView = baseFov;
        if (look != null) look.SensitivityScale = 1f;
        fovOverridden = false;
    }

    void OnLanded(float impact)
    {
        if (impact < knockOffLandSpeed) return;
        if (!wantsAim && aimBlend < 0.05f) return;
        wantsAim = false;
        lockoutUntil = Time.time + landingLockout;
        aimVel -= impact * landingKick; // the gun is jolted off the sights
    }

    void LateUpdate()
    {
        if (slot == null || motor == null || look == null) return;

        float rawDt = Time.deltaTime;
        if (rawDt <= 0f) return;
        float dt = Mathf.Min(rawDt, 0.033f);

        RefreshItem();

        // ---------------------------------------------------------------- intent
        bool canAim = CanAim();
        if (toggleAim)
        {
            if (canAim && GameInput.AimPressed()) wantsAim = !wantsAim;
            if (!canAim) wantsAim = false;
        }
        else
        {
            wantsAim = canAim && GameInput.AimHeld();
        }

        float mass = slot.HasItem ? slot.HeldData.massKg : 3f;

        // ---------------------------------------------------------------- raise / lower spring
        float baseTime = sights != null ? sights.EffectiveAdsTime : 0.3f;
        float time = baseTime * Mathf.Min(2f, 1f + Mathf.Max(0f, mass - 2.5f) * heavySlowdownPerKg);
        if (!wantsAim) time *= lowerTimeFactor;
        float w = 4.5f / Mathf.Max(0.05f, time);
        float target = wantsAim ? 1f : 0f;

        aimVel += (w * w * (target - aimBlend) - 2f * dampingRatio * w * aimVel) * dt;
        aimBlend += aimVel * dt;
        if (aimBlend < 0f) { aimBlend = 0f; aimVel = Mathf.Max(0f, aimVel); }
        else if (aimBlend > 1f) { aimBlend = 1f; aimVel = Mathf.Min(0f, aimVel); }

        // ---------------------------------------------------------------- body effects
        motor.SprintBlocked = wantsAim;
        float moveMult = sights != null ? sights.moveSpeedMultiplier : 1f;
        motor.SpeedMultiplier = Mathf.Lerp(1f, moveMult, aimBlend);

        // ---------------------------------------------------------------- breath + sway
        float fatigue = stamina != null ? stamina.Fatigue : 0f;
        UpdateBreath(dt, fatigue);
        Vector3 swayEuler = ComputeSway(dt, fatigue, mass);

        // ---------------------------------------------------------------- hand the pose to the hands
        if (sights != null && slot.HasItem && aimBlend > 0.0001f)
        {
            sights.GetAimPose(out Vector3 pos, out Quaternion rot);
            slot.SetAimPose(aimBlend, pos, rot, swayEuler);
        }
        else
        {
            slot.SetAimPose(0f, Vector3.zero, Quaternion.identity, Vector3.zero);
        }

        UpdateCamera(rawDt);
    }

    // ================================================================== helpers

    void RefreshItem()
    {
        Pickupable cur = slot.Current;
        if (cur == cachedItem) return;

        cachedItem = cur;
        sights = cur != null ? cur.GetComponent<WeaponSights>() : null;

        // A different item in the hands starts from the hip.
        aimBlend = aimVel = 0f;
        wantsAim = false;
        holdingBreath = false;
        gaspTimer = 0f;
    }

    bool CanAim()
    {
        if (!slot.HasItem || slot.IsEquipping || slot.ExternalControl) return false;
        if (slot.HeldData == null || slot.HeldData.category != ItemCategory.Weapon) return false;
        if (sights == null) return false;
        if (!look.LookEnabled || Cursor.lockState != CursorLockMode.Locked) return false;
        if (slot.IsThrowCharging) return false;
        if (slot.WallPushAmount > wallBlockPush) return false;
        if (Time.time < lockoutUntil) return false;
        return true;
    }

    void UpdateBreath(float dt, float fatigue)
    {
        float maxReserve = breathHoldSeconds * Mathf.Lerp(1f, 0.4f, fatigue);
        bool wantHold = wantsAim && aimBlend > 0.7f && GameInput.SprintHeld();
        bool wasHolding = holdingBreath;
        if (breathCooldown > 0f) breathCooldown = Mathf.Max(0f, breathCooldown - dt);

        if (gaspTimer > 0f)
        {
            gaspTimer -= dt;
            holdingBreath = false;
        }
        else if (wantHold && breathCooldown <= 0f && breathReserve > 0f)
        {
            holdingBreath = true;
            breathReserve -= dt;
            if (stamina != null) stamina.Spend(holdBreathStaminaPerSecond * dt);
            if (breathReserve <= 0f)
            {
                breathReserve = 0f;
                holdingBreath = false;
                gaspTimer = gaspRiseSeconds + gaspSeconds;
            }
        }
        else
        {
            holdingBreath = false;
        }

        // A hold just ended (let go, stopped aiming, or ran dry): start the cooldown.
        if (wasHolding && !holdingBreath) breathCooldown = holdBreathCooldown;

        if (!holdingBreath)
            breathReserve = Mathf.Min(maxReserve, breathReserve + breathRecoverRate * dt);
    }

    Vector3 ComputeSway(float dt, float fatigue, float mass)
    {
        float swayMult = sights != null ? sights.EffectiveSwayMultiplier : 1f;
        float weightFactor = Mathf.Clamp(mass / 3.5f, 0.6f, 1.6f);
        float move = Mathf.Clamp01(motor.PlanarSpeed / Mathf.Max(0.1f, motor.walkSpeed)) * movingSwayGain;

        float amp = swayDegrees * swayMult * weightFactor * (1f + fatigue * fatigueSwayGain + move);
        if (motor.IsCrouching) amp *= crouchSteadiness;
        bool gasping = gaspTimer > 0f && !holdingBreath;
        if (holdingBreath) amp *= holdSwayMultiplier;
        else if (gasping)
        {
            float rise = Mathf.Max(0f, gaspRiseSeconds);
            float fall = Mathf.Max(0.01f, gaspSeconds);
            float elapsed = rise + fall - gaspTimer;

            float factor;
            if (elapsed < rise)
            {
                // Intro: smooth rise from the held-breath calm up to the gasp peak
                float u = Mathf.Clamp01(elapsed / rise);
                factor = Mathf.Lerp(holdSwayMultiplier, gaspSwayMultiplier, u * u * (3f - 2f * u));
            }
            else
            {
                // Recovery: straight line from the peak back to normal breathing
                factor = Mathf.Lerp(1f, gaspSwayMultiplier, Mathf.Clamp01(gaspTimer / fall));
            }
            amp *= factor;
        }

        // Holding/releasing breath eases in; the gasp follows its own straight line, so it is not smoothed.
        if (gasping) swayAmp = amp;
        else swayAmp += (amp - swayAmp) * (1f - Mathf.Exp(-6f * dt));

        // Breathing rises and falls the sights; muscle tremor wanders them around (slow noise on both axes)
        breathPhase += dt * Mathf.Lerp(0.25f, 0.9f, fatigue) * Mathf.PI * 2f;
        float t = Time.time;
        float nx = (Mathf.PerlinNoise(t * 1.3f, 11.3f) - 0.5f) * 2f;
        float ny = (Mathf.PerlinNoise(t * 1.1f, 47.9f) - 0.5f) * 2f;

        return new Vector3(
            Mathf.Sin(breathPhase) * 0.8f + nx * 0.5f,                    // pitch
            ny * 0.6f + Mathf.Sin(breathPhase * 0.5f + 1.3f) * 0.3f,      // yaw
            nx * 0.3f) * swayAmp;                                         // roll
    }

    void UpdateCamera(float rawDt)
    {
        if (sights != null) fovMult = sights.EffectiveFovMultiplier;
        fovBlend += (aimBlend - fovBlend) * (1f - Mathf.Exp(-fovFollowSpeed * rawDt));

        if (cam == null) return;

        if (!fovOverridden) baseFov = cam.fieldOfView; // follows any FOV setting changed elsewhere

        if (fovBlend > 0.0005f)
        {
            cam.fieldOfView = Mathf.Lerp(baseFov, baseFov * fovMult, fovBlend);
            fovOverridden = true;
        }
        else if (fovOverridden)
        {
            cam.fieldOfView = baseFov;
            fovOverridden = false;
        }

        // Same hand motion = same visual distance: scale by the ratio of the tangents of the half-FOVs.
        look.SensitivityScale = fovOverridden
            ? Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(baseFov * 0.5f * Mathf.Deg2Rad)
            : 1f;
    }
}