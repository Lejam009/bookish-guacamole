using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct AttachmentSave
{
    public string slotID;
    public string attachmentID;
    public int railIndex;
}

[Serializable]
public class DefaultAttachment
{
    public string slotID;
    public AttachmentData data;
    [Tooltip("-1 = the slot's default rail position.")]
    public int railIndex = -1;
}

/// <summary>
/// Put on the root of a weapon prefab (only on items whose ItemData category is Weapon).
/// Finds every AttachmentSlot under it and handles installing / removing / sliding attachments,
/// stat totals, weight, saving and the ADS anchor.
///
/// Keep the weapon prefab at scale 1 so attachment seating math stays in real meters.
///
/// Saving: implements IItemStateful, so HeldItemSlot.CaptureSave() / RestoreSave() keep the loadout
/// automatically. Assign the Database or saved IDs can't be turned back into attachments.
/// (Pickupable only finds ONE IItemStateful per prefab. When you add ammo/chamber state later, merge both
/// into one script's CaptureState/RestoreState, or ask me and I'll combine them.)
/// </summary>
public class WeaponAttachments : MonoBehaviour, IItemStateful
{
    [Serializable] class StateWrapper { public List<AttachmentSave> attachments = new List<AttachmentSave>(); }

    [Header("Setup")]
    public AttachmentDatabase database;
    [Tooltip("Platform tags of this weapon (AR15, AK...). Attachments with compatibleTags need a match.")]
    public string[] platformTags;
    [Tooltip("Weight of the bare weapon in kg.")]
    public float baseWeightKg = 3.4f;
    [Tooltip("Player-driven changes (inspect UI) are only allowed in a SafeZone.")]
    public bool requireSafeZoneToModify = true;
    [Tooltip("Installed at start if no save was restored. Great for stock iron sights.")]
    public List<DefaultAttachment> defaultLoadout = new List<DefaultAttachment>();

    public IReadOnlyList<AttachmentSlot> Slots => slots;
    public AttachmentStats TotalStats { get; private set; } = new AttachmentStats();
    public float TotalWeightKg { get; private set; }
    public bool CanPlayerModify => !requireSafeZoneToModify || SafeZone.PlayerInSafeZone;

    /// <summary>slot, old attachment (or null), new attachment (or null).</summary>
    public event Action<AttachmentSlot, AttachmentData, AttachmentData> Changed;
    /// <summary>Fires when an attachment finishes seating. Hook a "click" sound here.</summary>
    public event Action<AttachmentSlot> Settled;

    AttachmentSlot[] slots = new AttachmentSlot[0];
    bool initialized;

    void Awake()
    {
        slots = GetComponentsInChildren<AttachmentSlot>(true);
        foreach (AttachmentSlot s in slots) s.Owner = this;
        Recalculate();
    }

    void Start() => EnsureInitialized();

    void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;
        foreach (DefaultAttachment d in defaultLoadout)
        {
            AttachmentSlot slot = GetSlot(d.slotID);
            if (slot != null && d.data != null) Install(slot, d.data, d.railIndex, false);
        }
    }

    public AttachmentSlot GetSlot(string slotID)
    {
        foreach (AttachmentSlot s in slots)
            if (s.slotID == slotID) return s;
        return null;
    }

    // ------------------------------------------------------------------ rules

    public bool CanInstall(AttachmentSlot slot, AttachmentData data, out string reason)
    {
        reason = null;
        if (slot == null || data == null) { reason = "Missing slot or attachment."; return false; }
        if (slot.category != data.category) { reason = "Wrong category for this slot."; return false; }
        if (data.prefab == null) { reason = "Attachment has no prefab."; return false; }

        if (slot.whitelist != null && slot.whitelist.Length > 0 && Array.IndexOf(slot.whitelist, data) < 0)
        { reason = "Not accepted by this slot."; return false; }

        if (!TagsMatch(slot, data)) { reason = "Incompatible mount or platform."; return false; }

        if (slot.isRail && slot.railSlotCount - data.railSlotsOccupied < 0)
        { reason = "Too long for this rail."; return false; }

        return true;
    }

    bool TagsMatch(AttachmentSlot slot, AttachmentData data)
    {
        if (data.compatibleTags == null || data.compatibleTags.Length == 0) return true;
        foreach (string tag in data.compatibleTags)
            if (HasTag(platformTags, tag) || HasTag(slot.slotTags, tag)) return true;
        return false;
    }

    static bool HasTag(string[] list, string tag)
    {
        if (list == null) return false;
        foreach (string t in list)
            if (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // ------------------------------------------------------------------ install / remove

    /// <summary>Installs (replacing whatever is in the slot). Does NOT check the safe zone; the UI does.</summary>
    public bool Install(AttachmentSlot slot, AttachmentData data, int railIndex = -1, bool animate = true)
    {
        if (!CanInstall(slot, data, out string reason))
        {
            Debug.LogWarning($"[WeaponAttachments] Can't install '{(data != null ? data.attachmentID : "null")}' " +
                             $"in '{(slot != null ? slot.slotID : "null")}': {reason}", this);
            return false;
        }

        AttachmentData old = slot.Installed;
        if (old != null) DetachInstance(slot, animate);

        int maxIndex = slot.isRail ? slot.railSlotCount - data.railSlotsOccupied : 0;
        int index = slot.isRail
            ? Mathf.Clamp(railIndex < 0 ? slot.defaultRailIndex : railIndex, 0, maxIndex)
            : 0;

        GameObject go = Instantiate(data.prefab, slot.Mount);
        go.name = data.attachmentID;
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        // Attachments are visual parts of the weapon. Physics bits on them would be left switched on while the
        // gun is held (Pickupable only switches off what existed at spawn) and shove the player around.
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (Rigidbody r in go.GetComponentsInChildren<Rigidbody>(true)) Destroy(r);

        AttachmentPrefabInfo info = go.GetComponent<AttachmentPrefabInfo>();
        ComputeSeat(go.transform, info, out Vector3 seatPos, out Quaternion seatRot);

        slot.Installed = data;
        slot.Instance = go;
        slot.Info = info;
        slot.RailIndex = index;
        slot.seatPos = seatPos;
        slot.seatRot = seatRot;

        Vector3 targetPos = TargetPosition(slot, index);

        if (animate)
        {
            // Start displaced and slightly twisted, then spring into place.
            // The spring is critically damped (never overshoots into the gun) and the part slides straight in
            // along the slot's insert direction.
            float twist = slot.installTwistDegrees;
            go.transform.localPosition = targetPos + slot.insertOffset;
            go.transform.localRotation = seatRot * Quaternion.Euler(
                UnityEngine.Random.Range(-twist, twist), UnityEngine.Random.Range(-twist, twist), UnityEngine.Random.Range(-twist, twist));
            AttachmentSpringMover mover = go.AddComponent<AttachmentSpringMover>();
            mover.Configure(slot.installStiffness, slot.installDampingRatio);
            mover.MoveTo(targetPos, seatRot, false, () => Settled?.Invoke(slot));
        }
        else
        {
            go.transform.localPosition = targetPos;
            go.transform.localRotation = seatRot;
        }

        Recalculate();
        Changed?.Invoke(slot, old, data);
        return true;
    }

    /// <summary>Removes whatever is in the slot and returns it (so you can give it back to the inventory).</summary>
    public AttachmentData Remove(AttachmentSlot slot, bool animate = true)
    {
        if (slot == null || slot.Installed == null) return null;
        AttachmentData old = slot.Installed;
        DetachInstance(slot, animate);
        Recalculate();
        Changed?.Invoke(slot, old, null);
        return old;
    }

    /// <summary>Slides the installed attachment along its Picatinny rail by delta slots.</summary>
    public bool ShiftRail(AttachmentSlot slot, int delta)
    {
        if (slot == null || !slot.isRail || slot.Installed == null || slot.Instance == null) return false;

        int maxIndex = slot.railSlotCount - slot.Installed.railSlotsOccupied;
        int next = Mathf.Clamp(slot.RailIndex + delta, 0, maxIndex);
        if (next == slot.RailIndex) return false;

        slot.RailIndex = next;
        AttachmentSpringMover mover = slot.Instance.GetComponent<AttachmentSpringMover>();
        if (mover == null) mover = slot.Instance.AddComponent<AttachmentSpringMover>();
        mover.Configure(420f, 1.1f);   // rail slides are crisper than a full install, still no overshoot
        mover.MoveTo(TargetPosition(slot, next), slot.seatRot, false, () => Settled?.Invoke(slot));
        return true;
    }

    void DetachInstance(AttachmentSlot slot, bool animate)
    {
        GameObject go = slot.Instance;
        slot.Installed = null;
        slot.Instance = null;
        slot.Info = null;
        slot.RailIndex = 0;
        if (go == null) return;

        if (animate)
        {
            AttachmentSpringMover mover = go.GetComponent<AttachmentSpringMover>();
            if (mover == null) mover = go.AddComponent<AttachmentSpringMover>();
            mover.Configure(180f, 1.1f);
            mover.MoveTo(go.transform.localPosition + slot.insertOffset, go.transform.localRotation, true);
        }
        else
        {
            Destroy(go);
        }
    }

    static Vector3 TargetPosition(AttachmentSlot slot, int railIndex)
    {
        Vector3 rail = slot.isRail ? slot.railAxis.normalized * (railIndex * slot.railSlotPitch) : Vector3.zero;
        return slot.seatPos + rail;
    }

    /// <summary>
    /// Works out where the attachment root must sit (in the mount's space) so that its
    /// attachPoint lands exactly on the mount point with matching rotation.
    /// </summary>
    static void ComputeSeat(Transform root, AttachmentPrefabInfo info, out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;
        if (info == null || info.attachPoint == null) return;

        Vector3 relPos = root.InverseTransformPoint(info.attachPoint.position);
        Quaternion relRot = Quaternion.Inverse(root.rotation) * info.attachPoint.rotation;
        rot = Quaternion.Inverse(relRot);
        pos = -(rot * relPos);
    }

    // ------------------------------------------------------------------ stats & queries

    void Recalculate()
    {
        var t = new AttachmentStats();
        float weight = baseWeightKg;

        foreach (AttachmentSlot s in slots)
        {
            if (s.Installed == null) continue;
            AttachmentStats a = s.Installed.stats;
            t.recoilMultiplier *= a.recoilMultiplier;
            t.swayMultiplier *= a.swayMultiplier;
            t.adsTimeMultiplier *= a.adsTimeMultiplier;
            t.spreadMultiplier *= a.spreadMultiplier;
            t.muzzleVelocityMultiplier *= a.muzzleVelocityMultiplier;
            t.ergonomics += a.ergonomics;
            if (a.magazineCapacity > 0) t.magazineCapacity = a.magazineCapacity;
            weight += s.Installed.weightKg;
        }

        TotalStats = t;
        TotalWeightKg = weight;
    }

    /// <summary>
    /// Where the camera should line up when aiming: the first installed optic, otherwise the
    /// rear iron sight. WeaponSights uses it automatically.
    /// Returns null if nothing provides an ADS point (WeaponSights then uses its own Sight Anchor).
    /// </summary>
    public Transform GetAdsAnchor()
    {
        Transform t = FindAdsPoint(AttachmentCategory.Optic);
        return t != null ? t : FindAdsPoint(AttachmentCategory.IronSight);
    }

    Transform FindAdsPoint(AttachmentCategory category)
    {
        foreach (AttachmentSlot s in slots)
            if (s.Installed != null && s.category == category && s.Info != null && s.Info.adsPoint != null)
                return s.Info.adsPoint;
        return null;
    }

    /// <summary>Magnification of the active optic (1 if none). Divide your ADS FOV by this.</summary>
    public float CurrentMagnification
    {
        get
        {
            foreach (AttachmentSlot s in slots)
                if (s.Installed != null && s.category == AttachmentCategory.Optic)
                    return Mathf.Max(1f, s.Installed.magnification);
            return 1f;
        }
    }

    public AttachmentData GetInstalled(AttachmentCategory category)
    {
        foreach (AttachmentSlot s in slots)
            if (s.category == category && s.Installed != null) return s.Installed;
        return null;
    }

    // ------------------------------------------------------------------ saving

    // ---- IItemStateful (HeldItemSlot saves/loads this with the held item) ----
    public string CaptureState() => JsonUtility.ToJson(new StateWrapper { attachments = Capture() });

    public void RestoreState(string state)
    {
        if (string.IsNullOrEmpty(state)) return;
        StateWrapper wrapper = JsonUtility.FromJson<StateWrapper>(state);
        Restore(wrapper != null ? wrapper.attachments : null);
    }

    public List<AttachmentSave> Capture()
    {
        var list = new List<AttachmentSave>();
        foreach (AttachmentSlot s in slots)
            if (s.Installed != null)
                list.Add(new AttachmentSave { slotID = s.slotID, attachmentID = s.Installed.attachmentID, railIndex = s.RailIndex });
        return list;
    }

    public void Restore(List<AttachmentSave> saved)
    {
        initialized = true; // a restored loadout replaces the defaults
        foreach (AttachmentSlot s in slots) if (s.Installed != null) DetachInstance(s, false);

        if (saved != null)
        {
            foreach (AttachmentSave entry in saved)
            {
                AttachmentSlot slot = GetSlot(entry.slotID);
                AttachmentData data = database != null ? database.Get(entry.attachmentID) : null;
                if (slot == null || data == null)
                {
                    Debug.LogWarning($"[WeaponAttachments] Couldn't restore '{entry.attachmentID}' into '{entry.slotID}'.", this);
                    continue;
                }
                Install(slot, data, entry.railIndex, false);
            }
        }
        Recalculate();
    }
}