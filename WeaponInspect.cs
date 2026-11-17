using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Weapon inspect mode. This is the first half of the manual reload: you look the gun over, then click
/// the parts you want to work on (magazine, chamber, ...). Works only for items whose category is Weapon.
///
///   Hold R (0.25 s)   enter inspect mode: the gun swings up to Pose A in front of the camera
///   Tap R             while inspecting: flip the gun to the other pose (A / B), with a little toss
///   Hold R            while inspecting: leave inspect mode, the gun swings back into your hands
///   Esc               leave inspect mode and leave the cursor free (menu)
///
/// While inspecting:
///   - the cursor is free (confined to the window) and the camera still follows the mouse, but multiplied
///     by "Inspect Look Multiplier" so it doesn't fly around while you aim the cursor at the gun
///   - the gun is moved by springs, not an animation: heavier guns take longer, overshoot a touch and settle
///   - the gun is NOT attached to the camera. It hangs in the air in front of your body (at your eye point,
///     facing where you face) and ignores looking up or down, so you can look down at your plate carrier while
///     it stays put. It still has inertia: it lags when you start/stop walking and when you turn, dips when you
///     land, drops with you when you crouch, and pulls back from walls.
///   - walking is slower and sprinting is off; jumping drops you out of inspect mode
///   - InspectHotspot components on the weapon prefab become clickable. Subscribe to their events (or use
///     the UnityEvents) to build "click the mag to pull it out" and similar.
///   - SAFE ZONES: while the player stands in a SafeZone, holding right mouse turns the gun freely (trackball
///     style, with weight and inertia) around its balance point. The camera is frozen while you do it. Tapping R
///     (flip) or leaving inspect mode eases the gun back to its normal orientation. Outside a safe zone the
///     gun stays on its pose and right mouse does nothing.
///   - Attachments: AttachmentSlot components create their own InspectHotspots, and AttachmentInspectUI
///     (on the player) uses this class to let you add, remove and slide attachments (safe zone only).
///
/// Put this on the Player root next to HeldItemSlot and WeaponADS.
/// Per-weapon poses: add WeaponInspectPoses to the weapon prefab. Without it, the default poses below are used.
/// </summary>
[DefaultExecutionOrder(60)]
public class WeaponInspect : MonoBehaviour
{
    [Header("References (found automatically if empty)")]
    public MouseLook look;
    public PlayerMotor motor;
    public HeldItemSlot slot;

    [Header("Input")]
    [Tooltip("Hold R this long (seconds) to enter or leave inspect mode. Releasing sooner counts as a tap.")]
    public float holdTime = 0.25f;

    [Header("Camera While Inspecting")]
    [Tooltip("Multiplies mouse look while the cursor is free. 1 = normal speed, 0 = camera frozen.")]
    [Range(0f, 1f)] public float inspectLookMultiplier = 0.25f;

    [Header("Anchoring")]
    [Tooltip("Off (default): the gun floats in front of your body and ignores looking up/down, so you can look at your plate carrier. On: it stays glued to the camera like before.")]
    public bool attachToCamera = false;
    [Tooltip("Seconds of lag behind turning. 0 = the gun turns instantly with you. 0.06 at 180 deg/s = about 10 degrees of trailing.")]
    public float turnLag = 0.06f;
    [Tooltip("Downward kick of the gun (m/s per m/s of impact) when you land.")]
    public float landingKick = 0.04f;
    [Tooltip("Pull the gun back toward you when a wall is in front of you.")]
    public bool retractFromWalls = true;
    public float maxWallRetract = 0.35f;

    [Header("Default Poses (relative to your eye point, facing where your body faces; used when the weapon has no WeaponInspectPoses)")]
    [Tooltip("The weapon's pivot (where the hand grips it) ends up here. +X right, +Y up (negative = below your eyes), +Z forward, in metres. Looking up or down does not move it.")]
    public Vector3 defaultPositionA = new Vector3(0f, -0.16f, 0.46f);
    [Tooltip("Rotation in degrees. Yaw about -68 turns the gun's left side toward you.")]
    public Vector3 defaultEulerA = new Vector3(12f, -68f, 8f);
    public Vector3 defaultPositionB = new Vector3(0f, -0.16f, 0.46f);
    [Tooltip("Yaw about +68 turns the gun's right side toward the camera.")]
    public Vector3 defaultEulerB = new Vector3(12f, 68f, -8f);

    [Header("Motion (spring-driven)")]
    [Tooltip("Seconds to swing to a pose for a light weapon. Heavier weapons take longer.")]
    public float transitionTime = 0.45f;
    [Tooltip("1 = no overshoot. Lower = the gun overshoots slightly and settles.")]
    [Range(0.3f, 1f)] public float dampingRatio = 0.65f;
    [Tooltip("Extra transition time per kg above 2.5 kg (0.12 = a 6.5 kg gun takes ~50% longer).")]
    public float heavySlowdownPerKg = 0.12f;
    [Tooltip("Upward toss (m/s) when you tap R to flip the gun. Divided down for heavy guns.")]
    public float flipLift = 0.35f;
    [Tooltip("How much the gun lags behind your body acceleration while inspecting.")]
    public float accelerationSway = 0.5f;

    [Header("Movement While Inspecting")]
    [Tooltip("Walking speed multiplier. Sprinting is not possible.")]
    [Range(0.2f, 1f)] public float moveSpeedMultiplier = 0.5f;
    [Tooltip("Jumping drops the gun back into your hands.")]
    public bool exitOnJump = true;

    [Header("Walk Bob (while inspecting)")]
    [Tooltip("Turn the bob off completely.")]
    public bool bobEnabled = true;
    [Tooltip("Overall strength of the bob. 1 = as set below, 0 = none, 2 = double.")]
    [Min(0f)] public float bobMultiplier = 1f;
    [Tooltip("Metres the gun dips with each footstep.")]
    public float bobHeight = 0.012f;
    [Tooltip("Metres the gun sways left and right, one cycle per two steps.")]
    public float bobSway = 0.01f;
    [Tooltip("Degrees the gun rolls with the sway.")]
    public float bobRoll = 1.5f;
    [Tooltip("Degrees the gun nods forward and back with each step.")]
    public float bobPitch = 0.8f;
    [Tooltip("How fast the bob fades in when you start walking and out when you stop (higher = quicker).")]
    public float bobBlendSpeed = 4f;
    [Tooltip("How much faster walking makes the bob bigger. 0 = same size at any speed, 1 = full scaling with speed.")]
    [Range(0f, 1f)] public float bobSpeedScaling = 0.6f;

    [Header("Restrictions")]
    [Tooltip("Can't start inspecting with a wall closer than this in front of you (metres).")]
    public float minWallDistance = 0.9f;
    public float wallBlockPush = 0.1f;

    [Header("Hotspots")]
    public LayerMask hotspotMask = ~0;
    [Tooltip("How far (metres) the cursor ray reaches.")]
    public float hotspotRange = 3f;

    [Header("Safe-Zone Rotation (hold right mouse)")]
    [Tooltip("On: the gun can only be turned while standing inside a SafeZone.")]
    public bool requireSafeZoneToRotate = true;
    [Tooltip("Degrees the gun turns per mouse count (before the weight feel below).")]
    public float degreesPerCount = 0.35f;
    [Tooltip("How quickly the spin dies after you let go. Higher = stops sooner. Heavier guns stop a little sooner.")]
    public float spinDamping = 4f;
    public float maxSpinSpeed = 900f;
    [Tooltip("Weapon weight (kg) that feels neutral. Heavier guns are sluggish to turn, lighter ones are snappy.")]
    public float referenceWeightKg = 3.5f;
    [Tooltip("Seconds to ease back to the normal orientation after a flip or when leaving inspect mode.")]
    public float rotationReturnTime = 0.35f;
    [Tooltip("The point the gun spins around (weapon space, relative to the object's origin). 0,0,0 = the origin. " +
             "Overridden per weapon by WeaponInspectPoses.")]
    public Vector3 defaultRotationCenter = Vector3.zero;
    [Tooltip("While you turn the gun, it glides to the middle of the screen so it spins around that point.")]
    public bool centerGunWhileRotating = true;
    [Tooltip("Metres in front of your eye the spin point sits when centred (long guns are pushed out a bit further).")]
    public float rotateViewDistance = 0.45f;
    [Tooltip("How fast the gun glides to / from the screen centre.")]
    public float centerSpeed = 8f;

    // ---- Public state / events ----
    public bool IsInspecting => phase != Phase.None;
    /// <summary>True while the player is spinning the gun with right mouse (the camera is frozen meanwhile).</summary>
    public bool IsRotating => rotating;
    /// <summary>True when right mouse would turn the gun right now (inspecting, and in a safe zone if required).</summary>
    public bool CanRotate => phase == Phase.Active && (!requireSafeZoneToRotate || SafeZone.PlayerInSafeZone);
    /// <summary>Set by UI that sits over the gun (menus) so the cursor stops hovering/pressing hotspots behind it.</summary>
    public bool PointerBlocked { get; set; }
    /// <summary>The held weapon while inspecting (null otherwise).</summary>
    public Pickupable Item => phase != Phase.None ? item : null;
    /// <summary>True while the gun is swinging back to the hands.</summary>
    public bool IsExiting => phase == Phase.Exiting;
    /// <summary>0 = Pose A, 1 = Pose B.</summary>
    public int PoseIndex => poseIndex;
    /// <summary>The hotspot under the cursor (null if none).</summary>
    public InspectHotspot HoveredHotspot => hovered;
    /// <summary>Ray from the camera through the cursor (for drag logic in your own scripts).</summary>
    public Ray PointerRay => cam != null ? cam.ScreenPointToRay(GameInput.PointerPosition()) : new Ray();

    public event Action Entered;
    public event Action Exited;
    public event Action<int> PoseChanged;
    public event Action<bool> RotatingChanged;

    enum Phase { None, Active, Exiting }
    Phase phase = Phase.None;

    Camera cam;
    Pickupable item;
    WeaponInspectPoses customPoses;
    WeaponAttachments attachments;

    // Free rotation (safe zone). userRot is applied on top of the pose, in frame space, about rotationCenter.
    Quaternion userRot = Quaternion.identity;
    Vector3 spin;                 // frame-space angular velocity, degrees/second
    Vector3 rotationCenter;
    bool rotating, returningToRest;
    float centerBlend;            // 0 = on its normal pose, 1 = pivot at the middle of the screen

    Vector3 pos, posVel, angVel;
    Quaternion rot = Quaternion.identity;
    int poseIndex;
    float exitTimer, bobBlend, frameYaw, frameYawVel, wallPush;

    bool rDown, rConsumed;
    float rTime;

    InspectHotspot hovered, pressed;
    readonly List<Collider> hotspotColliders = new List<Collider>();
    readonly RaycastHit[] hits = new RaycastHit[16];
    readonly RaycastHit[] wallHits = new RaycastHit[16];

    void Awake()
    {
        if (motor == null) motor = GetComponent<PlayerMotor>();
        if (slot == null) slot = GetComponent<HeldItemSlot>();
        if (look == null && slot != null) look = slot.look;
        if (look == null) look = GetComponentInChildren<MouseLook>();
        if (look != null)
        {
            cam = look.GetComponent<Camera>();
            if (cam == null) cam = look.GetComponentInChildren<Camera>();
        }
    }

    void OnEnable()
    {
        if (motor != null)
        {
            motor.Jumped += OnJumped;
            motor.Landed += OnLanded;
        }
    }

    void OnDisable()
    {
        if (motor != null)
        {
            motor.Jumped -= OnJumped;
            motor.Landed -= OnLanded;
        }
        if (phase != Phase.None && slot != null && motor != null) Abort();
    }

    void OnJumped()
    {
        if (exitOnJump && phase == Phase.Active) BeginExit(true);
    }

    void OnLanded(float impact)
    {
        if (phase != Phase.None) posVel.y -= impact * landingKick;
    }

    /// <summary>
    /// The frame the gun is simulated in. Default: your eye point (it follows crouching, ignores head bob)
    /// turned to face the same way as your view, but never tilted, so looking down doesn't move the gun.
    /// With "Attach To Camera" it is the camera itself.
    /// </summary>
    void GetFrame(out Vector3 origin, out Quaternion rotation)
    {
        if (attachToCamera)
        {
            Transform camT = look.transform;
            origin = camT.position;
            rotation = camT.rotation;
        }
        else
        {
            origin = motor.transform.position + Vector3.up * motor.EyeHeight;
            rotation = Quaternion.Euler(0f, frameYaw, 0f);
        }
    }

    // ================================================================== input

    void Update()
    {
        if (slot == null || motor == null || look == null) return;

        HandleReloadKey();

        if (phase == Phase.Active)
        {
            if (GameInput.EscapePressed())
            {
                BeginExit(false);
                return;
            }

            // Right mouse spins the gun (safe zone only). The camera is frozen while it does.
            SetRotating(CanRotate && GameInput.AimHeld());
            look.LookMultiplier = rotating ? 0f : inspectLookMultiplier;

            UpdatePointer();
        }
    }

    void SetRotating(bool value)
    {
        if (rotating == value) return;
        rotating = value;
        if (value) returningToRest = false;
        RotatingChanged?.Invoke(value);
    }

    void ResetRotationState()
    {
        SetRotating(false);
        userRot = Quaternion.identity;
        spin = Vector3.zero;
        returningToRest = false;
        centerBlend = 0f;
        PointerBlocked = false;
    }

    void HandleReloadKey()
    {
        if (GameInput.ReloadPressed())
        {
            rDown = true;
            rConsumed = false;
            rTime = 0f;
        }
        if (!rDown) return;

        rTime += Time.unscaledDeltaTime;

        // Held long enough: that's the "hold" action (enter or leave), fired once, even if R is still down
        if (!rConsumed && rTime >= holdTime)
        {
            rConsumed = true;
            if (phase == Phase.None) TryEnter();
            else if (phase == Phase.Active) BeginExit(true);
        }

        if (!GameInput.ReloadHeld())
        {
            // Released before the hold time: it was a tap
            if (!rConsumed && phase == Phase.Active) FlipPose();
            rDown = false;
        }
    }

    // ================================================================== enter / exit

    bool CanEnter()
    {
        if (phase != Phase.None) return false;
        if (!slot.HasItem || slot.IsEquipping || slot.ExternalControl) return false;
        if (slot.HeldData == null || slot.HeldData.category != ItemCategory.Weapon) return false;
        if (!look.LookEnabled || Cursor.lockState != CursorLockMode.Locked) return false;
        if (slot.IsThrowCharging || slot.WallPushAmount > wallBlockPush) return false;
        if (motor.IsSprinting) return false;

        Transform camT = look.transform;
        int mask = ~(1 << gameObject.layer);
        if (Physics.SphereCast(camT.position, 0.12f, camT.forward, out _, minWallDistance, mask,
                QueryTriggerInteraction.Ignore))
            return false;

        return true;
    }

    void TryEnter()
    {
        if (!CanEnter()) return;

        item = slot.Current;
        customPoses = item.GetComponent<WeaponInspectPoses>();
        attachments = item.GetComponent<WeaponAttachments>();
        rotationCenter = customPoses != null ? customPoses.rotationOffset : defaultRotationCenter;
        ResetRotationState();

        // The simulation frame must face where you look BEFORE we measure the gun in it. (It used to be set after,
        // so the first inspect measured the gun in a stale frame and it swept through your body to its pose.)
        frameYaw = look.Yaw;
        frameYawVel = 0f;

        // Start from wherever the gun is right now (hip, aimed, ...) so the swing-up is continuous
        GetFrame(out Vector3 frameOrigin, out Quaternion frameRot);
        Quaternion frameInv = Quaternion.Inverse(frameRot);
        pos = frameInv * (item.transform.position - frameOrigin);
        rot = frameInv * item.transform.rotation;
        posVel = Vector3.zero;
        angVel = Vector3.zero;
        poseIndex = 0;
        bobBlend = 0f;
        wallPush = 0f;

        slot.ExternalControl = true;
        EnableHotspotColliders();

        look.FreeCursorMode = true;
        look.LookMultiplier = inspectLookMultiplier;
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = true;

        phase = Phase.Active;
        Entered?.Invoke();
    }

    void BeginExit(bool relockCursor)
    {
        if (phase != Phase.Active) return;

        phase = Phase.Exiting;
        exitTimer = 0f;

        SetRotating(false);
        returningToRest = true;
        PointerBlocked = false;
        ClearPointer();
        DisableHotspotColliders();

        look.FreeCursorMode = false;
        look.LookMultiplier = 1f;
        if (relockCursor)
        {
            look.SetCursorLocked(true);
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void Finish()
    {
        phase = Phase.None;
        slot.ExternalControl = false;
        motor.SprintBlocked = false;
        motor.SpeedMultiplier = 1f;
        item = null;
        attachments = null;
        ResetRotationState();
        Exited?.Invoke();
    }

    /// <summary>The held item vanished or changed (dropped, swapped, loaded a save): drop out of inspect mode at once.</summary>
    void Abort()
    {
        ClearPointer();
        if (item != null && item.IsHeld) DisableHotspotColliders();
        else hotspotColliders.Clear();

        if (look != null)
        {
            look.FreeCursorMode = false;
            look.LookMultiplier = 1f;
            if (Cursor.lockState == CursorLockMode.Confined) look.SetCursorLocked(true);
        }

        slot.ExternalControl = false;
        motor.SprintBlocked = false;
        motor.SpeedMultiplier = 1f;
        phase = Phase.None;
        item = null;
        attachments = null;
        ResetRotationState();
        Exited?.Invoke();
    }

    void FlipPose()
    {
        poseIndex = 1 - poseIndex;
        returningToRest = true; // the gun un-twists as it flips to the other side
        float lift = flipLift / (1f + Mathf.Max(0f, item.MassKg - 2.5f) * heavySlowdownPerKg);
        posVel += Vector3.up * lift;
        PoseChanged?.Invoke(poseIndex);
    }

    void GetPose(int index, out Vector3 position, out Quaternion rotation)
    {
        Vector3 p, e;
        if (customPoses != null)
        {
            p = index == 0 ? customPoses.positionA : customPoses.positionB;
            e = index == 0 ? customPoses.eulerA : customPoses.eulerB;
        }
        else
        {
            p = index == 0 ? defaultPositionA : defaultPositionB;
            e = index == 0 ? defaultEulerA : defaultEulerB;
        }
        position = p;
        rotation = Quaternion.Euler(e);
    }

    // ================================================================== hotspots / cursor

    void EnableHotspotColliders()
    {
        hotspotColliders.Clear();
        foreach (InspectHotspot hs in item.GetComponentsInChildren<InspectHotspot>(true))
            foreach (Collider col in hs.GetComponentsInChildren<Collider>(true))
                hotspotColliders.Add(col);

        // Pickupable switches every collider off while the item is held; hotspots need theirs for the cursor ray.
        foreach (Collider col in hotspotColliders)
            if (col != null) col.enabled = true;
    }

    void DisableHotspotColliders()
    {
        foreach (Collider col in hotspotColliders)
            if (col != null) col.enabled = false;
        hotspotColliders.Clear();
    }

    void UpdatePointer()
    {
        // No hovering or clicking hotspots while spinning the gun or while a menu is over it
        InspectHotspot over = (rotating || PointerBlocked) ? null : FindHotspot();
        if (over != hovered)
        {
            if (hovered != null) hovered.NotifyHover(false);
            hovered = over;
            if (hovered != null) hovered.NotifyHover(true);
        }

        if (GameInput.ClickPressed() && hovered != null && pressed == null)
        {
            pressed = hovered;
            pressed.NotifyPressed(true);
        }

        if (pressed != null && !GameInput.ClickHeld())
        {
            pressed.NotifyPressed(false);
            pressed = null;
        }
    }

    InspectHotspot FindHotspot()
    {
        if (cam == null || item == null) return null;

        Physics.SyncTransforms(); // the gun was moved this frame; make the colliders catch up before raycasting
        Ray ray = cam.ScreenPointToRay(GameInput.PointerPosition());
        int count = Physics.RaycastNonAlloc(ray, hits, hotspotRange, hotspotMask, QueryTriggerInteraction.Collide);

        InspectHotspot best = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].distance >= bestDistance) continue;
            InspectHotspot hs = hits[i].collider.GetComponentInParent<InspectHotspot>();
            if (hs == null || !hs.transform.IsChildOf(item.transform)) continue;
            best = hs;
            bestDistance = hits[i].distance;
        }
        return best;
    }

    void ClearPointer()
    {
        if (hovered != null) { hovered.NotifyHover(false); hovered = null; }
        if (pressed != null) { pressed.NotifyPressed(false); pressed = null; }
    }

    // ================================================================== gun motion

    /// <summary>Footstep bob in camera space, driven by the motor's step phase. Fades out while leaving
    /// inspect mode, because the hands' own bob takes over from there.</summary>
    void ComputeBob(float dt, out Vector3 offset, out Quaternion tilt)
    {
        bool walking = bobEnabled && phase == Phase.Active && motor.IsGrounded && motor.PlanarSpeed > 0.3f;
        bobBlend = Mathf.MoveTowards(bobBlend, walking ? 1f : 0f, dt * bobBlendSpeed);

        if (bobBlend <= 0f)
        {
            offset = Vector3.zero;
            tilt = Quaternion.identity;
            return;
        }

        float speed01 = Mathf.Clamp01(motor.PlanarSpeed / Mathf.Max(0.1f, motor.sprintSpeed));
        float strength = bobBlend * bobMultiplier * Mathf.Lerp(1f, 0.4f + speed01 * 0.6f, bobSpeedScaling);

        float cycle = motor.StepPhase - 0.08f; // a touch behind the camera's own bob, so the gun feels like it has weight
        float parity = motor.RightFootNext ? 1f : 0f;
        float dip = -Mathf.Cos(cycle * Mathf.PI * 2f);        // lowest at foot strike
        float swing = Mathf.Sin((cycle + parity) * Mathf.PI); // one cycle per two steps

        offset = new Vector3(swing * bobSway, dip * bobHeight, 0f) * strength;
        tilt = Quaternion.Euler(dip * bobPitch * strength, 0f, swing * bobRoll * strength);
    }

    void LateUpdate()
    {
        if (phase == Phase.None) return;

        if (item == null || slot.Current != item)
        {
            Abort();
            return;
        }

        float rawDt = Time.deltaTime;
        if (rawDt <= 0f) return;
        float dt = Mathf.Min(rawDt, 0.033f);
        Transform camT = look.transform;

        // Slower walking, no sprinting, for as long as the gun is away from the ready position
        motor.SprintBlocked = true;
        motor.SpeedMultiplier = moveSpeedMultiplier;

        // The simulation frame trails your turning a little, so the gun swings out behind a fast turn
        frameYaw = turnLag > 0.001f
            ? Mathf.SmoothDampAngle(frameYaw, look.Yaw, ref frameYawVel, turnLag)
            : look.Yaw;
        GetFrame(out Vector3 frameOrigin, out Quaternion frameRot);
        Quaternion frameInv = Quaternion.Inverse(frameRot);

        // Where the gun is heading, in frame space
        Vector3 targetPos;
        Quaternion targetRot;
        if (phase == Phase.Active)
        {
            GetPose(poseIndex, out targetPos, out targetRot);

            // Pull back from a wall in front of you (looking up/down doesn't matter, only where your body faces)
            float pushTarget = 0f;
            if (retractFromWalls)
            {
                float reach = Mathf.Max(0.1f, targetPos.z + item.data.lengthMeters * 0.5f);
                Vector3 fwd = frameRot * Vector3.forward;
                // The nearest solid thing ahead pulls the gun back. A magazine that just dropped out of the gun is not a wall.
                int wallCount = Physics.SphereCastNonAlloc(frameOrigin, 0.08f, fwd, wallHits, reach,
                                                           ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore);
                float nearestWall = float.MaxValue;
                for (int wi = 0; wi < wallCount; wi++)
                {
                    if (wallHits[wi].distance >= nearestWall) continue;
                    if (wallHits[wi].collider.GetComponentInParent<MagazineItem>() != null) continue;
                    nearestWall = wallHits[wi].distance;
                }
                if (nearestWall < float.MaxValue)
                    pushTarget = Mathf.Clamp(reach - nearestWall, 0f, maxWallRetract);
            }
            wallPush += (pushTarget - wallPush) * (1f - Mathf.Exp(-12f * dt));
            targetPos.z -= wallPush;
        }
        else
        {
            // Leaving: head for wherever the hands would hold it right now (that pose is in camera space)
            Vector3 handWorld = camT.TransformPoint(slot.PoseLocalPos);
            Quaternion handWorldRot = camT.rotation * slot.PoseLocalRot;
            targetPos = frameInv * (handWorld - frameOrigin);
            targetRot = frameInv * handWorldRot;
        }

        float time = transitionTime * Mathf.Min(2f, 1f + Mathf.Max(0f, item.MassKg - 2.5f) * heavySlowdownPerKg);
        if (phase == Phase.Exiting) time *= 0.8f;
        float w = 4.5f / Mathf.Max(0.05f, time);
        float stiffness = w * w;
        float damp = 2f * dampingRatio * w;

        // --- Position spring (plus lag behind the body's acceleration) ---
        Vector3 accel = motor.LocalAcceleration;
        Vector3 ext = new Vector3(-accel.x, 0f, -accel.z) * accelerationSway;
        posVel += (stiffness * (targetPos - pos) - damp * posVel + ext) * dt;
        pos += posVel * dt;

        // --- Rotation spring (on the shortest rotation to the target) ---
        if (Quaternion.Dot(rot, targetRot) < 0f)
            targetRot = new Quaternion(-targetRot.x, -targetRot.y, -targetRot.z, -targetRot.w);
        Quaternion delta = targetRot * Quaternion.Inverse(rot);
        delta.ToAngleAxis(out float angleDeg, out Vector3 axis);
        if (angleDeg > 180f) angleDeg -= 360f;
        bool badAxis = float.IsInfinity(axis.x) || float.IsNaN(axis.x);
        Vector3 err = badAxis ? Vector3.zero : axis * (angleDeg * Mathf.Deg2Rad);

        angVel += (stiffness * err - damp * angVel) * dt;
        float step = angVel.magnitude * dt;
        if (step > 1e-6f)
            rot = Quaternion.Normalize(Quaternion.AngleAxis(step * Mathf.Rad2Deg, angVel.normalized) * rot);

        // --- Free rotation from the player (safe zone). Spins about the object's origin (plus rotationCenter),
        //     and that point glides to the middle of the screen while the gun is turned.
        UpdateUserRotation(rawDt, frameInv, camT);

        bool turned = rotating || spin.sqrMagnitude > 0f || userRot != Quaternion.identity;
        bool wantCenter = centerGunWhileRotating && CanRotate && !returningToRest && turned;
        centerBlend += ((wantCenter ? 1f : 0f) - centerBlend) * (1f - Mathf.Exp(-centerSpeed * dt));
        if (!wantCenter && centerBlend < 0.001f) centerBlend = 0f;

        Vector3 centerInFrame = rot * rotationCenter;
        float viewDistance = Mathf.Max(rotateViewDistance, item.data.lengthMeters * 0.55f);
        Vector3 viewCenter = (frameInv * camT.forward) * viewDistance;       // straight ahead of the eye point
        Vector3 pivot = Vector3.Lerp(pos + centerInFrame, viewCenter, centerBlend);
        Vector3 spunPos = pivot - userRot * centerInFrame;

        // Walk bob sits on top of the springs, so it never fights the swing between poses
        ComputeBob(dt, out Vector3 bobOffset, out Quaternion bobTilt);
        item.transform.SetPositionAndRotation(frameOrigin + frameRot * (spunPos + bobOffset),
            frameRot * (bobTilt * userRot * rot));

        // --- Leaving: hand the item back to HeldItemSlot once it has arrived ---
        if (phase == Phase.Exiting)
        {
            exitTimer += rawDt;
            bool arrived = (pos - targetPos).sqrMagnitude < 0.00004f && Quaternion.Angle(rot, targetRot) < 1.5f
                           && Quaternion.Angle(userRot, Quaternion.identity) < 1.5f;
            if (arrived || exitTimer > 2f) Finish();
        }
    }

    /// <summary>
    /// Right-mouse trackball: dragging turns the gun about the camera's up/right axes, so the side of the gun
    /// facing you follows the cursor. Heavier guns turn less per drag and stop sooner after release.
    /// The weight is the whole loadout (WeaponAttachments.TotalWeightKg), so a gun with a big optic feels heavier.
    /// </summary>
    void UpdateUserRotation(float dt, Quaternion frameInv, Transform camT)
    {
        float mass = attachments != null ? attachments.TotalWeightKg : item.MassKg;
        float massRatio = Mathf.Max(0.1f, mass) / Mathf.Max(0.1f, referenceWeightKg);

        if (rotating)
        {
            float feel = Mathf.Clamp(1f / Mathf.Sqrt(massRatio), 0.6f, 1.3f);
            Vector2 d = GameInput.LookDelta() * degreesPerCount * feel;

            // Drag right: near side goes right. Drag up: near side goes up. (camera axes, expressed in frame space)
            Quaternion toFrame = frameInv * camT.rotation;
            Vector3 degreesThisFrame = toFrame * (Vector3.up * -d.x + Vector3.right * d.y);
            Vector3 target = degreesThisFrame / Mathf.Max(dt, 0.0001f);

            spin = Vector3.Lerp(spin, target, 1f - Mathf.Exp(-25f * dt));
            spin = Vector3.ClampMagnitude(spin, maxSpinSpeed);
        }
        else
        {
            spin *= Mathf.Exp(-spinDamping * Mathf.Clamp(massRatio, 0.7f, 1.6f) * dt);
            if (spin.sqrMagnitude < 1f) spin = Vector3.zero;
        }

        if (spin.sqrMagnitude > 0f)
            userRot = Quaternion.Normalize(Quaternion.AngleAxis(spin.magnitude * dt, spin.normalized) * userRot);

        // Ease back to the normal orientation after a flip, when leaving, or if the safe zone is gone
        bool shouldReturn = returningToRest || phase == Phase.Exiting || !CanRotate;
        if (!rotating && shouldReturn && userRot != Quaternion.identity)
        {
            float t = 1f - Mathf.Exp(-4f * dt / Mathf.Max(0.05f, rotationReturnTime));
            userRot = Quaternion.Slerp(userRot, Quaternion.identity, t);
            spin *= 1f - t;
            if (Quaternion.Angle(userRot, Quaternion.identity) < 0.1f)
            {
                userRot = Quaternion.identity;
                spin = Vector3.zero;
                returningToRest = false;
            }
        }
        else if (!rotating && shouldReturn)
        {
            returningToRest = false;
        }
    }
}