using System.Collections.Generic;
using UnityEngine;

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
    }

    void OnDisable() => CancelAll();

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

        if (GameInput.MagReleasePressed()) gun.PressMagazineRelease();
        if (GameInput.BoltCatchPressed()) gun.PressBoltCatch();

        switch (drag)
        {
            case DragKind.Handle: UpdateHandle(ray, held); break;
            case DragKind.SeatedMagazine: UpdateSeated(ray, held); break;
            case DragKind.HeldMagazine: UpdateHeld(ray, pressed, held); break;
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
        drag = DragKind.HeldMagazine;
    }

    void UpdateHeld(Ray ray, bool pressed, bool held)
    {
        if (heldVisual == null)
        {
            drag = DragKind.None;
            return;
        }

        Vector3 target = ray.origin + ray.direction * heldDistance;
        heldVisual.transform.position = Vector3.SmoothDamp(heldVisual.transform.position, target, ref heldVel,
                                                            Mathf.Max(0.01f, heldFollowTime));
        heldVisual.transform.rotation = CamRotation() * heldRotRelCam;

        // Dragged out of the gun: ends when you let go. Taken from the stash: ends on your next click.
        bool end = heldSticky ? pressed : !held;
        if (end) EndHeld();
    }

    void EndHeld()
    {
        InspectHotspot over = inspect.HoveredHotspot;
        bool overWell = magSlot != null && over == magSlot.Hotspot;

        if (overWell && gun.CanAcceptMagazine(heldContents) && gun.InsertMagazine(heldContents, heldType))
        {
            Destroy(heldVisual); // the gun's own magazine model slides in
            ClearHeld();
        }
        else
        {
            Stow();
        }
        drag = DragKind.None;
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