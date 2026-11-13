using System;
using UnityEngine;

/// <summary>
/// The player's hands. Holds ONE item and remembers it.
///
/// - Starting item: drag an ItemData into "Starting Item" and the player begins the level holding it
///   (no pickup needed).
/// - Saving: CaptureSave() returns an ItemSaveData (item ID + optional item state).
///   Loading: RestoreSave(data) puts that item straight into the hands.
/// - Physics feel: the item hangs off the camera on spring-dampers. It lags behind head turns and body
///   acceleration, bobs with footsteps, dips on landings, lowers when sprinting, pulls back from walls
///   and sags/trembles when you're out of stamina. Heavier items have lazier springs and take longer to reach for.
///
/// Put this on the Player root (same object as PlayerMotor).
/// Runs after MouseLook so the item always follows the final camera pose.
/// </summary>
[DefaultExecutionOrder(50)]
public class HeldItemSlot : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The MouseLook on your camera. Found automatically if left empty.")]
    public MouseLook look;
    public PlayerMotor motor;
    public PlayerStamina stamina;
    [Tooltip("Needed to turn a saved item ID back into an item.")]
    public ItemDatabase database;

    [Header("Starting Item")]
    [Tooltip("Held from the first frame, no pickup needed. Empty = start empty-handed. Skipped if a save is restored.")]
    public ItemData startingItem;
    [Tooltip("Play the raise animation when an item appears from a save or the starting item.")]
    public bool drawOnRestore = true;

    [Header("Carry")]
    [Tooltip("Heaviest single item the player can lift.")]
    public float maxLiftKg = 25f;

    [Header("Reaching For An Item")]
    public float equipBaseTime = 0.3f;
    public float equipTimePerKg = 0.07f;
    public float equipMaxTime = 0.9f;
    [Tooltip("The item rises in a small arc on its way to the hand.")]
    public float equipArcHeight = 0.07f;
    public float drawTime = 0.45f;

    [Header("Inertia (spring-damper)")]
    public float stiffness = 140f;
    [Range(0.1f, 1f)] public float dampingRatio = 0.45f;
    [Tooltip("Effective mass = 1 + itemKg * this. Higher = heavier items feel lazier.")]
    public float massLag = 0.25f;
    [Tooltip("How much the item trails behind head turns.")]
    public float lookSway = 2f;
    [Tooltip("How much the item trails behind body acceleration.")]
    public float accelerationSway = 0.5f;
    public float maxPosOffset = 0.06f;
    public float maxRotOffset = 9f;
    public float landingKick = 0.04f;
    public float jumpKick = 0.05f;
    public float poseBlendSpeed = 7f;

    [Header("Footstep Bob")]
    public float bobHeight = 0.012f;
    public float bobSway = 0.01f;
    public float bobRoll = 1f;
    [Tooltip("Multiplier on the item's bob while walking.")]
    public float walkBobMultiplier = 1f;
    [Tooltip("Multiplier on the item's bob while sprinting. Blends in smoothly as you start/stop sprinting.")]
    public float sprintBobMultiplier = 1.8f;

    [Header("Tired Arms")]
    public float breathPitch = 0.6f;
    [Tooltip("Metres the item sags when stamina is gone (scaled by item weight).")]
    public float fatigueSag = 0.025f;

    [Header("Aiming")]
    [Tooltip("How much bob, sway and tired-arm motion is removed at full aim. Braced against the shoulder, the gun moves far less.")]
    [Range(0f, 1f)] public float aimMotionReduction = 0.85f;

    [Header("Wall Collision")]
    [Tooltip("Pull the item back and tilt it up when you walk into a wall.")]
    public bool retractFromWalls = true;
    public float maxRetract = 0.35f;

    [Header("Drop / Throw")]
    public float dropSpeed = 1.2f;
    public float swapDropSpeed = 0.6f;
    [Tooltip("Arm strength in N*s. Throw speed = impulse / mass, so a 5 kg item flies far slower than a 0.5 kg one.")]
    public float minThrowImpulse = 12f;
    public float maxThrowImpulse = 45f;
    public float maxThrowSpeed = 14f;
    public float throwStaminaPerKg = 1.5f;

    // ---- Public state ----
    public Pickupable Current { get; private set; }
    public bool HasItem => Current != null;
    public ItemData HeldData => Current != null ? Current.data : null;
    /// <summary>ID of what's in the hands ("" if nothing).</summary>
    public string HeldItemId => HeldData != null ? HeldData.id : string.Empty;
    public bool IsEquipping => state == State.Equipping;
    /// <summary>True while the player is winding up a throw.</summary>
    public bool IsThrowCharging => throwCharge > 0.02f;
    /// <summary>How far (metres) the item is currently pushed back by a wall in front of you.</summary>
    public float WallPushAmount => wallPush;
    /// <summary>Set true while another system (e.g. weapon inspect mode) positions the item itself.</summary>
    public bool ExternalControl { get; set; }

    public event Action<Pickupable> ItemEquipped;
    public event Action<Pickupable> ItemDropped;

    enum State { Empty, Equipping, Held }
    State state = State.Empty;

    Rigidbody playerRb;
    Collider[] playerColliders;
    int worldMask;

    float heldMass, addedLoad;
    float equipT, equipDuration;
    Vector3 equipStartPos;
    Quaternion equipStartRot;

    Vector3 posOff, posVel, rotOff, rotVel;
    Quaternion prevCamRot = Quaternion.identity;
    float sprintBlend, drawBlend, chargeBlend, throwCharge, wallPush, bobBlend, breathPhase;
    bool startingHandled;

    // Aim pose pushed in by WeaponADS
    float aimBlend;
    Vector3 aimPos, aimSway;
    Quaternion aimRot = Quaternion.identity;

    void Awake()
    {
        if (motor == null) motor = GetComponent<PlayerMotor>();
        if (stamina == null) stamina = GetComponent<PlayerStamina>();
        if (look == null && motor != null) look = motor.look;
        if (look == null) look = GetComponentInChildren<MouseLook>();

        playerRb = GetComponent<Rigidbody>();
        playerColliders = GetComponentsInChildren<Collider>();
        worldMask = ~(1 << gameObject.layer);
    }

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

    void Start()
    {
        if (look != null) prevCamRot = look.transform.rotation;

        if (!startingHandled)
        {
            startingHandled = true;
            if (startingItem != null) SpawnAndEquip(startingItem, null, drawOnRestore);
        }
    }

    // =================================================================== public API

    public bool CanLift(Pickupable p) => p != null && p.data != null && p.MassKg <= maxLiftKg;

    /// <summary>Reach for a world item. If the hands are full, the current item is set down first.</summary>
    public bool TryEquip(Pickupable p)
    {
        if (p == null || p.IsHeld || !CanLift(p)) return false;
        if (Current != null) Release(swapDropSpeed, false);
        Equip(p, false, false);
        return true;
    }

    /// <summary>Set the held item down gently in front of you.</summary>
    public void Drop() => Release(dropSpeed, false);

    /// <summary>Throw with 0..1 charge. Speed is limited by arm strength divided by item mass.</summary>
    public void Throw(float charge01)
    {
        if (Current == null) return;
        charge01 = Mathf.Clamp01(charge01);

        float tire = stamina != null ? Mathf.Lerp(1f, 0.6f, stamina.Fatigue) : 1f;
        float impulse = Mathf.Lerp(minThrowImpulse, maxThrowImpulse, charge01) * tire;
        float speed = Mathf.Min(maxThrowSpeed, impulse / Mathf.Max(0.05f, heldMass));

        if (stamina != null) stamina.Spend(heldMass * throwStaminaPerKg * (0.5f + charge01));
        Release(speed, true);
    }

    /// <summary>Wind-up amount 0..1 (the interactor feeds this while the drop key is held).</summary>
    public void SetThrowCharge(float value01) => throwCharge = Mathf.Clamp01(value01);

    /// <summary>
    /// Called every frame by WeaponADS. blend01 = 0 is the normal hold pose, 1 is fully on the sights.
    /// localPos/localRot is the camera-space pose that lines the sights up with the eye;
    /// swayEuler is extra aim wobble in degrees (breathing, muscle tremor).
    /// </summary>
    public void SetAimPose(float blend01, Vector3 localPos, Quaternion localRot, Vector3 swayEuler)
    {
        aimBlend = Mathf.Clamp01(blend01);
        aimPos = localPos;
        aimRot = localRot;
        aimSway = swayEuler;
    }

    public ItemSaveData CaptureSave()
    {
        ItemSaveData save = new ItemSaveData();
        if (Current != null && Current.data != null)
        {
            save.itemId = Current.data.id;
            save.state = Current.CaptureState();
        }
        return save;
    }

    /// <summary>Replace whatever is in the hands with the saved item (or nothing).</summary>
    public void RestoreSave(ItemSaveData save)
    {
        startingHandled = true; // a loaded save beats the Starting Item field
        DestroyHeld();
        if (save == null || !save.HasItem) return;

        ItemData data = database != null ? database.Find(save.itemId) : null;
        if (data == null)
        {
            Debug.LogWarning($"HeldItemSlot: can't restore '{save.itemId}'. Is the ItemDatabase assigned and does it contain this item?", this);
            return;
        }
        SpawnAndEquip(data, save.state, drawOnRestore);
    }

    /// <summary>Remove the held item from the game (used when loading, or if you consume it).</summary>
    public void DestroyHeld()
    {
        if (Current != null) Destroy(Current.gameObject);
        Current = null;
        ReleaseLoad();
        state = State.Empty;
        throwCharge = 0f;
    }

    // =================================================================== equip / release

    bool SpawnAndEquip(ItemData data, string itemState, bool draw)
    {
        if (data == null || data.prefab == null || look == null)
        {
            Debug.LogWarning("HeldItemSlot: item or its prefab is missing.", this);
            return false;
        }

        Transform cam = look.transform;
        GameObject go = Instantiate(data.prefab, cam.TransformPoint(data.holdPosition),
            cam.rotation * Quaternion.Euler(data.holdEuler));

        Pickupable p = go.GetComponent<Pickupable>();
        if (p == null)
        {
            Debug.LogWarning($"HeldItemSlot: prefab of '{data.name}' has no Pickupable component.", this);
            Destroy(go);
            return false;
        }
        if (p.data == null) p.data = data;

        p.RestoreState(itemState);
        Equip(p, true, draw);
        return true;
    }

    void Equip(Pickupable p, bool instant, bool draw)
    {
        Current = p;
        heldMass = p.MassKg;
        p.SetHeld(true);
        p.transform.SetParent(null, true);

        addedLoad = heldMass;
        motor.carriedLoadKg += addedLoad;

        if (instant)
        {
            state = State.Held;
            drawBlend = draw ? 1f : 0f;
        }
        else
        {
            state = State.Equipping;
            equipT = 0f;
            equipDuration = Mathf.Clamp(equipBaseTime + heldMass * equipTimePerKg, equipBaseTime, equipMaxTime);
            equipStartPos = p.transform.position;
            equipStartRot = p.transform.rotation;
            drawBlend = 0f;
        }

        ItemEquipped?.Invoke(p);
    }

    void Release(float speed, bool tumble)
    {
        if (Current == null) return;

        Pickupable p = Current;
        Transform cam = look.transform;
        Vector3 pos = p.transform.position;

        // Never let go of it inside a wall: put it just in front of the wall instead.
        Vector3 toItem = pos - cam.position;
        if (toItem.sqrMagnitude > 0.0001f &&
            Physics.Linecast(cam.position, pos, out RaycastHit wall, worldMask, QueryTriggerInteraction.Ignore))
        {
            pos = wall.point - toItem.normalized * 0.12f;
        }
        p.transform.SetPositionAndRotation(pos, p.transform.rotation);

        p.SetHeld(false);

        Vector3 dir = (cam.forward + cam.up * 0.12f).normalized;
        Vector3 playerVel = playerRb != null ? Pickupable.GetVelocity(playerRb) : Vector3.zero;
        Pickupable.SetVelocity(p.Body, playerVel + dir * speed);
        p.Body.angularVelocity = UnityEngine.Random.onUnitSphere *
            (tumble ? Mathf.Clamp(speed * 0.5f, 0.5f, 6f) : 0.5f);
        p.IgnoreCollisionsTemporarily(playerColliders, 0.4f);

        Current = null;
        ReleaseLoad();
        state = State.Empty;
        throwCharge = 0f;
        ItemDropped?.Invoke(p);
    }

    void ReleaseLoad()
    {
        if (addedLoad > 0f && motor != null)
            motor.carriedLoadKg = Mathf.Max(0f, motor.carriedLoadKg - addedLoad);
        addedLoad = 0f;
    }

    // =================================================================== events from the motor

    void OnLanded(float impact)
    {
        if (Current == null) return;
        posVel.y -= impact * landingKick * (1f + heldMass * 0.1f);
    }

    void OnJumped()
    {
        if (Current == null) return;
        posVel.y -= jumpKick * (1f + heldMass * 0.1f);
    }

    // =================================================================== per-frame pose

    void LateUpdate()
    {
        if (look == null || motor == null) return;

        float rawDt = Time.deltaTime;
        if (rawDt <= 0f) return;
        float dt = Mathf.Min(rawDt, 0.033f);
        Transform cam = look.transform;

        // The item was destroyed by something else? Give back its weight.
        if (state != State.Empty && Current == null)
        {
            ReleaseLoad();
            state = State.Empty;
        }

        // Camera turn rate in camera-local axes (deg/s): x = pitch, y = yaw, z = roll
        Quaternion dq = Quaternion.Inverse(prevCamRot) * cam.rotation;
        prevCamRot = cam.rotation;
        Vector3 e = dq.eulerAngles;
        Vector3 angVel = new Vector3(
            Mathf.DeltaAngle(0f, e.x), Mathf.DeltaAngle(0f, e.y), Mathf.DeltaAngle(0f, e.z)) / rawDt;
        angVel = Vector3.ClampMagnitude(angVel, 900f);

        // Breathing phase runs even with empty hands so it never restarts out of sync
        float fatigue = stamina != null ? stamina.Fatigue : 0f;
        breathPhase += dt * Mathf.Lerp(0.25f, 0.9f, fatigue) * Mathf.PI * 2f;

        if (state == State.Empty)
        {
            posOff = posVel = rotOff = rotVel = Vector3.zero;
            sprintBlend = chargeBlend = wallPush = 0f;
            return;
        }

        ItemData d = Current.data;
        float meff = 1f + heldMass * massLag;

        // --- Spring-dampers: heavier item = softer spring, same damping ratio ---
        float k = stiffness / meff;
        float c = 2f * dampingRatio * Mathf.Sqrt(k);

        Vector3 accel = motor.LocalAcceleration;
        Vector3 extPos = new Vector3(-accel.x, 0f, -accel.z) * accelerationSway;
        Vector3 extRot = new Vector3(-angVel.x, -angVel.y, -angVel.z * 0.5f) * lookSway;

        posVel += (-k * posOff - c * posVel + extPos) * dt;
        posOff += posVel * dt;
        posOff = ClampAxes(posOff, maxPosOffset);

        rotVel += (-k * rotOff - c * rotVel + extRot) * dt;
        rotOff += rotVel * dt;
        rotOff = ClampAxes(rotOff, maxRotOffset);

        // --- Pose blends ---
        float blendRate = 1f - Mathf.Exp(-poseBlendSpeed / meff * dt);
        bool sprinting = motor.IsSprinting && motor.PlanarSpeed > motor.walkSpeed * 1.1f;
        sprintBlend += ((sprinting ? 1f : 0f) - sprintBlend) * blendRate;
        chargeBlend += (throwCharge - chargeBlend) * (1f - Mathf.Exp(-10f * dt));
        drawBlend = Mathf.MoveTowards(drawBlend, 0f, dt / Mathf.Max(0.05f, drawTime * (0.6f + 0.4f * meff)));

        // --- Footstep bob (slightly behind the camera's own bob, so the item feels like it has weight) ---
        float speed01 = Mathf.Clamp01(motor.PlanarSpeed / motor.sprintSpeed);
        float bobTarget = (motor.IsGrounded && motor.PlanarSpeed > 0.3f) ? 1f : 0f;
        bobBlend = Mathf.MoveTowards(bobBlend, bobTarget, dt * 4f);
        float gaitMultiplier = Mathf.Lerp(walkBobMultiplier, sprintBobMultiplier, sprintBlend);
        float bobScale = bobBlend * (0.4f + speed01 * 0.6f) * gaitMultiplier;
        float phase = motor.StepPhase;
        float parity = motor.RightFootNext ? 1f : 0f;
        float bobY = -Mathf.Cos((phase - 0.08f) * Mathf.PI * 2f) * bobHeight * bobScale;
        float swing = Mathf.Sin((phase - 0.08f + parity) * Mathf.PI);
        float bobX = swing * bobSway * bobScale;

        // --- Tired arms ---
        float weightFactor = Mathf.Clamp(heldMass / 3f, 0.3f, 1.5f);
        float breath = Mathf.Sin(breathPhase - 0.6f);
        float sag = fatigue * fatigueSag * weightFactor;

        // --- Wall retraction ---
        float pushTarget = 0f;
        if (retractFromWalls && d.lengthMeters > 0.01f)
        {
            float reach = d.holdPosition.z + d.lengthMeters;
            if (Physics.SphereCast(cam.position, 0.08f, cam.forward, out RaycastHit hit, reach,
                    worldMask, QueryTriggerInteraction.Ignore))
                pushTarget = Mathf.Clamp(reach - hit.distance, 0f, maxRetract);
        }
        wallPush += (pushTarget - wallPush) * (1f - Mathf.Exp(-12f * dt));

        // --- Build the pose in camera space ---
        Vector3 lp = Vector3.Lerp(d.holdPosition, d.sprintPosition, sprintBlend);
        Quaternion lr = Quaternion.Slerp(Quaternion.Euler(d.holdEuler), Quaternion.Euler(d.sprintEuler), sprintBlend);

        // Aim down sights: slide from the hold pose to the pose that lines the sights up with the eye,
        // and calm the passive motion (bob, sway, tired arms) the more the gun is braced.
        float a = aimBlend;
        float steady = 1f - a * aimMotionReduction;
        if (a > 0.0001f)
        {
            lp = Vector3.Lerp(lp, aimPos, a);
            lr = Quaternion.Slerp(lr, aimRot, a);
        }

        float draw = drawBlend * drawBlend * (3f - 2f * drawBlend);
        lp += new Vector3(0.02f, -0.32f, -0.08f) * draw;
        lr = Quaternion.Euler(40f * draw, 0f, 0f) * lr;

        lp += new Vector3(0.07f, -0.04f, -0.16f) * chargeBlend;
        lr = Quaternion.Euler(-8f * chargeBlend, -12f * chargeBlend, 6f * chargeBlend) * lr;

        // (while aiming, the wall push is faded out so it can't pull the sights off the eye)
        lp.z -= wallPush * (1f - a);
        lr = Quaternion.Euler(-Mathf.Min(45f, wallPush * 130f * (1f - a)), 0f, 0f) * lr;

        lp += (posOff + new Vector3(bobX, bobY - sag, 0f)) * steady;
        Vector3 rotEuler = (rotOff + new Vector3(breath * breathPitch * fatigue, 0f, swing * bobRoll * bobScale)) * steady
                         + aimSway * a;
        Quaternion localRot = Quaternion.Euler(rotEuler) * lr;

        if (ExternalControl) return; // someone else (inspect mode) is posing the item

        Vector3 worldPos = cam.TransformPoint(lp);
        Quaternion worldRot = cam.rotation * localRot;
        Transform item = Current.transform;

        if (state == State.Equipping)
        {
            equipT += dt / equipDuration;
            float t = Mathf.Clamp01(equipT);
            float s = t * t * t * (t * (6f * t - 15f) + 10f); // smootherstep
            Vector3 pos = Vector3.Lerp(equipStartPos, worldPos, s) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * equipArcHeight);
            item.SetPositionAndRotation(pos, Quaternion.Slerp(equipStartRot, worldRot, s));

            if (t >= 1f)
            {
                state = State.Held;
                posVel.y -= Mathf.Min(0.12f * heldMass, 0.6f); // the weight settles into the hand
            }
        }
        else
        {
            item.SetPositionAndRotation(worldPos, worldRot);
        }
    }

    static Vector3 ClampAxes(Vector3 v, float max)
    {
        v.x = Mathf.Clamp(v.x, -max, max);
        v.y = Mathf.Clamp(v.y, -max, max);
        v.z = Mathf.Clamp(v.z, -max, max);
        return v;
    }
}