using UnityEngine;

/// <summary>
/// Put this on the WORLD prefab of a weapon (same object as Pickupable). It tells WeaponADS how this
/// particular gun is aimed. Only used when the item's category is Weapon.
///
/// SETUP (the 30-second version):
///   1. In the prefab, add an empty child at the spot your EYE should look through:
///      the center of the rear sight aperture, or the center of the optic's rear lens.
///   2. Rotate it so its blue arrow (+Z) points down the barrel, same direction the gun shoots.
///      Its green arrow (+Y) should point up.
///   3. Drag that child into "Sight Anchor". Select the prefab to see a yellow ray in the Scene view.
///
/// WeaponADS then moves the gun so that anchor sits straight in front of the camera. No hand-tuned offsets
/// per gun, and it works for any model, any sight, any scale of the grip.
/// </summary>
[DisallowMultipleComponent]
public class WeaponSights : MonoBehaviour
{
    [Header("Sight Alignment")]
    [Tooltip("Empty child at the rear sight / optic lens, +Z pointing down the barrel. Leave empty to use the fallback pose below.")]
    public Transform sightAnchor;
    [Tooltip("Distance (metres) from the camera to the sight anchor when aiming. Smaller = sights closer to the eye, bigger on screen. 0.08-0.15 is typical for iron sights.")]
    [Min(0.02f)] public float eyeRelief = 0.12f;

    [Tooltip("Used only when no Sight Anchor is set: camera-space position of the weapon pivot when aiming.")]
    public Vector3 fallbackPosition = new Vector3(0f, -0.075f, 0.32f);
    public Vector3 fallbackEuler = Vector3.zero;

    [Header("Optic")]
    [Tooltip("Multiplies the camera FOV when fully aimed. Iron sights ~0.85, 2x optic ~0.6, 4x optic ~0.35.")]
    [Range(0.15f, 1f)] public float fovMultiplier = 0.85f;

    [Header("Handling")]
    [Tooltip("Seconds to settle on the sights for a light weapon. Heavier weapons take longer (see WeaponADS).")]
    public float adsTime = 0.28f;
    [Tooltip("Walking speed multiplier while fully aimed.")]
    [Range(0.2f, 1f)] public float moveSpeedMultiplier = 0.6f;
    [Tooltip("1 = normal. A bipod, stock or good ergonomics: lower. A long, front-heavy or unwieldy gun: higher.")]
    [Min(0f)] public float swayMultiplier = 1f;

    // ---- Attachments (optional: only when the prefab also has WeaponAttachments) ----
    WeaponAttachments attachments;
    bool attachmentsLooked;

    WeaponAttachments Attachments
    {
        get
        {
            if (!attachmentsLooked)
            {
                attachmentsLooked = true;
                attachments = GetComponent<WeaponAttachments>();
            }
            return attachments;
        }
    }

    /// <summary>The anchor aiming lines up with: the installed optic / rear sight, else this component's Sight Anchor.</summary>
    Transform ActiveAnchor
    {
        get
        {
            Transform fromAttachments = Attachments != null ? Attachments.GetAdsAnchor() : null;
            return fromAttachments != null ? fromAttachments : sightAnchor;
        }
    }

    /// <summary>FOV multiplier with the installed optic's magnification applied (4x = a quarter of the field).</summary>
    public float EffectiveFovMultiplier =>
        Mathf.Clamp(fovMultiplier / (Attachments != null ? Attachments.CurrentMagnification : 1f), 0.1f, 1f);

    /// <summary>Raise time with attachment weight/ergonomics applied (heavy optics are slower).</summary>
    public float EffectiveAdsTime =>
        adsTime * (Attachments != null ? Attachments.TotalStats.adsTimeMultiplier : 1f);

    /// <summary>Sway with attachment effects applied (a good stock steadies the gun).</summary>
    public float EffectiveSwayMultiplier =>
        swayMultiplier * (Attachments != null ? Attachments.TotalStats.swayMultiplier : 1f);

    /// <summary>
    /// The camera-space pose (position + rotation of this weapon's root) that puts the sights on the eye.
    /// Assumes the weapon root has scale 1, which is what you want for a physics object anyway.
    /// </summary>
    public void GetAimPose(out Vector3 localPos, out Quaternion localRot)
    {
        Transform anchor = ActiveAnchor;
        if (anchor == null)
        {
            localPos = fallbackPosition;
            localRot = Quaternion.Euler(fallbackEuler);
            return;
        }

        // Where the anchor sits relative to the weapon root (independent of where the gun is in the world)
        Quaternion invRoot = Quaternion.Inverse(transform.rotation);
        Vector3 anchorPos = invRoot * (anchor.position - transform.position);
        Quaternion anchorRot = invRoot * anchor.rotation;

        // Rotate the gun so the anchor's forward/up match the camera's, then shift it so the anchor is at (0,0,eyeRelief).
        localRot = Quaternion.Inverse(anchorRot);
        localPos = new Vector3(0f, 0f, eyeRelief) - localRot * anchorPos;
    }

    void OnDrawGizmosSelected()
    {
        if (sightAnchor == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(sightAnchor.position, 0.006f);
        Gizmos.DrawRay(sightAnchor.position, sightAnchor.forward * 0.6f);
        Gizmos.color = Color.green;
        Gizmos.DrawRay(sightAnchor.position, sightAnchor.up * 0.05f);
    }
}