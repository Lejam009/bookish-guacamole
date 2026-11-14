using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Extra inputs for the attachment system, kept separate from GameInput so your file stays untouched.
/// Works with both the new Input System and the legacy Input Manager.
/// </summary>
public static class AttachmentInput
{
    public static bool RightHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
        return Input.GetMouseButton(1);
#endif
    }

    /// <summary>Cursor position in pixels, origin bottom-left.</summary>
    public static Vector2 PointerPosition()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }

    /// <summary>-1, 0 or +1 per frame (one step per wheel notch).</summary>
    public static int ScrollStep()
    {
#if ENABLE_INPUT_SYSTEM
        float y = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
#else
        float y = Input.mouseScrollDelta.y;
#endif
        return Mathf.Abs(y) < 0.01f ? 0 : (y > 0f ? 1 : -1);
    }
}

/// <summary>
/// Lets the player turn the weapon freely in inspect mode by holding right click, but only
/// while standing in a SafeZone. Mouse drag spins the weapon like a trackball and it keeps
/// spinning with inertia after you let go. Heavier loadouts (more attachments) feel heavier:
/// slower to throw around and they stop sooner. On leaving inspect mode the weapon eases
/// back to its rest rotation.
///
/// SETUP: it rotates a child "pivot", NOT the transform your inspect mode drives, so the two
/// never fight. Make an empty child of the weapon root at its center of mass, parent the
/// weapon's meshes + AttachmentSlots under it, and assign it as rotationPivot.
///
/// Call SetInspecting(true/false) from your inspect mode on enter/exit.
/// Read WeaponInspectRotator.IsRotating (or RotatingChanged) to freeze camera movement
/// and block other click actions while the player is spinning the weapon.
/// </summary>
public class WeaponInspectRotator : MonoBehaviour
{
    [Header("References")]
    public Transform rotationPivot;
    public Camera viewCamera;
    [Tooltip("Optional: used for weight-based feel.")]
    public WeaponAttachments attachments;

    [Header("Feel")]
    [Tooltip("Degrees of rotation per mouse count.")]
    public float degreesPerCount = 0.35f;
    [Tooltip("How quickly the spin dies after release. Higher = stops sooner.")]
    public float spinDamping = 4f;
    public float maxSpinSpeed = 900f;
    [Tooltip("Weapon weight (kg) that feels 'neutral'. Heavier is sluggish, lighter is snappy.")]
    public float referenceWeightKg = 3.5f;
    [Tooltip("Seconds to ease back to the rest rotation after inspect ends.")]
    public float resetTime = 0.35f;

    [Header("Rules")]
    public bool requireSafeZone = true;

    public static bool IsRotating { get; private set; }
    public event Action<bool> RotatingChanged;

    Quaternion restLocalRot = Quaternion.identity;
    Vector3 spin;                 // world-space angular velocity, degrees/second
    bool inspecting, returning;

    void Awake()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        if (attachments == null) attachments = GetComponentInParent<WeaponAttachments>();
        if (rotationPivot != null) restLocalRot = rotationPivot.localRotation;
    }

    void OnDisable() => SetRotating(false);

    public void SetInspecting(bool on)
    {
        inspecting = on;
        if (on) { returning = false; }
        else { SetRotating(false); returning = true; }
    }

    /// <summary>Eases back to the rest rotation (e.g. when the weapon flips to the chamber-side pose).</summary>
    public void ResetRotation()
    {
        spin = Vector3.zero;
        returning = true;
    }

    void Update()
    {
        if (rotationPivot == null || viewCamera == null) return;
        float dt = Mathf.Min(Time.deltaTime, 0.033f);

        bool allowed = inspecting && (!requireSafeZone || SafeZone.PlayerInSafeZone);
        bool held = allowed && AttachmentInput.RightHeld();
        SetRotating(held);

        float mass = attachments != null ? attachments.TotalWeightKg : referenceWeightKg;
        float massRatio = Mathf.Max(0.1f, mass) / Mathf.Max(0.1f, referenceWeightKg);

        if (held)
        {
            returning = false;
            // Heavier weapon = a given drag turns it less.
            float feel = Mathf.Clamp(1f / Mathf.Sqrt(massRatio), 0.6f, 1.3f);
            Vector2 d = GameInput.LookDelta() * degreesPerCount * feel;

            // Trackball: dragging right brings the near side of the gun to the right, up brings it up.
            Transform c = viewCamera.transform;
            Vector3 deltaDeg = c.up * -d.x + c.right * d.y;
            Vector3 target = deltaDeg / Mathf.Max(dt, 0.0001f);
            spin = Vector3.Lerp(spin, target, 1f - Mathf.Exp(-25f * dt));
            spin = Vector3.ClampMagnitude(spin, maxSpinSpeed);
        }
        else
        {
            spin *= Mathf.Exp(-spinDamping * Mathf.Clamp(massRatio, 0.7f, 1.6f) * dt);
            if (spin.sqrMagnitude < 1f) spin = Vector3.zero;
        }

        if (spin.sqrMagnitude > 0f)
        {
            float angle = spin.magnitude * dt;
            rotationPivot.rotation = Quaternion.AngleAxis(angle, spin.normalized) * rotationPivot.rotation;
        }

        if (returning && !held)
        {
            float t = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, resetTime) * 4f);
            rotationPivot.localRotation = Quaternion.Slerp(rotationPivot.localRotation, restLocalRot, t);
            spin *= 1f - t;
            if (Quaternion.Angle(rotationPivot.localRotation, restLocalRot) < 0.1f)
            {
                rotationPivot.localRotation = restLocalRot;
                spin = Vector3.zero;
                returning = false;
            }
        }
    }

    void SetRotating(bool value)
    {
        if (IsRotating == value) return;
        IsRotating = value;
        RotatingChanged?.Invoke(value);
    }
}
