using System;
using UnityEngine;
#if UNITY_6000_0_OR_NEWER
using PhysMat = UnityEngine.PhysicsMaterial;
using PhysCombine = UnityEngine.PhysicsMaterialCombine;
#else
using PhysMat = UnityEngine.PhysicMaterial;
using PhysCombine = UnityEngine.PhysicMaterialCombine;
#endif

/// <summary>
/// Force-driven first-person motor. The player is a real Rigidbody with real mass:
/// legs can only push with a limited force (F = m * a), so acceleration, braking,
/// air control, jumps and landings all follow from physics instead of snapping
/// to a target velocity. Heavier loadout = slower to start, stop and jump.
///
/// Setup: Player root pivot at the FEET. Needs Rigidbody + CapsuleCollider (auto-configured).
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class PlayerMotor : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The MouseLook on your camera. Movement is relative to its yaw.")]
    public MouseLook look;
    [Tooltip("Optional. Found automatically if it's on the same object.")]
    public PlayerStamina stamina;

    [Header("Body")]
    public float bodyMassKg = 80f;
    [Tooltip("Gear, armor, weapon, plates... Change at runtime from your inventory system.")]
    public float carriedLoadKg = 0f;
    public float standingHeight = 1.8f;
    public float crouchHeight = 1.2f;
    public float radius = 0.3f;
    public float eyeOffsetFromTop = 0.12f;
    public float crouchTransitionTime = 0.22f;

    [Header("Target Speeds (m/s)")]
    public float walkSpeed = 1.9f;
    public float sprintSpeed = 5.0f;
    public float crouchSpeed = 1.1f;
    [Range(0.3f, 1f)] public float backwardSpeedMultiplier = 0.7f;
    [Range(0.3f, 1f)] public float strafeSpeedMultiplier = 0.85f;

    [Header("Leg Force (Newtons)")]
    [Tooltip("Max push force when speeding up. Acceleration = force / total mass (480N / 80kg = 6 m/s^2).")]
    public float driveForce = 480f;
    [Tooltip("Max force when slowing down or reversing. Humans brake harder than they accelerate.")]
    public float brakeForce = 800f;
    [Tooltip("Steering force in the air. Very low: you can't push against nothing.")]
    public float airForce = 90f;

    [Header("Ground / Slopes")]
    public LayerMask groundMask = ~0;
    public float slopeLimit = 45f;
    public float groundCheckDistance = 0.1f;
    [Tooltip("Uphill slows you, downhill speeds you up slightly.")]
    public float slopeSpeedEffect = 0.7f;

    [Header("Jumping")]
    public float jumpHeight = 0.5f;
    public float jumpBufferTime = 0.12f;
    public float coyoteTime = 0.1f;
    public float jumpCooldown = 0.45f;

    [Header("Landing")]
    [Tooltip("Impact speed (m/s) where you start losing control. 2.5 m/s is about a 0.3 m drop.")]
    public float softLandSpeed = 2.5f;
    [Tooltip("Impact speed (m/s) for a full stumble. 7 m/s is about a 2.5 m drop.")]
    public float hardLandSpeed = 7f;
    public float maxRecoveryTime = 0.8f;

    // ---- Public read-only state (camera, audio, animation, UI can read these) ----
    public bool IsGrounded { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool IsCrouching => wantsCrouch;
    public float EyeHeight => currentHeight - eyeOffsetFromTop;
    public Vector3 PlanarVelocity { get; private set; }
    public float PlanarSpeed => PlanarVelocity.magnitude;
    /// <summary>Smoothed horizontal acceleration in yaw-local space (x = right, z = forward).</summary>
    public Vector3 LocalAcceleration { get; private set; }
    /// <summary>0..1 progress through the current footstep.</summary>
    public float StepPhase { get; private set; }
    /// <summary>Flips every footstep. Use for left/right sway or foot audio.</summary>
    public bool RightFootNext { get; private set; }

    public event Action<float> Landed;       // impact speed in m/s (hook fall damage here)
    public event Action Jumped;
    public event Action<bool> Footstep;      // true = right foot

    Rigidbody rb;
    CapsuleCollider capsule;
    int effectiveMask;

    bool wantsCrouch;
    float currentHeight, heightVel;
    float jumpBuffer, coyoteTimer, nextJumpTime, noGroundUntil;
    float lastVy;
    float recoveryTimer, recoveryMult = 1f;
    Vector3 groundNormal = Vector3.up;
    Vector3 lastPlanarVel, accelSmoothed;

    float TotalMass => bodyMassKg + carriedLoadKg;

    Vector3 Velocity
    {
        get
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }
        set
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = value;
#else
            rb.velocity = value;
#endif
        }
    }

    float Yaw => look != null ? look.Yaw : transform.eulerAngles.y;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        if (stamina == null) stamina = GetComponent<PlayerStamina>();

        rb.useGravity = false; // we apply gravity ourselves so slopes don't make you slide
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.mass = TotalMass;

        capsule.radius = radius;
        capsule.direction = 1;
        currentHeight = standingHeight;
        ApplyHeight();

        // Zero friction: our leg forces handle grip, so the capsule never sticks to walls.
        PhysMat mat = new PhysMat("PlayerNoFriction")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            frictionCombine = PhysCombine.Minimum,
            bounceCombine = PhysCombine.Minimum
        };
        capsule.sharedMaterial = mat;

        effectiveMask = groundMask.value & ~(1 << gameObject.layer);
    }

    void Update()
    {
        if (GameInput.JumpPressed()) jumpBuffer = jumpBufferTime;

        if (GameInput.CrouchPressed())
        {
            if (!wantsCrouch) wantsCrouch = true;
            else if (CanStand()) wantsCrouch = false;
        }
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        rb.mass = TotalMass;

        Vector3 vel = Velocity;
        bool wasGrounded = IsGrounded;

        CheckGround();

        if (IsGrounded && !wasGrounded)
            OnLand(Mathf.Max(0f, -lastVy));
        lastVy = vel.y;

        if (recoveryTimer > 0f)
        {
            recoveryTimer -= dt;
            if (recoveryTimer <= 0f) recoveryMult = 1f;
        }

        Vector2 input = GameInput.Move();
        bool sprintHeld = GameInput.SprintHeld();

        // Sprinting stands you up (if there's headroom).
        if (sprintHeld && wantsCrouch && input.y > 0.1f && CanStand()) wantsCrouch = false;
        bool hasStamina = stamina == null || stamina.CanSprint;
        IsSprinting = sprintHeld && input.y > 0.1f && !wantsCrouch && hasStamina;

        UpdateHeight();
        HandleJump(dt);
        ApplyMovementForces(input, dt);
        ApplyGravity();
        RotateBody(dt);
        UpdateStride(dt);
        TrackAcceleration(dt);
    }

    // ------------------------------------------------------------------ ground

    void CheckGround()
    {
        IsGrounded = false;
        groundNormal = Vector3.up;
        if (Time.time < noGroundUntil) return;

        Vector3 origin = transform.position + Vector3.up * (radius + 0.1f);
        if (Physics.SphereCast(origin, radius * 0.95f, Vector3.down, out RaycastHit hit,
                0.1f + groundCheckDistance, effectiveMask, QueryTriggerInteraction.Ignore))
        {
            if (Vector3.Angle(hit.normal, Vector3.up) <= slopeLimit)
            {
                IsGrounded = true;
                groundNormal = hit.normal;
            }
        }
    }

    void OnLand(float impact)
    {
        if (impact < 0.5f) return;

        float severity = Mathf.InverseLerp(softLandSpeed, hardLandSpeed, impact);
        if (severity > 0f)
        {
            recoveryTimer = Mathf.Lerp(0.15f, maxRecoveryTime, severity);
            recoveryMult = Mathf.Lerp(0.85f, 0.3f, severity);

            // Bleed off horizontal momentum, like absorbing the landing with your legs.
            Vector3 v = Velocity;
            float keep = Mathf.Lerp(1f, 0.55f, severity);
            Velocity = new Vector3(v.x * keep, v.y, v.z * keep);
        }
        Landed?.Invoke(impact);
    }

    // ------------------------------------------------------------------ crouch

    bool CanStand()
    {
        float rise = standingHeight - crouchHeight;
        Vector3 origin = transform.position + Vector3.up * (crouchHeight - radius);
        return !Physics.SphereCast(origin, radius * 0.9f, Vector3.up, out _, rise + 0.02f,
            effectiveMask, QueryTriggerInteraction.Ignore);
    }

    void UpdateHeight()
    {
        float target = wantsCrouch ? crouchHeight : standingHeight;
        currentHeight = Mathf.SmoothDamp(currentHeight, target, ref heightVel, crouchTransitionTime);
        ApplyHeight();
    }

    void ApplyHeight()
    {
        capsule.height = Mathf.Max(currentHeight, radius * 2f);
        capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);
    }

    // ------------------------------------------------------------------ jump

    void HandleJump(float dt)
    {
        if (IsGrounded) coyoteTimer = coyoteTime; else coyoteTimer -= dt;
        if (jumpBuffer > 0f) jumpBuffer -= dt;

        if (jumpBuffer > 0f && coyoteTimer > 0f && !wantsCrouch && Time.time >= nextJumpTime
            && recoveryMult > 0.5f)
        {
            // Same push, more mass = lower jump (h ~ 1/m).
            float h = jumpHeight * (bodyMassKg / TotalMass);
            float v0 = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * h);

            Vector3 v = Velocity;
            v.y = v0;
            Velocity = v;

            jumpBuffer = 0f;
            coyoteTimer = 0f;
            nextJumpTime = Time.time + jumpCooldown;
            noGroundUntil = Time.time + 0.15f;
            IsGrounded = false;
            if (stamina != null) stamina.OnJump();
            Jumped?.Invoke();
        }
    }

    // ------------------------------------------------------------------ movement

    void ApplyMovementForces(Vector2 input, float dt)
    {
        Vector3 planeNormal = IsGrounded ? groundNormal : Vector3.up;
        Vector3 cur = Vector3.ProjectOnPlane(Velocity, planeNormal);

        Vector3 wish = Quaternion.Euler(0f, Yaw, 0f) * new Vector3(input.x, 0f, input.y);
        Vector3 dir = Vector3.ProjectOnPlane(wish, planeNormal);
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.zero;

        // Base speed by stance
        float speed = wantsCrouch ? crouchSpeed : (IsSprinting ? sprintSpeed : walkSpeed);

        // Walking backwards / sideways is slower than going forward
        float back = Mathf.Max(0f, -input.y);
        float side = Mathf.Abs(input.x);
        float dirMult = 1f - back * (1f - backwardSpeedMultiplier)
                           - side * (1f - back) * (1f - strafeSpeedMultiplier);

        // Heavy loadout slows you down
        float loadMult = Mathf.Clamp(1f - carriedLoadKg * 0.006f, 0.6f, 1f);

        // Uphill slows you, downhill helps a bit
        float slopeMult = IsGrounded
            ? Mathf.Clamp(1f - dir.y * slopeSpeedEffect, 0.5f, 1.2f)
            : 1f;

        speed *= dirMult * loadMult * slopeMult * recoveryMult;
        Vector3 desired = dir * speed * input.magnitude;

        float mass = rb.mass;

        if (IsGrounded)
        {
            bool braking = input.sqrMagnitude < 0.0001f || desired.sqrMagnitude < cur.sqrMagnitude;
            float maxForce = (braking ? brakeForce : driveForce) * Mathf.Max(recoveryMult, 0.35f);

            Vector3 force = (desired - cur) * (mass / dt); // force that would hit the target exactly
            rb.AddForce(Vector3.ClampMagnitude(force, maxForce), ForceMode.Force);
        }
        else if (input.sqrMagnitude > 0.0001f)
        {
            // Air: weak steering, no braking. Momentum is conserved.
            Vector3 needed = desired - cur;
            if (cur.sqrMagnitude > desired.sqrMagnitude && cur.sqrMagnitude > 0.01f)
                needed = Vector3.ProjectOnPlane(needed, cur.normalized);

            Vector3 force = needed * (mass / dt);
            rb.AddForce(Vector3.ClampMagnitude(force, airForce), ForceMode.Force);
        }
    }

    void ApplyGravity()
    {
        float g = Physics.gravity.magnitude;
        if (IsGrounded)
            rb.AddForce(-groundNormal * g * rb.mass, ForceMode.Force); // pressed into slope, no sliding
        else
            rb.AddForce(Physics.gravity * rb.mass, ForceMode.Force);
    }

    void RotateBody(float dt)
    {
        Quaternion target = Quaternion.Euler(0f, Yaw, 0f);
        rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, target, 540f * dt));
    }

    // ------------------------------------------------------------------ feel data

    void UpdateStride(float dt)
    {
        Vector3 v = Velocity;
        PlanarVelocity = new Vector3(v.x, 0f, v.z);
        float speed = PlanarSpeed;

        if (!IsGrounded || speed < 0.3f) return;

        // Longer steps the faster you go (~0.7 m walking, ~1.3 m sprinting)
        float stepLength = Mathf.Lerp(0.7f, 1.3f, Mathf.InverseLerp(walkSpeed, sprintSpeed, speed));
        StepPhase += speed * dt / stepLength;
        if (StepPhase >= 1f)
        {
            StepPhase -= 1f;
            RightFootNext = !RightFootNext;
            Footstep?.Invoke(RightFootNext);
        }
    }

    void TrackAcceleration(float dt)
    {
        Vector3 dv = (PlanarVelocity - lastPlanarVel) / dt;
        lastPlanarVel = PlanarVelocity;
        accelSmoothed = Vector3.Lerp(accelSmoothed, dv, 0.2f);
        LocalAcceleration = Quaternion.Inverse(Quaternion.Euler(0f, Yaw, 0f)) * accelSmoothed;
    }
}
