using UnityEngine;

/// <summary>
/// Mouse look + physics-driven camera. Put this on the Camera (a child of the player).
/// The camera is positioned/rotated in world space every LateUpdate, so the body's
/// physics interpolation never makes the view jitter.
///
/// The head sits on a spring-damper: landings dip it, jumps crouch it, accelerating
/// makes it lag behind the body, and footsteps add bob and sway.
/// </summary>
public class MouseLook : MonoBehaviour
{
    [Header("References")]
    public PlayerMotor motor;

    [Header("Look")]
    [Tooltip("Degrees per mouse count.")]
    public float sensitivity = 0.09f;
    [Tooltip("Small head inertia. 0.03-0.06 feels weighty, 0 is instant.")]
    public float lookSmoothTime = 0.04f;
    public float minPitch = -85f;
    public float maxPitch = 85f;
    public bool invertY = false;
    public bool LookEnabled = true; // set false while an inspect/UI mode owns the mouse

    [Header("Head Spring")]
    public float stiffness = 110f;
    public float damping = 11f;
    [Tooltip("Dip per m/s of landing impact.")]
    public float landingKick = 0.18f;
    public float jumpKick = 0.35f;
    [Tooltip("How far the head lags behind body acceleration.")]
    public float accelerationInfluence = 0.6f;
    public float maxDip = 0.25f;

    [Header("Tired Breathing")]
    [Tooltip("Head heave from heavy breathing when stamina is low (needs PlayerStamina).")]
    public float breathStrength = 4f;
    public float breathPitch = 1.2f;

    [Header("Footstep Bob")]
    public float bobHeight = 0.018f;
    public float bobSway = 0.012f;
    public float bobRoll = 0.5f;
    public float strafeRoll = 0.6f;

    public float Yaw => yaw;

    float targetYaw, targetPitch, yaw, pitch, yawVel, pitchVel;
    Vector3 springPos, springVel;
    float bobBlend, roll, rollVel, breathPhase;
    PlayerStamina stamina;

    void Awake()
    {
        if (motor == null) motor = GetComponentInParent<PlayerMotor>();
        if (motor != null) stamina = motor.GetComponent<PlayerStamina>();
        yaw = targetYaw = motor != null ? motor.transform.eulerAngles.y : transform.eulerAngles.y;
    }

    void Start() => SetCursorLocked(true);

    void OnEnable()
    {
        if (motor == null) return;
        motor.Landed += OnLanded;
        motor.Jumped += OnJumped;
    }

    void OnDisable()
    {
        if (motor == null) return;
        motor.Landed -= OnLanded;
        motor.Jumped -= OnJumped;
    }

    public void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    void OnLanded(float impact) => springVel.y -= impact * landingKick;
    void OnJumped() => springVel.y -= jumpKick;

    void Update()
    {
        if (GameInput.EscapePressed()) SetCursorLocked(false);
        else if (LookEnabled && Cursor.lockState != CursorLockMode.Locked && GameInput.ClickPressed())
            SetCursorLocked(true);

        if (LookEnabled && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 d = GameInput.LookDelta() * sensitivity;
            targetYaw += d.x;
            targetPitch = Mathf.Clamp(targetPitch + (invertY ? d.y : -d.y), minPitch, maxPitch);
        }

        yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVel, lookSmoothTime);
        pitch = Mathf.SmoothDamp(pitch, targetPitch, ref pitchVel, lookSmoothTime);
    }

    void LateUpdate()
    {
        if (motor == null) return;
        float dt = Mathf.Min(Time.deltaTime, 0.033f);
        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);

        // --- Spring-damper head: external force comes from body acceleration ---
        Vector3 accel = motor.LocalAcceleration;
        Vector3 external = new Vector3(-accel.x, 0f, -accel.z) * accelerationInfluence;

        // Tired breathing: a rhythmic push on the head that gets faster and stronger as you tire
        float fatigue = stamina != null ? stamina.Fatigue : 0f;
        breathPhase += dt * Mathf.Lerp(0.25f, 0.9f, fatigue) * Mathf.PI * 2f;
        float breath = Mathf.Sin(breathPhase);
        external.y += breath * breathStrength * fatigue;

        Vector3 force = -stiffness * springPos - damping * springVel + external;
        springVel += force * dt;
        springPos += springVel * dt;
        springPos.y = Mathf.Max(springPos.y, -maxDip);

        // --- Footstep bob (fades in/out with movement) ---
        float speed01 = Mathf.Clamp01(motor.PlanarSpeed / motor.sprintSpeed);
        float bobTarget = (motor.IsGrounded && motor.PlanarSpeed > 0.3f) ? 1f : 0f;
        bobBlend = Mathf.MoveTowards(bobBlend, bobTarget, dt * 4f);
        float scale = bobBlend * (0.4f + speed01 * 0.6f);

        float phase = motor.StepPhase;
        float parity = motor.RightFootNext ? 1f : 0f;
        float bobY = -Mathf.Cos(phase * Mathf.PI * 2f) * bobHeight * scale;  // lowest at foot strike
        float swing = Mathf.Sin((phase + parity) * Mathf.PI);                // one cycle per two steps
        float bobX = swing * bobSway * scale;

        // --- Roll: lean into strafing + a touch of sway ---
        Vector3 localVel = Quaternion.Inverse(yawRot) * motor.PlanarVelocity;
        float rollTarget = -localVel.x * strafeRoll - swing * bobRoll * scale;
        roll = Mathf.SmoothDamp(roll, rollTarget, ref rollVel, 0.12f);

        // --- Apply to camera in world space ---
        Vector3 eye = motor.transform.position + Vector3.up * motor.EyeHeight;
        Vector3 offset = springPos + new Vector3(bobX, bobY, 0f);
        transform.position = eye + yawRot * offset;
        transform.rotation = Quaternion.Euler(pitch + breath * breathPitch * fatigue, yaw, roll);
    }
}