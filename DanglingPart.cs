using UnityEngine;

/// <summary>
/// A small part that hangs on a hinge and flops around as the weapon moves: a sling swivel, a dust cover flap,
/// a loose strap loop, a dangling tag.
///
/// WHY THIS EXISTS
/// Unity's Rigidbody + Hinge Joint only reacts when its parent is moved BY PHYSICS. The held weapon is moved by setting
/// its transform every frame, which physics sees as a teleport: no speed, no inertia, so a joint on it never swings.
/// This script does the same job without any Rigidbody. Every frame it looks at how the part's pivot is accelerating
/// through the world (walking, turning, firing, inspecting) and swings the part as a pendulum would.
///
/// SETUP
///   1. On the part (e.g. SideSling): REMOVE its Rigidbody and Hinge Joint (they are not needed and fight the weapon).
///   2. Add this component.
///   3. The part's PIVOT must sit on the hinge (the HingeJoint "Anchor 0,0,0" setup you already had does this).
///   4. Set Axis to the hinge axis in the part's own space (your joint used 0,0,1) and the Min/Max angles
///      (your joint used 0 to 180). If it swings the wrong way, flip the axis sign: 0,0,-1.
/// Select the part to see the hinge axis (blue), the weight point (yellow) and the swing range (red) in the Scene view.
/// </summary>
[DefaultExecutionOrder(60)] // after HeldItemSlot (50) and Firearm (55): the weapon has already moved this frame
public class DanglingPart : MonoBehaviour
{
    [Header("Hinge")]
    [Tooltip("The hinge axis in the part's own space. Same idea as the Axis field on a Hinge Joint. If it swings the wrong way, flip the sign.")]
    public Vector3 axis = new Vector3(0f, 0f, 1f);
    [Tooltip("Lowest swing angle in degrees, measured from the pose the part has in the prefab.")]
    public float minAngle = 0f;
    [Tooltip("Highest swing angle in degrees, measured from the pose the part has in the prefab.")]
    public float maxAngle = 180f;
    [Tooltip("How much speed is kept when the part hits the end of its range. 0 = dead stop, 0.3 = a small clack.")]
    [Range(0f, 0.9f)] public float bounciness = 0.25f;

    [Header("Weight")]
    [Tooltip("ON: the part's centre is taken from its mesh bounds (good for most parts). OFF: use Center Of Mass Offset below.")]
    public bool autoCenterOfMass = true;
    [Tooltip("Where the weight sits, in METRES from the pivot, in the part's rotation space (not affected by object scale). Only used when Auto Center Of Mass is off.")]
    public Vector3 centerOfMassOffset = new Vector3(0f, -0.02f, 0f);

    [Header("Feel")]
    [Tooltip("Resistance to swinging (per second). Higher = the part settles quicker and feels stiffer or greasier.")]
    public float damping = 6f;
    [Tooltip("Constant friction in the hinge, radians per second squared. Stops tiny endless wobbling.")]
    public float friction = 1.5f;
    [Tooltip("How strongly the weapon's movement throws the part around. 1 = realistic, higher = more dramatic.")]
    [Range(0f, 4f)] public float motionStrength = 1f;
    [Tooltip("Safety limit on the movement used (m/s^2). Stops the part spinning wildly when the weapon is teleported (equip, inspect).")]
    public float maxAcceleration = 120f;
    [Tooltip("Smooths the measured movement so camera jitter doesn't buzz the part. Higher = more responsive, lower = smoother.")]
    public float responseSharpness = 30f;

    // ---- runtime ----
    Quaternion restLocalRot;
    Vector3 comDirLocal;       // weight position from pivot, metres, in the part's REST orientation frame
    float angle, angVel;       // radians, relative to rest
    bool primed;
    Vector3 prevPos, prevVel, smoothAccel;
    Quaternion prevParentRot;
    float prevParentSpin, smoothParentSpinAccel;

    Quaternion ParentRot => transform.parent != null ? transform.parent.rotation : Quaternion.identity;

    void Awake()
    {
        restLocalRot = transform.localRotation;
        if (axis.sqrMagnitude < 0.0001f) axis = Vector3.forward;
        axis.Normalize();

        // Weight position as a world-metre offset from the pivot, then turned into the part's own (unscaled) frame.
        Vector3 offsetWorld;
        if (autoCenterOfMass && TryGetBoundsCenter(out Vector3 center))
            offsetWorld = center - transform.position;
        else
            offsetWorld = ParentRot * restLocalRot * centerOfMassOffset;

        comDirLocal = Quaternion.Inverse(ParentRot * restLocalRot) * offsetWorld;
        if (comDirLocal.sqrMagnitude < 0.000001f) comDirLocal = new Vector3(0f, -0.005f, 0f); // never exactly on the pivot
    }

    bool TryGetBoundsCenter(out Vector3 center)
    {
        Renderer[] rends = GetComponentsInChildren<Renderer>();
        center = Vector3.zero;
        if (rends.Length == 0) return false;
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        center = b.center;
        return true;
    }

    void OnEnable() { primed = false; }

    void LateUpdate()
    {
        float frameDt = Time.deltaTime;
        if (frameDt <= 0f) return;

        Vector3 pos = transform.position;
        Quaternion parentRot = ParentRot;

        // --- Measure how the pivot is moving through the world ---
        Quaternion hingeFrame0 = parentRot * restLocalRot;
        Vector3 axisW = (hingeFrame0 * axis).normalized;

        float spin = 0f; // how fast the parent turns about the hinge axis (rad/s)
        if (primed)
        {
            Vector3 vel = (pos - prevPos) / frameDt;
            Vector3 accel = (vel - prevVel) / frameDt;
            accel = Vector3.ClampMagnitude(accel, maxAcceleration);

            Quaternion dq = parentRot * Quaternion.Inverse(prevParentRot);
            dq.ToAngleAxis(out float deg, out Vector3 spinAxis);
            if (deg > 180f) deg -= 360f;
            spin = Vector3.Dot(spinAxis, axisW) * deg * Mathf.Deg2Rad / frameDt;
            float spinAccel = Mathf.Clamp((spin - prevParentSpin) / frameDt, -maxAcceleration * 4f, maxAcceleration * 4f);

            float k = 1f - Mathf.Exp(-responseSharpness * frameDt);
            smoothAccel = Vector3.Lerp(smoothAccel, accel, k);
            smoothParentSpinAccel = Mathf.Lerp(smoothParentSpinAccel, spinAccel, k);
            prevVel = vel;
        }
        else
        {
            prevVel = Vector3.zero;
            smoothAccel = Vector3.zero;
            smoothParentSpinAccel = 0f;
            primed = true;
        }
        prevPos = pos;
        prevParentRot = parentRot;
        prevParentSpin = spin;

        // In the part's frame the world "falls" with gravity minus the pivot's own acceleration
        // (accelerate the weapon forward and the part swings back, like a weight hanging in a car).
        Vector3 gEff = Physics.gravity - smoothAccel * motionStrength;

        // --- Pendulum, in small substeps so quick movements stay stable ---
        int steps = Mathf.Clamp(Mathf.CeilToInt(frameDt / 0.004f), 1, 12);
        float h = frameDt / steps;
        float lo = minAngle * Mathf.Deg2Rad, hi = maxAngle * Mathf.Deg2Rad;

        for (int i = 0; i < steps; i++)
        {
            Quaternion frame = hingeFrame0 * Quaternion.AngleAxis(angle * Mathf.Rad2Deg, axis);
            Vector3 r = frame * comDirLocal;                       // pivot -> weight, world metres
            Vector3 rPerp = r - axisW * Vector3.Dot(r, axisW);
            float armSq = Mathf.Max(0.00005f, rPerp.sqrMagnitude); // distance from the axis, squared

            // gravity (and movement) torque on the weight, divided by its moment of inertia (m * r^2, mass cancels)
            float alpha = Vector3.Dot(axisW, Vector3.Cross(r, gEff)) / armSq;
            alpha -= smoothParentSpinAccel * motionStrength;       // the weapon twisting about the hinge drags the part the other way
            alpha -= damping * angVel;
            if (Mathf.Abs(angVel) > 0.0001f) alpha -= Mathf.Sign(angVel) * Mathf.Min(friction, Mathf.Abs(angVel) / h);

            angVel += alpha * h;
            angle += angVel * h;

            if (angle < lo) { angle = lo; angVel = angVel < 0f ? -angVel * bounciness : angVel; }
            else if (angle > hi) { angle = hi; angVel = angVel > 0f ? -angVel * bounciness : angVel; }
        }

        transform.localRotation = restLocalRot * Quaternion.AngleAxis(angle * Mathf.Rad2Deg, axis);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 p = transform.position;
        Quaternion parentRot = ParentRot;
        Quaternion rest = Application.isPlaying ? restLocalRot : transform.localRotation;
        Quaternion frame0 = parentRot * rest;
        Vector3 axisW = frame0 * (axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.forward);

        Gizmos.color = Color.blue;
        Gizmos.DrawLine(p - axisW * 0.03f, p + axisW * 0.03f);

        Vector3 com;
        if (Application.isPlaying) com = p + frame0 * Quaternion.AngleAxis(angle * Mathf.Rad2Deg, axis) * comDirLocal;
        else if (autoCenterOfMass && TryGetBoundsCenter(out Vector3 c)) com = c;
        else com = p + frame0 * centerOfMassOffset;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(com, 0.004f);
        Gizmos.DrawLine(p, com);

        // swing range (red): where the weight ends up at the two limits
        Vector3 rest0 = com - p;
        if (Application.isPlaying) rest0 = frame0 * comDirLocal;
        Gizmos.color = Color.red;
        Gizmos.DrawLine(p, p + Quaternion.AngleAxis(minAngle, axisW) * rest0);
        Gizmos.DrawLine(p, p + Quaternion.AngleAxis(maxAngle, axisW) * rest0);
    }
}
