using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Reload interaction mode: working the gun with the mouse.
///
/// WHEN IT'S ACTIVE: while you are in weapon inspect mode (hold R) and NOT standing in a safe zone. In a safe zone
/// the attachment picker is used instead (that part is untouched).
///
/// WHAT YOU CAN DO (each part is a clickable InspectHotspot on the weapon, set up on the Firearm component):
///   Bolt catch          click: releases a locked-back bolt (it slams forward and chambers a round)
///   Magazine release    click: unlocks the magazine. Nobody holding it? It falls out after a moment.
///   Fire selector       click: next fire mode, same as pressing B
///   Charging handle     press and DRAG it back along the bolt, then let go. Pulled all the way back it throws out
///                       what is in the chamber, and on release strips the next round from the magazine.
///   Magazine            once it is unlocked, press and DRAG it out of the magazine well. Let go over the well
///                       to push it back in, anywhere else to put it in your stash.
///
///   T   magazine release. Works in every mode, not only here (an unlocked magazine nobody holds falls out); in this mode it is
///       also how you keep hold of the magazine: the cursor is on the magazine, so you can't click the button
///   Y   bolt catch while the mouse is holding the charging handle all the way back (locks the bolt open)
///
/// A temporary "magazine stash" list at the bottom left lets you take a magazine in hand, until pouches replace it:
/// click one, move it over the magazine well and click to insert it.
///
/// Put this on the Player root next to WeaponInspect.
/// </summary>
[DefaultExecutionOrder(40)]
public class FirearmInteraction : MonoBehaviour
{
    [Header("References (found automatically if empty)")]
    public WeaponInspect inspect;
    public HeldItemSlot slot;
    public WeaponRecoil recoil;
    public AttachmentInventory inventory;

    [Header("Magazine")]
    [Tooltip("Seconds after pressing the release before an unheld magazine falls out.")]
    public float magazineDropDelay = 0.5f;
    [Tooltip("The shortest distance (in the magazine slot's units) a magazine has to be pulled before it is clear of the well. " +
             "Normally the slot's Insert Offset length is used, this is the minimum.")]
    public float minPullDistance = 0.08f;
    [Tooltip("How far in front of the camera (metres) a magazine in your hand floats.")]
    public float heldDistance = 0.45f;
    [Tooltip("How quickly the magazine in your hand follows the cursor. Smaller = snappier.")]
    public float heldFollowTime = 0.05f;
    [Tooltip("How quickly the magazine in your hand turns to match the cursor (or the well, once it is guided). Higher = snappier.")]
    public float heldTurnSpeed = 18f;

    [Header("Magwell Guide")]
    [Tooltip("ON: a magazine in your hand is gently guided into the well as the cursor lines up with it. The mouse only gives two axes, " +
             "so without this the magazine floats past the well. The guide uses the magazine slot's own position and Insert Offset: no extra empty needed.")]
    public bool guideIntoWell = true;
    [Tooltip("How far out from the well (metres, in the real world, along the insert direction) the guide starts to pull. " +
             "Beyond this the magazine follows the cursor freely. Shown as the cyan funnel in the Scene view while playing.")]
    public float guideLength = 0.18f;
    [Tooltip("The point of no return (metres from the seated position, along the insert direction). Once the magazine is this close to the well and inside the funnel, " +
             "it is stuck to the well's axis: yanking the mouse sideways can't push it through the magwell, it can only slide in or back out. " +
             "It lets go gradually over the next 50% of this distance as you pull it back out. 0 = off. Shown as the red ring.")]
    public float pointOfNoReturn = 0.04f;
    [Tooltip("Width of the funnel at its mouth (metres): how close the cursor has to pass to the well's axis, far out, before the guide pulls. Bigger = more forgiving.")]
    public float guideRadius = 0.12f;
    [Tooltip("Width of the funnel right at the well (metres). Smaller = the magazine has to be lined up more accurately to go in.")]
    public float guideTightRadius = 0.03f;
    [Tooltip("How quickly the guided magazine slides along the well's axis to follow the cursor (seconds, smaller = snappier). " +
             "Only the depth is smoothed: the magazine's sideways position and angle are always exact, so this can't make it clip.")]
    public float guidedFollowTime = 0.03f;
    [Tooltip("ON: pushing the guided magazine all the way in seats it with a click, even if the mouse is still held. OFF: you have to let go over the well.")]
    public bool autoSeat = true;
    [Tooltip("How close (metres) the guided magazine has to be to its seated position to click in.")]
    public float seatDistance = 0.012f;
    [Header("Magwell Collision")]
    [Tooltip("ON: slamming the magazine at the well, or pushing it in when it isn't lined up, makes it miss the magwell: it goes off along a random " +
             "Magazine Miss Axis set up on the Firearm. You have to go in slower and straighter. Needs Point Of No Return above 0 (the lip sits at 1.5 x that distance).")]
    public bool magwellCollision = true;
    [Tooltip("Fastest the magazine can be pushed into the well without hitting the lip (metres per second along the well). Lower = stricter.")]
    public float maxInsertSpeed = 0.9f;
    [Tooltip("How far (metres) the cursor can be off the well's axis when the magazine reaches the lip without hitting it. Smaller = stricter.")]
    public float lipTolerance = 0.02f;
    [Tooltip("How quickly (seconds) the magazine swings from the well's axis onto the miss axis, and back off it at the end.")]
    public float missBlendSeconds = 0.08f;
    [Tooltip("Fired when the magazine hits the lip. Hook a 'clack' sound here (drag an AudioSource and pick Play).")]
    public UnityEvent onMagazineHitWell;

    [Header("Debug")]
    [Tooltip("Draw the funnel in the Scene view while playing, to tune the numbers above.")]
    public bool showGuideGizmo = true;

    [Header("On Screen")]
    public bool showHelp = true;
    [Tooltip("The temporary list of magazines in your stash (bottom left), until the plate carrier pouches exist.")]
    public bool showStash = true;

    enum DragKind { None, Handle, SeatedMagazine, HeldMagazine }
    struct StashEntry { public AttachmentData data; public MagazineContents mag; }

    DragKind drag = DragKind.None;
    Firearm gun;
    AttachmentSlot magSlot;
    Collider[] playerColliders;
    Camera cam;
    bool pointerBlockedByUs;

    // charging handle drag
    float handleS0, handleStartFraction;

    // magazine still in the well
    Transform seatedT;
    Vector3 seatedLocal0, seatedAxisLocal;
    float seatedClear, seatedS0;

    // magazine in the hand
    GameObject heldVisual;
    MagazineContents heldContents;
    AttachmentData heldType;
    bool heldSticky;
    Vector3 heldVel;
    Quaternion heldRotRelCam = Quaternion.identity;
    Vector3 heldFreePos;               // where the magazine would be with no guide (smoothed cursor follow)
    Quaternion heldFreeRot;
    float heldAlong, heldAlongVel;     // smoothed depth along the well's axis while guided
    bool heldAlongValid;
    float lastCursorAlong, cursorAlongSpeed; // how fast the cursor is pushing the magazine along the well's axis
    MagazineMissAxis missAxis, lastMissAxis; // the miss line the magazine is sliding along (null = not missed)
    bool missExiting;                        // the miss is over, easing back to normal guidance
    float missDepth, missDepthVel, missBlend;
    bool missCoverPending;                   // this miss still has to knock the dust cover shut
    bool framePressed, frameHeld;     // this frame's click input, handed to the late step
    float guideWeight;                 // 0 = following the cursor freely, 1 = fully guided into the well
    const float ReleaseAcceptWeight = 0.5f; // letting go while at least this guided seats the magazine

    // stash list
    readonly List<StashEntry> stash = new List<StashEntry>();
    Rect stashRect;
    GUIStyle labelStyle, buttonStyle;

    void Awake()
    {
        if (inspect == null) inspect = GetComponent<WeaponInspect>();
        if (slot == null) slot = GetComponent<HeldItemSlot>();
        if (recoil == null) recoil = GetComponent<WeaponRecoil>();
        if (inventory == null) inventory = GetComponentInChildren<AttachmentInventory>();
        playerColliders = GetComponentsInChildren<Collider>();

        // The magazine in the hand is placed AFTER the weapon has been posed this frame (see LateStep), so it never trails the gun.
        LateStep step = gameObject.AddComponent<LateStep>();
        step.tick = PlaceHeldMagazine;
    }

    void OnDisable() => CancelAll();

    /// <summary>Runs in LateUpdate after WeaponInspect (60) and HeldItemSlot (50) have moved the gun.</summary>
    [DefaultExecutionOrder(100)]
    class LateStep : MonoBehaviour
    {
        public System.Action tick;
        void LateUpdate() { if (tick != null) tick(); }
    }

    void PlaceHeldMagazine()
    {
        if (drag != DragKind.HeldMagazine || gun == null || inspect == null) return;
        UpdateHeld(inspect.PointerRay, framePressed, frameHeld);
    }

    // ================================================================== frame

    void Update()
    {
        if (inspect == null) return;

        WorkMagazineReleaseOutsideInspect();

        Firearm current = inspect.IsInspecting && !inspect.IsExiting && inspect.Item != null
            ? inspect.Item.GetComponent<Firearm>() : null;
        bool active = current != null && !SafeZone.PlayerInSafeZone;

        if (!active || current != gun)
        {
            CancelAll();
            gun = active ? current : null;
            magSlot = gun != null ? gun.MagazineSlot : null;
            if (!active)
            {
                if (pointerBlockedByUs) { inspect.PointerBlocked = false; pointerBlockedByUs = false; }
                stash.Clear();
                return;
            }
        }

        if (cam == null && inspect.look != null) cam = inspect.look.GetComponentInChildren<Camera>();
        if (cam == null) cam = Camera.main;

        BuildStash();
        Vector2 guiPointer = GameInput.PointerPosition();
        guiPointer.y = Screen.height - guiPointer.y;
        bool overStash = showStash && stashRect.Contains(guiPointer);
        inspect.PointerBlocked = overStash;
        pointerBlockedByUs = overStash;

        Ray ray = inspect.PointerRay;
        bool pressed = GameInput.ClickPressed() && !overStash;
        bool held = GameInput.ClickHeld();
        framePressed = pressed;
        frameHeld = held;

        if (GameInput.MagReleasePressed()) gun.PressMagazineRelease();
        if (GameInput.BoltCatchPressed()) gun.PressBoltCatch();

        switch (drag)
        {
            case DragKind.Handle: UpdateHandle(ray, held); break;
            case DragKind.SeatedMagazine: UpdateSeated(ray, held); break;
            case DragKind.HeldMagazine: break; // placed in LateUpdate (PlaceHeldMagazine), after the gun has moved
            default:
                if (pressed) TryBeginClick(ray);
                break;
        }
    }

    void TryBeginClick(Ray ray)
    {
        InspectHotspot over = inspect.HoveredHotspot;
        if (over == null) return;

        if (over == gun.boltCatchHotspot) gun.PressBoltCatch();
        else if (over == gun.magazineReleaseHotspot) gun.PressMagazineRelease();
        else if (over == gun.selectorHotspot) gun.CycleFireMode();
        else if (over == gun.dustCoverHotspot) gun.ToggleDustCover();
        else if (over == gun.chargingHandleHotspot) BeginHandle(ray);
        else if (magSlot != null && over == magSlot.Hotspot && gun.HasMagazine && gun.MagazineUnlocked) BeginSeated(ray);
    }

    // ================================================================== charging handle

    void BeginHandle(Ray ray)
    {
        if (!gun.BeginBoltDrag()) return;
        drag = DragKind.Handle;
        handleS0 = AxisParam(ray, gun.DragRestWorld, gun.DragAxisWorld);
        handleStartFraction = gun.HandleFraction;
    }

    void UpdateHandle(Ray ray, bool held)
    {
        if (!held || !gun.BoltDragging)
        {
            gun.EndBoltDrag();
            drag = DragKind.None;
            return;
        }
        float s = AxisParam(ray, gun.DragRestWorld, gun.DragAxisWorld);
        float fraction = handleStartFraction + (s - handleS0) / Mathf.Max(0.001f, gun.DragTravelWorld);
        gun.SetBoltDrag(fraction);
    }

    // ================================================================== magazine: still in the well

    void BeginSeated(Ray ray)
    {
        GameObject inst = magSlot.Instance;
        if (inst == null) return;

        AttachmentSpringMover mover = inst.GetComponent<AttachmentSpringMover>();
        if (mover != null) mover.enabled = false; // the hand moves it now

        seatedT = inst.transform;
        seatedLocal0 = seatedT.localPosition;
        Vector3 offset = magSlot.insertOffset;
        seatedAxisLocal = offset.sqrMagnitude > 1e-6f ? offset.normalized : Vector3.down;
        seatedClear = Mathf.Max(minPullDistance, offset.magnitude);
        seatedS0 = AxisParam(ray, magSlot.Mount.TransformPoint(seatedLocal0), SeatedAxisWorld());
        drag = DragKind.SeatedMagazine;
    }

    Vector3 SeatedAxisWorld() => magSlot.Mount.TransformDirection(seatedAxisLocal).normalized;

    void UpdateSeated(Ray ray, bool held)
    {
        if (seatedT == null || !gun.HasMagazine)
        {
            drag = DragKind.None;
            return;
        }

        if (!held)
        {
            SpringBackSeated(); // let go before it cleared the well: it slides back (or falls, if the release is still pressed)
            drag = DragKind.None;
            return;
        }

        float worldPerLocal = Mathf.Max(0.0001f, magSlot.Mount.TransformVector(seatedAxisLocal).magnitude);
        float s = AxisParam(ray, magSlot.Mount.TransformPoint(seatedLocal0), SeatedAxisWorld());
        float d = Mathf.Clamp((s - seatedS0) / worldPerLocal, 0f, seatedClear);
        seatedT.localPosition = seatedLocal0 + seatedAxisLocal * d;

        if (d >= seatedClear * 0.98f) TakeMagazineInHand(ray);
    }

    void SpringBackSeated()
    {
        if (seatedT == null) return;
        AttachmentSpringMover mover = seatedT.GetComponent<AttachmentSpringMover>();
        if (mover == null) mover = seatedT.gameObject.AddComponent<AttachmentSpringMover>();
        mover.Configure(300f, 1.1f);
        mover.MoveTo(seatedLocal0, seatedT.localRotation, false);
    }

    // ================================================================== magazine: in the hand

    /// <summary>The magazine has cleared the well: it leaves the gun (rounds and all) and follows the cursor.</summary>
    void TakeMagazineInHand(Ray ray)
    {
        Transform src = seatedT;
        GameObject visual = Instantiate(src.gameObject, src.position, src.rotation);
        visual.transform.localScale = src.lossyScale;
        StripVisual(visual);

        heldContents = gun.EjectMagazine(out heldType, false); // removes the original model with the rounds still in `heldContents`
        if (heldContents == null || heldType == null)
        {
            Destroy(visual);
            drag = DragKind.None;
            return;
        }

        BeginHolding(visual, false);
    }

    void BeginHolding(GameObject visual, bool sticky)
    {
        heldVisual = visual;
        heldSticky = sticky;
        heldVel = Vector3.zero;
        heldRotRelCam = Quaternion.Inverse(CamRotation()) * visual.transform.rotation;
        heldFreePos = visual.transform.position;
        heldFreeRot = visual.transform.rotation;
        heldAlongValid = false;
        guideWeight = 0f;
        ResetMiss();
        drag = DragKind.HeldMagazine;
    }

    void UpdateHeld(Ray ray, bool pressed, bool held)
    {
        if (heldVisual == null)
        {
            drag = DragKind.None;
            return;
        }

        Transform vt = heldVisual.transform;
        float dt = Time.deltaTime;

        // Free: the magazine floats at a fixed distance along the cursor ray, smoothed (this is the only part that trails).
        Vector3 freeTarget = ray.origin + ray.direction * heldDistance;
        Quaternion freeRotTarget = CamRotation() * heldRotRelCam;
        heldFreePos = Vector3.SmoothDamp(heldFreePos, freeTarget, ref heldVel, Mathf.Max(0.01f, heldFollowTime));
        heldFreeRot = Quaternion.Slerp(heldFreeRot, freeRotTarget, 1f - Mathf.Exp(-heldTurnSpeed * dt));

        Vector3 pos = heldFreePos;
        Quaternion rot = heldFreeRot;

        // Guided: near the well the magazine is placed ON the well's axis, straight from the gun's current pose this frame
        // (no smoothing on the sideways position or the angle, so it can't lag behind the gun and clip through the magwell).
        // Only the depth along the axis is smoothed, which can't push it sideways through anything.
        guideWeight = 0f;
        Vector3 seatWorld = Vector3.zero;
        if (guideIntoWell && magSlot != null && gun != null && gun.CanAcceptMagazine(heldContents))
        {
            guideWeight = ComputeGuide(ray, vt.position, out Vector3 axis, out float along, out float cursorMiss, out Quaternion seatRot, out seatWorld);
            if (guideWeight > 0f)
            {
                if (!heldAlongValid)
                {
                    heldAlong = Vector3.Dot(vt.position - seatWorld, axis);
                    heldAlongVel = 0f;
                    lastCursorAlong = along;
                    cursorAlongSpeed = 0f;
                    heldAlongValid = true;
                }

                // --- Magwell collision: slamming the magazine at the well makes it hit the lip ---
                float target = along;
                if (magwellCollision && pointOfNoReturn > 0.0001f)
                {
                    float gate = pointOfNoReturn * 1.5f;   // the lip: where the magazine starts to enter the well proper

                    // How fast the cursor is pushing the magazine toward the well (m/s along the axis, lightly smoothed).
                    float v = (along - lastCursorAlong) / Mathf.Max(dt, 0.0001f);
                    cursorAlongSpeed = Mathf.Lerp(cursorAlongSpeed, v, 1f - Mathf.Exp(-25f * dt));
                    lastCursorAlong = along;

                    if (missAxis == null && heldAlong > gate && along < gate)
                    {
                        // Trying to enter the well right now. Too fast, or not lined up: it misses, and goes off along
                        // one of the weapon's Magazine Miss Axes (a random one). With none set up, nothing special happens.
                        if ((-cursorAlongSpeed > maxInsertSpeed || cursorMiss > lipTolerance) && BeginMiss(vt.position))
                        {
                            if (onMagazineHitWell != null) onMagazineHitWell.Invoke();
                        }
                    }
                    if (missAxis != null) target = Mathf.Max(along, gate); // underneath the miss, the well-axis position waits at the lip
                }

                heldAlong = Mathf.Max(0f, Mathf.SmoothDamp(heldAlong, target, ref heldAlongVel, Mathf.Max(0.005f, guidedFollowTime)));

                pos = Vector3.Lerp(heldFreePos, seatWorld + axis * heldAlong, guideWeight);
                rot = Quaternion.Slerp(heldFreeRot, seatRot, guideWeight);
            }

            // --- A missed insertion: the magazine travels along the chosen Magazine Miss Axis instead of the well's axis ---
            if (missAxis != null && missAxis.axis != null)
            {
                Vector3 mo = missAxis.axis.position;              // deepest point
                Vector3 md = missAxis.axis.forward;               // away from the gun
                float mlen = Mathf.Max(0.01f, missAxis.length);

                ClosestRayToAxis(ray, mo, md, out float mt, out float mmiss);
                float mw = 1f - Smooth01(0.6f, 1f, mmiss / Mathf.Max(0.001f, guideRadius));

                // Over once the cursor has taken the magazine out past the end of the line (or wandered off it): try again.
                if (!missExiting && (mt >= mlen || mw < 0.05f)) missExiting = true;

                missDepth = Mathf.Clamp(Mathf.SmoothDamp(missDepth, Mathf.Clamp(mt, 0f, mlen), ref missDepthVel,
                                                          Mathf.Max(0.005f, guidedFollowTime)), 0f, mlen);
                missBlend = Mathf.MoveTowards(missBlend, missExiting ? 0f : 1f, dt / Mathf.Max(0.01f, missBlendSeconds));

                // The magazine has slid far enough in along the line to hit the dust cover: it slams shut.
                if (missCoverPending && 1f - missDepth / mlen >= missAxis.closesAtDepth)
                {
                    gun.CloseDustCover();
                    missCoverPending = false;
                }

                Vector3 missPos = mo + md * missDepth;
                Quaternion missRot = Quaternion.FromToRotation(axis, md) * seatRot; // tilted the way the miss axis points
                pos = Vector3.Lerp(pos, missPos, missBlend);
                rot = Quaternion.Slerp(rot, missRot, missBlend);

                if (missExiting && missBlend <= 0.001f) ResetMiss();
            }
            else if (missAxis != null)
            {
                ResetMiss(); // the empty was deleted
            }
        }
        else
        {
            ResetMiss();
        }
        if (guideWeight <= 0f) heldAlongValid = false;

        vt.SetPositionAndRotation(pos, rot);

        // Pushed all the way in: it clicks home.
        if (autoSeat && missAxis == null && guideWeight > 0.9f && Vector3.Distance(vt.position, seatWorld) <= seatDistance && SeatHeld())
        {
            drag = DragKind.None;
            return;
        }

        // Dragged out of the gun: ends when you let go. Taken from the stash: ends on your next click.
        bool end = heldSticky ? pressed : !held;
        if (end && heldSticky && missAxis != null) end = false; // a missed magazine can't be put down on the spot: pull it out and try again
        if (end) EndHeld();
    }

    void EndHeld()
    {
        InspectHotspot over = inspect.HoveredHotspot;
        bool overWell = magSlot != null && over == magSlot.Hotspot;
        bool lined = guideWeight >= ReleaseAcceptWeight;
        bool missed = missAxis != null;                  // a magazine sliding along a miss axis has to be pulled out and retried, not dropped in

        if (!(!missed && (overWell || lined) && SeatHeld())) Stow();
        drag = DragKind.None;
    }

    // ================================================================== magazine misses

    /// <summary>Picks a random Magazine Miss Axis from the weapon (not the same one twice in a row if there is a choice). False if the gun has none.</summary>
    bool BeginMiss(Vector3 currentPos)
    {
        MagazineMissAxis[] axes = gun != null ? gun.magazineMissAxes : null;
        if (axes == null || axes.Length == 0) return false;

        MagazineMissAxis pick = null;
        for (int i = 0; i < 8; i++)
        {
            MagazineMissAxis c = axes[Random.Range(0, axes.Length)];
            if (c == null || c.axis == null) continue;
            pick = c;
            if (c != lastMissAxis || axes.Length == 1) break;
        }
        if (pick == null) return false;

        missAxis = lastMissAxis = pick;
        missExiting = false;
        missCoverPending = pick.closesDustCover;        // this miss slams into the dust cover: it shuts once the magazine slides far enough in
        missDepth = Mathf.Clamp(Vector3.Dot(currentPos - pick.axis.position, pick.axis.forward), 0f, Mathf.Max(0.01f, pick.length));
        missDepthVel = 0f;
        return true;
    }

    void ResetMiss()
    {
        missAxis = null;
        missExiting = false;
        missCoverPending = false;
        missBlend = 0f;
        missDepthVel = 0f;
    }

    /// <summary>Puts the magazine in the hand into the gun. The gun's own model takes over from where the one in the hand is.</summary>
    bool SeatHeld()
    {
        if (heldVisual == null || gun == null || !gun.CanAcceptMagazine(heldContents)) return false;

        Vector3 pos = heldVisual.transform.position;
        Quaternion rot = heldVisual.transform.rotation;
        if (!gun.InsertMagazine(heldContents, heldType)) return false;

        // The install animation would start the new model at the well's mouth; start it where the hand left it instead (no jump).
        GameObject inst = magSlot != null ? magSlot.Instance : null;
        if (inst != null && inst.GetComponent<AttachmentSpringMover>() != null)
            inst.transform.SetPositionAndRotation(pos, rot);

        Destroy(heldVisual);
        ClearHeld();
        return true;
    }

    // ================================================================== magwell guide

    /// <summary>
    /// How strongly the magazine is guided (0..1). The well is a funnel along the slot's Insert Offset direction: wide far out,
    /// narrow at the well. The cursor ray is compared with the well's axis (so it works whatever the camera depth is):
    /// the closer the ray passes to the axis, the more the magazine follows the axis instead of the cursor.
    /// </summary>
    float ComputeGuide(Ray ray, Vector3 currentPos, out Vector3 axis, out float along, out float cursorMiss, out Quaternion seatRot, out Vector3 seatWorld)
    {
        Transform mount = magSlot.Mount;
        Vector3 offset = magSlot.insertOffset;

        seatWorld = mount.TransformPoint(magSlot.seatPos);   // where the magazine root sits when fully seated
        seatRot = mount.rotation * magSlot.seatRot;
        axis = Vector3.up;
        along = 0f;
        cursorMiss = 0f;
        if (offset.sqrMagnitude < 1e-8f) return 0f;

        axis = mount.TransformDirection(offset).normalized;                        // out of the well
        float funnel = Mathf.Max(0.02f, guideLength);                              // real-world metres, exactly the field's value

        ClosestRayToAxis(ray, seatWorld, axis, out float t, out float miss);
        cursorMiss = miss;

        // Depth comes from where the cursor lines up along the axis. Past the seat (the "overshoot") it simply stays seated.
        along = Mathf.Clamp(t, 0f, funnel);

        float radius = Mathf.Lerp(guideTightRadius, guideRadius, along / funnel);
        float sideways = 1f - Smooth01(0.35f, 1f, miss / Mathf.Max(0.001f, radius)); // full pull near the axis, none at the funnel wall
        float lengthFade = 1f - Smooth01(0.5f, 1f, t / funnel);                      // no pull beyond the funnel's mouth
        float weight = sideways * lengthFade;

        // Point of no return: judged from where the magazine ACTUALLY is (not the cursor), so a yank of the mouse can't undo it.
        // Close to the well and inside the funnel = stuck to the axis, whatever the cursor does sideways.
        if (pointOfNoReturn > 0.0001f)
        {
            Vector3 fromSeat = currentPos - seatWorld;
            float depth = Vector3.Dot(fromSeat, axis);                          // actual distance out from the seat
            float side = (fromSeat - axis * depth).magnitude;                    // actual distance from the axis
            float wall = Mathf.Lerp(guideTightRadius, guideRadius, Mathf.Clamp01(depth / funnel));
            if (side <= wall)
            {
                float commit = 1f - Smooth01(pointOfNoReturn, pointOfNoReturn * 1.5f, depth);
                weight = Mathf.Max(weight, commit);
            }
        }
        return weight;
    }

    static float Smooth01(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    /// <summary>Closest approach between the cursor ray and the well's axis: t = distance along the axis from its origin, miss = how far apart they are.</summary>
    static void ClosestRayToAxis(Ray ray, Vector3 origin, Vector3 dir, out float t, out float miss)
    {
        Vector3 r = ray.origin - origin;
        float b = Vector3.Dot(ray.direction, dir);
        float d = Vector3.Dot(ray.direction, r);
        float e = Vector3.Dot(dir, r);
        float denom = 1f - b * b;
        float s = 0f;
        if (denom > 1e-4f) { t = (e - b * d) / denom; s = t * b - d; }
        else t = e;
        if (s < 0f) { s = 0f; t = e; } // the closest point is behind the camera: use the camera itself

        miss = Vector3.Distance(ray.origin + ray.direction * s, origin + dir * t);
    }

    void OnDrawGizmos()
    {
        if (!showGuideGizmo || !Application.isPlaying || magSlot == null || magSlot.Mount == null || gun == null) return;
        Vector3 offset = magSlot.insertOffset;
        if (offset.sqrMagnitude < 1e-8f) return;

        Transform mount = magSlot.Mount;
        Vector3 seat = mount.TransformPoint(magSlot.seatPos);
        Vector3 axis = mount.TransformDirection(offset).normalized;
        float funnel = Mathf.Max(0.02f, guideLength);
        Vector3 end = seat + axis * funnel;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(seat, end);
        DrawRing(seat, axis, guideTightRadius);
        DrawRing(seat + axis * funnel * 0.5f, axis, Mathf.Lerp(guideTightRadius, guideRadius, 0.5f));
        DrawRing(end, axis, guideRadius);

        if (pointOfNoReturn > 0.0001f)
        {
            Gizmos.color = Color.red;
            DrawRing(seat + axis * pointOfNoReturn, axis, Mathf.Lerp(guideTightRadius, guideRadius, Mathf.Clamp01(pointOfNoReturn / funnel)));

            if (magwellCollision)
            {
                Gizmos.color = Color.yellow; // the lip: where a too-fast or crooked magazine gets stopped
                float gate = pointOfNoReturn * 1.5f;
                DrawRing(seat + axis * gate, axis, lipTolerance);
            }
        }
    }

    static void DrawRing(Vector3 center, Vector3 axis, float radius)
    {
        Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 v = Vector3.Cross(axis, u);
        Vector3 prev = center + u * radius;
        for (int i = 1; i <= 24; i++)
        {
            float a = i / 24f * Mathf.PI * 2f;
            Vector3 p = center + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius;
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
    }

    /// <summary>Puts the magazine in the hand into the stash (or on the ground if there is no stash).</summary>
    void Stow()
    {
        if (heldContents != null && heldType != null)
        {
            if (inventory != null) inventory.AddMagazine(heldType, heldContents);
            else if (heldVisual != null)
                MagazineItem.Drop(heldType, heldContents, heldVisual.transform.position, heldVisual.transform.rotation,
                                      heldVisual.transform.lossyScale, Vector3.zero, inventory, playerColliders);
        }
        if (heldVisual != null) Destroy(heldVisual);
        ClearHeld();
    }

    void ClearHeld()
    {
        heldVisual = null;
        heldContents = null;
        heldType = null;
    }

    void GrabFromStash(StashEntry e)
    {
        if (drag != DragKind.None || e.data == null || e.mag == null) return;
        if (!inventory.TakeMagazine(e.data, e.mag)) return;

        Quaternion rot = magSlot != null ? magSlot.Mount.rotation : CamRotation();
        Vector3 pos = inspect.PointerRay.origin + inspect.PointerRay.direction * heldDistance;
        GameObject visual = e.data.prefab != null ? Instantiate(e.data.prefab, pos, rot) : new GameObject("Magazine (held)");
        visual.transform.position = pos;
        visual.transform.rotation = rot;
        StripVisual(visual);

        heldContents = e.mag;
        heldType = e.data;
        BeginHolding(visual, true);
    }

    static void StripVisual(GameObject go)
    {
        foreach (MagazineItem m in go.GetComponentsInChildren<MagazineItem>()) Destroy(m);
        foreach (AttachmentSpringMover m in go.GetComponentsInChildren<AttachmentSpringMover>()) Destroy(m);
        foreach (Collider c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        foreach (Rigidbody r in go.GetComponentsInChildren<Rigidbody>()) Destroy(r);
    }

    Quaternion CamRotation() => cam != null ? cam.transform.rotation : Quaternion.identity;

    // ================================================================== magazine release: falls out

    /// <summary>An unlocked magazine nobody is holding falls out of the gun and lands on the ground.</summary>
    void AutoDrop(Firearm g, AttachmentSlot ms)
    {
        if (ms == null || !g.MagazineUnlocked || !g.HasMagazine) return;
        if (g == gun && drag == DragKind.SeatedMagazine) return; // being held
        if (Time.time - g.MagazineUnlockedAt < magazineDropDelay) return;

        GameObject inst = ms.Instance;
        Vector3 pos = inst != null ? inst.transform.position : g.transform.position;
        Quaternion rot = inst != null ? inst.transform.rotation : g.transform.rotation;
        Vector3 scale = inst != null ? inst.transform.lossyScale : Vector3.one;

        MagazineContents contents = g.EjectMagazine(out AttachmentData type, false);
        MagazineItem.Drop(type, contents, pos, rot, scale, Vector3.down * 0.3f, inventory, playerColliders);

        // The gun lightens and drops a little in the hands.
        if (recoil != null) recoil.ApplyKick(g.magazineDropKickDegrees, g.magazineDropKickSeconds);
    }

    /// <summary>
    /// T works the magazine release whenever the gun is in the hands, not only in reload mode. This also makes an unlocked magazine
    /// fall out in every mode (reload mode, normal play, safe zone), so it runs every frame.
    /// </summary>
    void WorkMagazineReleaseOutsideInspect()
    {
        Firearm held = slot != null && slot.Current != null ? slot.Current.GetComponent<Firearm>() : null;
        if (held == null) return;

        if (!inspect.IsInspecting && GameInput.MagReleasePressed() && CanUseHands())
            held.PressMagazineRelease();

        AutoDrop(held, held.MagazineSlot);
    }

    /// <summary>The gun is in the hands and nothing else owns them (no inspect mode, no menu).</summary>
    bool CanUseHands()
    {
        if (slot == null || !slot.HasItem || slot.HeldData == null || slot.HeldData.category != ItemCategory.Weapon) return false;
        if (slot.IsEquipping || slot.ExternalControl || slot.IsThrowCharging) return false;
        if (inspect.look != null && !inspect.look.LookEnabled) return false;
        return Cursor.lockState == CursorLockMode.Locked;
    }

    // ================================================================== cancel

    void CancelAll()
    {
        switch (drag)
        {
            case DragKind.Handle: if (gun != null) gun.EndBoltDrag(); break;
            case DragKind.SeatedMagazine: SpringBackSeated(); break;
            case DragKind.HeldMagazine: Stow(); break;
        }
        drag = DragKind.None;
    }

    // ================================================================== geometry

    /// <summary>
    /// Where along a line (origin + t * dir, dir is a unit vector) the cursor ray passes closest to it. Lets the mouse drag a part
    /// along its rail smoothly, however the camera looks at it.
    /// </summary>
    static float AxisParam(Ray ray, Vector3 origin, Vector3 dir)
    {
        Vector3 w0 = origin - ray.origin;
        float b = Vector3.Dot(dir, ray.direction);
        float d = Vector3.Dot(dir, w0);
        float e = Vector3.Dot(ray.direction, w0);
        float denom = 1f - b * b;
        if (denom < 1e-4f) return 0f; // the ray runs along the axis: no information
        return (b * e - d) / denom;
    }

    // ================================================================== on screen

    void BuildStash()
    {
        stash.Clear();
        if (inventory != null && gun != null)
            foreach (AttachmentData d in inventory.Owned(AttachmentCategory.Magazine))
            {
                if (d.magazineCaliber != gun.caliber) continue;
                foreach (MagazineContents m in inventory.MagazinesOf(d))
                    stash.Add(new StashEntry { data = d, mag = m });
            }

        int rows = Mathf.Max(1, stash.Count) + 1;
        float h = rows * 26f + 12f;
        stashRect = new Rect(20f, Screen.height - h - 20f, 320f, h);
    }

    void OnGUI()
    {
        if (gun == null) return;

        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
        }

        if (showHelp)
            GUI.Label(new Rect(20f, 20f, 640f, 90f),
                "<b>Reload mode</b>   Click: bolt catch, magazine release, fire selector.   Drag: charging handle, magazine (release it first).\n" +
                "T = magazine release while holding the magazine.   Y = bolt catch while holding the handle back.", labelStyle);

        if (!showStash) return;

        GUI.Box(stashRect, "");
        float y = stashRect.y + 6f;
        string title = drag == DragKind.HeldMagazine && heldSticky
            ? "Click the magazine well to insert, elsewhere to stow"
            : "Magazine stash (click to take in hand)";
        GUI.Label(new Rect(stashRect.x + 8f, y, stashRect.width - 16f, 24f), title, labelStyle);
        y += 26f;

        if (stash.Count == 0)
        {
            GUI.Label(new Rect(stashRect.x + 8f, y, stashRect.width - 16f, 24f), "No magazines of this caliber.", labelStyle);
            return;
        }

        for (int i = 0; i < stash.Count; i++)
        {
            StashEntry e = stash[i];
            string text = $"{e.data.displayName}   {e.mag.Count}/{e.mag.capacity}";
            if (GUI.Button(new Rect(stashRect.x + 6f, y, stashRect.width - 12f, 24f), text, buttonStyle))
            {
                GrabFromStash(e);
                return;
            }
            y += 26f;
        }
    }
}