using System;
using System.Collections.Generic;
using UnityEngine;

public enum AttachmentCategory
{
    IronSight,
    Optic,
    Stock,
    Magazine,
    Rail,     // Picatinny accessories: lights, lasers, grips, bipods...
    Muzzle    // Category exists, behaviour not implemented yet
}

/// <summary>
/// Stat modifiers an attachment applies to the weapon. Multipliers stack by multiplication
/// (1 = no change), ergonomics stacks by addition. Add new stats here and they flow through
/// WeaponAttachments.TotalStats automatically (just extend Combine there too).
/// </summary>
[Serializable]
public class AttachmentStats
{
    [Header("Handling")]
    [Tooltip("Multiplies recoil. 0.85 = 15% less recoil.")]
    public float recoilMultiplier = 1f;
    [Tooltip("Multiplies hold wobble / sway. Good stocks lower this.")]
    public float swayMultiplier = 1f;
    [Tooltip("Multiplies time to aim down sights. Above 1 = slower (heavy optics).")]
    public float adsTimeMultiplier = 1f;
    [Tooltip("Added to the weapon's ergonomics score.")]
    public float ergonomics = 0f;

    [Header("Ballistics")]
    public float spreadMultiplier = 1f;
    public float muzzleVelocityMultiplier = 1f;

    [Header("Magazine only")]
    [Tooltip("0 = not a magazine / no change.")]
    public int magazineCapacity = 0;

    /// <summary>Short UI text, e.g. "Recoil -15%, ADS +10%".</summary>
    public string Summary()
    {
        var parts = new List<string>();
        Pct(parts, "Recoil", recoilMultiplier);
        Pct(parts, "Sway", swayMultiplier);
        Pct(parts, "ADS time", adsTimeMultiplier);
        Pct(parts, "Spread", spreadMultiplier);
        Pct(parts, "Velocity", muzzleVelocityMultiplier);
        if (!Mathf.Approximately(ergonomics, 0f)) parts.Add("Ergo " + ergonomics.ToString("+0.#;-0.#"));
        if (magazineCapacity > 0) parts.Add("Cap " + magazineCapacity);
        return parts.Count > 0 ? string.Join(", ", parts) : "no stat change";
    }

    static void Pct(List<string> list, string label, float mult)
    {
        if (!Mathf.Approximately(mult, 1f)) list.Add(label + " " + (mult - 1f).ToString("+0%;-0%"));
    }
}

/// <summary>
/// One attachment type (create via Assets > Create > Weapons > Attachment Data).
/// The attachmentID is what saves and stats lookups use, so never rename it after shipping.
/// </summary>
[CreateAssetMenu(fileName = "NewAttachment", menuName = "Weapons/Attachment Data")]
public class AttachmentData : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Unique + stable. Auto-filled as category_name if empty, e.g. optic_reddot_01.")]
    public string attachmentID;
    public AttachmentCategory category;
    public string displayName;
    [TextArea] public string description;
    public Sprite icon;

    [Header("Prefab")]
    [Tooltip("Put an AttachmentPrefabInfo on the root to define its attach point and ADS point.")]
    public GameObject prefab;

    [Header("Fitment")]
    [Tooltip("Platform / mount tags this fits (AR15, AK, Picatinny...). Empty = fits anything. " +
             "Matched against the weapon's platformTags and the slot's slotTags.")]
    public string[] compatibleTags;
    [Tooltip("Rail slots this occupies on a Picatinny slot (1 slot = 10 mm).")]
    [Min(1)] public int railSlotsOccupied = 1;

    [Header("Physical")]
    public float weightKg = 0.1f;

    [Header("Stats")]
    public AttachmentStats stats = new AttachmentStats();

    [Header("Optic only")]
    [Tooltip("1 = no magnification (red dot / iron sight).")]
    public float magnification = 1f;

#if UNITY_EDITOR
    void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(attachmentID))
            attachmentID = category.ToString().ToLowerInvariant() + "_" + name.ToLowerInvariant().Replace(' ', '_');
        if (string.IsNullOrWhiteSpace(displayName)) displayName = name;
    }
#endif
}
