using UnityEngine;

/// <summary>
/// Optional. Put this on a WEAPON prefab (next to Pickupable) to set where THIS gun goes in inspect mode.
/// Without it, WeaponInspect uses its default poses.
///
/// Positions are relative to your eye point, facing where your BODY faces (+X right, +Y up, +Z forward, metres; looking up or down does not move them) and are where the weapon's pivot
/// (the grip) ends up. Rotations are degrees.
///
/// Tuning tip: enter Play mode, hold R, then change the values on the gun's WeaponInspectPoses in the
/// Hierarchy and watch it move. Right-click the component header > Copy Component, stop Play mode,
/// then right-click > Paste Component Values on the prefab.
/// </summary>
[DisallowMultipleComponent]
public class WeaponInspectPoses : MonoBehaviour
{
    [Header("Pose A (first pose when you enter inspect mode)")]
    public Vector3 positionA = new Vector3(0f, -0.16f, 0.46f);
    public Vector3 eulerA = new Vector3(12f, -68f, 8f);

    [Header("Pose B (tap R to flip here, e.g. the other side of the gun)")]
    public Vector3 positionB = new Vector3(0f, -0.16f, 0.46f);
    public Vector3 eulerB = new Vector3(12f, 68f, -8f);

    [Header("Safe-zone rotation (hold right click while inspecting)")]
    [Tooltip("The point the gun spins around, in the weapon's own space relative to the object's origin, metres. " +
             "0,0,0 = the origin itself. Nudge it only if a weapon looks off-centre while turning.")]
    public Vector3 rotationOffset = Vector3.zero;

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.TransformPoint(rotationOffset), 0.02f);
    }
}