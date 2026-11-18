using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The CURVE the rounds of a magazine follow. Put it on the magazine prefab's root (next to MagazineRoundDisplay).
///
/// SETUP
///   1. Add the component. It creates 3 child points for you (RoundPath_Top, RoundPath_Mid, RoundPath_Bottom).
///   2. Move the points so the line runs through the middle of the stack of rounds, from the FEED LIPS (first point,
///      yellow in the Scene view) down to the FOLLOWER end (last point, red). Add more points for a banana mag: duplicate
///      a point, drag it into the list, and keep the list in order from top to bottom.
///   3. ROTATE each point: its blue arrow (local +Z) is where the bullet tip points, and its local +X is the side the
///      rounds stagger towards. On a curved AK magazine you tilt the lower points so the rounds fan along the curve.
///
/// The line is a smooth spline through the points (Catmull-Rom), and rotations are blended between them, so any magazine shape
/// works: straight stick magazines, banana curves, even drum magazines with enough points.
/// Everything is measured in the magazine's own space, so it follows the magazine when it moves, slides, or is dragged.
/// </summary>
[DisallowMultipleComponent]
public class MagazineRoundPath : MonoBehaviour
{
    [Tooltip("The points the rounds follow, in order from the FEED LIPS (where the next round to be fed sits) down to the follower end. " +
             "Each point's blue arrow (local +Z) is the bullet direction, its local +X is the stagger side.")]
    public List<Transform> points = new List<Transform>();

    [Tooltip("How finely the curve is measured (samples per segment between two points). 16 is plenty.")]
    [Range(4, 64)] public int samplesPerSegment = 16;

    [Tooltip("On: each round takes the blended rotation of the points around it (curved magazines). " +
             "Off: every round uses the first point's rotation (straight magazines, one less thing to set up).")]
    public bool followPointRotations = true;

    [Tooltip("Draw the curve and the points in the Scene view.")]
    public bool showGizmos = true;

    // Samples of the curve in this transform's own space.
    Vector3[] samplePos = new Vector3[0];
    Quaternion[] sampleRot = new Quaternion[0];
    float[] sampleDist = new float[0];
    float length;
    bool built;

    /// <summary>True when there are at least two points and the curve has a length.</summary>
    public bool IsValid
    {
        get
        {
            if (!built) Rebuild();
            return samplePos.Length >= 2 && length > 1e-6f;
        }
    }

    /// <summary>Length of the curve in this transform's own units (multiply by WorldScale for metres).</summary>
    public float Length
    {
        get
        {
            if (!built) Rebuild();
            return length;
        }
    }

    /// <summary>How much bigger this transform is in the world than its own units (1 for a normal scale-1 magazine).</summary>
    public float WorldScale
    {
        get
        {
            Vector3 s = transform.lossyScale;
            return Mathf.Max(1e-5f, (Mathf.Abs(s.x) + Mathf.Abs(s.y) + Mathf.Abs(s.z)) / 3f);
        }
    }

    /// <summary>Call after moving points from script. (The editor and play mode normally never need it: points are fixed on the prefab.)</summary>
    public void MarkDirty() => built = false;

    void OnEnable() => built = false;
    void OnValidate() => built = false;

    /// <summary>Adds three starting points when the component is first added in the editor.</summary>
    void Reset()
    {
        if (points.Count > 0) return;
        string[] names = { "RoundPath_Top", "RoundPath_Mid", "RoundPath_Bottom" };
        for (int i = 0; i < names.Length; i++)
        {
            var go = new GameObject(names[i]);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, -0.055f * i, 0f);
            points.Add(go.transform);
        }
    }

    // ------------------------------------------------------------------ the curve

    /// <summary>Re-measures the curve from the points.</summary>
    public void Rebuild()
    {
        built = true;

        var pos = new List<Vector3>();
        var rot = new List<Quaternion>();
        Quaternion inverseRoot = Quaternion.Inverse(transform.rotation);
        foreach (Transform t in points)
        {
            if (t == null) continue;
            pos.Add(transform.InverseTransformPoint(t.position));
            rot.Add(inverseRoot * t.rotation);
        }

        int n = pos.Count;
        if (n < 2)
        {
            samplePos = new Vector3[0];
            sampleRot = new Quaternion[0];
            sampleDist = new float[0];
            length = 0f;
            return;
        }

        int perSegment = Mathf.Max(2, samplesPerSegment);
        int total = (n - 1) * perSegment + 1;
        samplePos = new Vector3[total];
        sampleRot = new Quaternion[total];
        sampleDist = new float[total];

        int index = 0;
        for (int i = 0; i < n - 1; i++)
        {
            // Catmull-Rom needs a point before and after the segment: the ends reuse themselves.
            Vector3 p0 = pos[Mathf.Max(i - 1, 0)];
            Vector3 p1 = pos[i];
            Vector3 p2 = pos[i + 1];
            Vector3 p3 = pos[Mathf.Min(i + 2, n - 1)];
            for (int s = 0; s < perSegment; s++)
            {
                float t = (float)s / perSegment;
                samplePos[index] = CatmullRom(p0, p1, p2, p3, t);
                sampleRot[index] = followPointRotations ? Quaternion.Slerp(rot[i], rot[i + 1], t) : rot[0];
                index++;
            }
        }
        samplePos[index] = pos[n - 1];
        sampleRot[index] = followPointRotations ? rot[n - 1] : rot[0];

        float run = 0f;
        sampleDist[0] = 0f;
        for (int i = 1; i < total; i++)
        {
            run += Vector3.Distance(samplePos[i - 1], samplePos[i]);
            sampleDist[i] = run;
        }
        length = run;
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1
                     + (-p0 + p2) * t
                     + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                     + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    /// <summary>
    /// The pose on the curve at a distance from the FIRST point (the feed lips), in this transform's own space.
    /// Distances past either end are clamped to that end, unless extend is on: then the curve carries on in a straight line
    /// along its end direction (the follower of a full magazine sits one step past the last round).
    /// </summary>
    public void Evaluate(float distance, out Vector3 localPosition, out Quaternion localRotation, bool extend = false)
    {
        if (!IsValid)
        {
            localPosition = Vector3.zero;
            localRotation = Quaternion.identity;
            return;
        }

        float d = Mathf.Clamp(distance, 0f, length);
        float overshoot = extend ? distance - d : 0f;
        int lo = 0, hi = sampleDist.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (sampleDist[mid] <= d) lo = mid; else hi = mid;
        }
        float span = sampleDist[hi] - sampleDist[lo];
        float f = span > 1e-9f ? (d - sampleDist[lo]) / span : 0f;
        localPosition = Vector3.Lerp(samplePos[lo], samplePos[hi], f);
        localRotation = Quaternion.Slerp(sampleRot[lo], sampleRot[hi], f);

        if (overshoot > 0f)        // past the bottom end: carry on along the last bit of the curve
        {
            Vector3 dir = samplePos[samplePos.Length - 1] - samplePos[samplePos.Length - 2];
            if (dir.sqrMagnitude > 1e-12f) localPosition += dir.normalized * overshoot;
        }
        else if (overshoot < 0f)   // before the feed lips: carry on backwards along the first bit
        {
            Vector3 dir = samplePos[1] - samplePos[0];
            if (dir.sqrMagnitude > 1e-12f) localPosition += dir.normalized * overshoot;
        }
    }

    // ------------------------------------------------------------------ Scene view

    void OnDrawGizmos()
    {
        if (!showGizmos) return;
        if (!Application.isPlaying || !built) Rebuild(); // in the editor points get dragged around: always show the current shape
        if (samplePos.Length < 2) return;

        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
        for (int i = 1; i < samplePos.Length; i++) Gizmos.DrawLine(samplePos[i - 1], samplePos[i]);

        Gizmos.matrix = Matrix4x4.identity;
        for (int i = 0; i < points.Count; i++)
        {
            Transform t = points[i];
            if (t == null) continue;
            Gizmos.color = i == 0 ? Color.yellow : (i == points.Count - 1 ? Color.red : Color.white);
            Gizmos.DrawWireSphere(t.position, i == 0 ? 0.006f : 0.004f);
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(t.position, t.forward * 0.02f);
            Gizmos.color = Color.green;
            Gizmos.DrawRay(t.position, t.right * 0.008f);
        }
        Gizmos.matrix = previous;
    }
}