using System;
using UnityEngine;

/// <summary>
/// Physics-style motion for attachments snapping on, sliding along a rail, or popping off.
/// A damped spring pulls the object's LOCAL position and rotation to a target.
///
/// Damping is set as a RATIO: 1 = critically damped (the fastest approach that never overshoots), above 1 is
/// slightly softer. Anything below 1 overshoots the seat and pushes the part into the weapon, so the
/// defaults stay at 1 or higher and the part simply eases in along its insert direction.
/// Added and removed automatically by WeaponAttachments.
/// </summary>
public class AttachmentSpringMover : MonoBehaviour
{
    [Tooltip("Spring stiffness. Higher = snappier. Lower = slower, softer approach.")]
    public float stiffness = 150f;
    [Tooltip("1 = critically damped (no overshoot). Keep at 1 or above so parts never sink into the weapon.")]
    public float dampingRatio = 1.15f;

    Vector3 targetPos, vel, angVel;
    Quaternion targetRot = Quaternion.identity;
    bool destroyGameObject;
    Action onSettled;
    float age;

    public void Configure(float newStiffness, float newDampingRatio)
    {
        stiffness = Mathf.Max(1f, newStiffness);
        dampingRatio = Mathf.Max(1f, newDampingRatio); // never allow overshoot
    }

    public void MoveTo(Vector3 localPos, Quaternion localRot, bool destroyWhenDone = false, Action done = null)
    {
        targetPos = localPos;
        targetRot = localRot;
        destroyGameObject = destroyWhenDone;
        onSettled = done;
        age = 0f;
        enabled = true;
    }

    void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.033f);
        age += dt;
        float damping = 2f * Mathf.Max(1f, dampingRatio) * Mathf.Sqrt(stiffness);

        // Position spring
        Vector3 p = transform.localPosition;
        Vector3 posErr = targetPos - p;
        vel += (stiffness * posErr - damping * vel) * dt;
        p += vel * dt;
        transform.localPosition = p;

        // Rotation spring (angular error as a rotation vector, in parent space)
        Quaternion q = transform.localRotation;
        (targetRot * Quaternion.Inverse(q)).ToAngleAxis(out float deg, out Vector3 axis);
        if (deg > 180f) deg -= 360f;
        Vector3 rotErr = Mathf.Abs(deg) < 0.001f ? Vector3.zero : axis * (deg * Mathf.Deg2Rad);
        angVel += (stiffness * rotErr - damping * angVel) * dt;
        float w = angVel.magnitude;
        if (w > 1e-5f) q = Quaternion.AngleAxis(w * dt * Mathf.Rad2Deg, angVel / w) * q;
        transform.localRotation = q;

        bool settled = posErr.sqrMagnitude < 1e-7f && vel.sqrMagnitude < 4e-4f
                       && rotErr.sqrMagnitude < 1e-4f && angVel.sqrMagnitude < 2.5e-3f;
        if (settled || age > 2.5f) Finish();
    }

    void Finish()
    {
        transform.localPosition = targetPos;
        transform.localRotation = targetRot;
        vel = Vector3.zero;
        angVel = Vector3.zero;
        Action cb = onSettled;
        onSettled = null;
        if (destroyGameObject) { Destroy(gameObject); return; }
        cb?.Invoke();
        enabled = false;
    }
}