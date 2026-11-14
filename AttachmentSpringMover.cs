using System;
using UnityEngine;

/// <summary>
/// Physics-style motion for attachments snapping on, sliding along a rail, or popping off.
/// A damped spring (slightly under-damped) pulls the object's LOCAL position and rotation to a
/// target, so parts overshoot a few millimeters and settle with a "click" instead of lerping.
/// Added and removed automatically by WeaponAttachments.
/// </summary>
public class AttachmentSpringMover : MonoBehaviour
{
    [Tooltip("Spring stiffness. Higher = snappier.")]
    public float stiffness = 260f;
    [Tooltip("Damping ~20 gives about 10% overshoot (a small mechanical settle).")]
    public float damping = 20f;

    Vector3 targetPos, vel, angVel;
    Quaternion targetRot = Quaternion.identity;
    bool destroyGameObject;
    Action onSettled;
    float age;

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
        if (settled || age > 2f) Finish();
    }

    void Finish()
    {
        transform.localPosition = targetPos;
        transform.localRotation = targetRot;
        Action cb = onSettled;
        onSettled = null;
        if (destroyGameObject) { Destroy(gameObject); return; }
        cb?.Invoke();
        enabled = false;
    }
}
