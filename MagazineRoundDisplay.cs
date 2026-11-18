using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shows EVERY round of a magazine as a real object, so you can read at a glance how full or empty it is (and so a
/// transparent magazine shows its ammo). Put it on the magazine prefab's root next to MagazineRoundPath.
///
/// HOW IT WORKS
///   - One visual per round in the magazine's real contents (MagazineContents): 30 rounds = 30 objects, 7 rounds = 7.
///   - Rounds sit on the curve from MagazineRoundPath. The next round to feed is at the feed lips (the first point) and the
///     stack builds down from there, so an emptying magazine empties from the BOTTOM, like a real one: when a round is fired
///     the rest glide up one step.
///   - Every round uses the model of its own ammo (AmmoData.cartridgePrefab, the same prefab that is thrown out of the ejection
///     port), so a tracer every fifth round looks right. Rounds with no model get a plain brass placeholder.
///   - It works on every magazine without any other setup: in the gun, lying on the ground, and on the magazine you drag
///     with the cursor.
///
/// SETUP
///   1. Remove any rounds baked into the magazine MODEL (the 3 you see now), the display draws all of them.
///   2. Add this component (it adds MagazineRoundPath) and shape the curve, see MagazineRoundPath.
///   3. The round model's pivot should be in the middle of the cartridge with the bullet tip pointing along +Z. If your cartridge
///      prefab was made differently, fix it with Round Position Offset / Round Rotation Offset below, so you never have to
///      touch the prefab.
///   4. Select the magazine in the Scene view to see a preview of a full magazine.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MagazineRoundPath))]
public class MagazineRoundDisplay : MonoBehaviour
{
    /// <summary>The last ammo database any gun gave to a magazine. Dropped magazines (which have no gun) use it.</summary>
    public static AmmoDatabase SharedAmmoDatabase;

    [Header("Rounds")]
    [Tooltip("Turns a round's ammo ID into its AmmoData (and cartridge model). Filled in automatically from the gun the magazine is put into. " +
             "Leave empty unless you want to force one.")]
    public AmmoDatabase ammoDatabase;
    [Tooltip("Model used for a round whose ammo has no Cartridge Prefab. Empty = a plain brass capsule.")]
    public GameObject fallbackRoundPrefab;
    [Tooltip("Placeholder capsule size in metres (overall length). 5.56x45 is about 0.057, 9x19 about 0.029.")]
    public float fallbackLengthMetres = 0.057f;
    [Tooltip("Placeholder capsule diameter in metres. 5.56x45 is about 0.0095, 9x19 about 0.0099.")]
    public float fallbackDiameterMetres = 0.0095f;
    [Tooltip("Shift every round in its own frame (metres: X = stagger side, Y = up, Z = bullet direction). For cartridge prefabs whose pivot is not in the middle.")]
    public Vector3 roundPositionOffset = Vector3.zero;
    [Tooltip("Extra rotation (degrees) on every round. For cartridge prefabs that do not point along +Z.")]
    public Vector3 roundRotationOffset = Vector3.zero;

    [Header("Layout")]
    [Tooltip("On: a FULL magazine spreads its rounds over the whole curve (set the curve from the top round to the bottom round of a full magazine). " +
             "Off: rounds are a fixed distance apart (Round Spacing).")]
    public bool fitToPath = true;
    [Tooltip("Distance between neighbouring rounds along the curve in metres, only used when Fit To Path is off. A staggered 5.56 magazine is about 0.008.")]
    public float roundSpacing = 0.008f;
    [Tooltip("Metres each round sits to the left / right of the curve, alternating, for a double stack that zig-zags. 0 = a single column.")]
    public float stagger = 0.0035f;
    [Tooltip("Swaps which side each round sits on (left becomes right). The lowest round, the one next to the follower, switches side with it: " +
             "use it when that round clips into the follower or the magazine wall.")]
    public bool flipStagger = false;

    [Header("Follower")]
    [Tooltip("The follower mesh that is already in the magazine prefab. It rides the curve just below the last round, pushing the stack up: " +
             "down at the bottom when the magazine is full, all the way up at the feed lips when it is empty. Leave empty for none.")]
    public Transform follower;
    [Tooltip("On: the follower is modelled where it sits in a FULL magazine (pushed down under a full stack, see Preview Rounds). " +
             "Off: it is modelled where it sits in an EMPTY magazine (pushed up under the feed lips).")]
    public bool followerModelledWhenFull = true;

    [Header("Motion")]
    [Tooltip("Seconds for the rounds to glide up one step after one is fired (or down when one is loaded).")]
    public float advanceTime = 0.05f;

    [Header("Scene View")]
    [Tooltip("Draw a preview of a full magazine along the curve when the magazine is selected in the editor.")]
    public bool showPreview = true;
    [Tooltip("Rounds in a FULL magazine of this model. Sizes the Scene view preview, and tells the follower how far down the magazine it was modelled.")]
    public int previewRounds = 30;

    // Where the follower sits in the prefab, relative to the curve. Serialized so that a copy of a live magazine (the one dragged out
    // of a gun) keeps the ORIGINAL pose instead of measuring the already-moved follower.
    [SerializeField, HideInInspector] bool followerRestCaptured;
    [SerializeField, HideInInspector] Vector3 followerRestPos;
    [SerializeField, HideInInspector] Quaternion followerRestRot = Quaternion.identity;

    // Unusual on purpose: it is the one child this script is allowed to delete, so it must never match a part of your model.
    const string RootName = "__MagazineRounds";

    class RoundSlot
    {
        public GameObject go;
        public GameObject source;
        public string ammoId;
        public Vector3 baseScale = Vector3.one;
    }

    MagazineRoundPath path;
    MagazineItem item;
    MagazineContents bound, lastContents;
    GameObject root, proceduralTemplate;
    readonly List<RoundSlot> slots = new List<RoundSlot>();

    float fill, fillVelocity;                 // rounds as a smooth number: glides to the real count
    int lastCount = -1, lastCapacity = -1;
    float lastSpacing = -1f, lastInvScale = -1f;
    AmmoDatabase lastDb;
    bool lastFlip;

    /// <summary>
    /// Show the rounds of this exact magazine instead of the nearest MagazineItem's. Used for the copy of a magazine you drag with the
    /// cursor, which has no MagazineItem of its own. Pass null to go back to the normal behaviour.
    /// </summary>
    public void Bind(MagazineContents contents) => bound = contents;

    /// <summary>Gives the display the ammo database to look cartridge models up in (does nothing if it already has one).</summary>
    public void UseAmmoDatabase(AmmoDatabase database)
    {
        if (database == null) return;
        SharedAmmoDatabase = database;
        if (ammoDatabase == null) ammoDatabase = database;
    }

    void Awake()
    {
        path = GetComponent<MagazineRoundPath>();

        // Remember the follower's modelled pose (in the curve's space) before anything moves it.
        if (follower != null && !followerRestCaptured)
        {
            followerRestPos = path.transform.InverseTransformPoint(follower.position);
            followerRestRot = Quaternion.Inverse(path.transform.rotation) * follower.rotation;
            followerRestCaptured = true;
        }

        // A copy of a magazine that was already showing rounds (the one dragged out of a gun) arrives with its old round objects:
        // they are stale, this display builds its own.
        Transform stale = transform.Find(RootName);
        if (stale != null)
        {
            stale.gameObject.SetActive(false);
            Destroy(stale.gameObject);
        }
    }

    void LateUpdate()
    {
        if (path == null) path = GetComponent<MagazineRoundPath>();
        if (bound == null && item == null) item = GetComponentInParent<MagazineItem>(); // added by WeaponAttachments just after the model is created

        MagazineContents c = bound != null ? bound : (item != null ? item.contents : null);
        if (c == null || path == null || !path.IsValid)
        {
            SetRootActive(false);
            return;
        }

        EnsureRoot();
        SetRootActive(true);

        int count = c.Count;
        int capacity = Mathf.Max(2, Mathf.Max(c.capacity, count));
        float invScale = 1f / path.WorldScale;
        float spacing = fitToPath ? path.Length / (capacity - 1) : Mathf.Max(0.0001f, roundSpacing) * invScale;
        float sideOffset = stagger * invScale;

        // The rounds move as one: they glide towards the real count instead of jumping.
        bool changed = false;
        if (c != lastContents)
        {
            lastContents = c;
            fill = count;
            fillVelocity = 0f;
            changed = true;
        }
        else if (fill != count)
        {
            fill = Mathf.SmoothDamp(fill, count, ref fillVelocity, Mathf.Max(0.001f, advanceTime));
            if (Mathf.Abs(fill - count) < 0.001f)
            {
                fill = count;
                fillVelocity = 0f;
            }
            changed = true;
        }
        if (count != lastCount || capacity != lastCapacity || flipStagger != lastFlip ||
            !Mathf.Approximately(spacing, lastSpacing) || !Mathf.Approximately(invScale, lastInvScale))
        {
            lastFlip = flipStagger;
            lastCount = count;
            lastCapacity = capacity;
            lastSpacing = spacing;
            lastInvScale = invScale;
            changed = true;
        }

        AmmoDatabase db = ammoDatabase != null ? ammoDatabase : SharedAmmoDatabase;
        if (db != lastDb)
        {
            lastDb = db;
            foreach (RoundSlot s in slots) s.ammoId = null; // models may resolve differently now
        }

        for (int k = 0; k < count; k++)
        {
            RoundSlot slot;
            if (k < slots.Count) slot = slots[k];
            else
            {
                slot = new RoundSlot();
                slots.Add(slot);
            }

            // The model is only looked up when this round's ammo is not the one already shown.
            string ammoId = c.rounds[k];
            bool needsPlacing = false;
            if (slot.go == null || slot.ammoId != ammoId)
            {
                GameObject prefab = PrefabFor(db, ammoId);
                if (slot.go == null || slot.source != prefab)
                {
                    BuildRound(slot, prefab);
                    needsPlacing = true;
                }
                slot.ammoId = ammoId;
            }
            if (!slot.go.activeSelf)
            {
                slot.go.SetActive(true);
                needsPlacing = true;
            }
            if (changed || needsPlacing) Place(slot, k, spacing, sideOffset, invScale);
        }

        for (int k = count; k < slots.Count; k++)
        {
            GameObject go = slots[k].go;
            if (go != null && go.activeSelf) go.SetActive(false);
        }

        if (follower != null && followerRestCaptured && changed) PlaceFollower(spacing, invScale);
    }

    /// <summary>
    /// The follower rides the same curve as the rounds, one step under the lowest one, using the same smooth number as the rounds:
    /// as a round is fired the stack and the follower rise together, so it always looks like it is pushing the last round.
    /// It moves by exactly as far as the stack does from the state it was modelled in, so its contact with the rounds is kept.
    /// </summary>
    void PlaceFollower(float spacing, float invScale)
    {
        int authoredRounds = Mathf.Max(2, previewRounds);
        float authoredSpacing = fitToPath ? path.Length / (authoredRounds - 1) : Mathf.Max(0.0001f, roundSpacing) * invScale;

        float modelledAt = (followerModelledWhenFull ? authoredRounds : 0) * authoredSpacing; // distance below the lips when modelled
        float now = fill * spacing;                                                           // and where the stack's bottom pushes it now

        path.Evaluate(modelledAt, out Vector3 p0, out Quaternion r0, true);
        path.Evaluate(now, out Vector3 p, out Quaternion r, true);

        // Moves and tilts with the curve: a banana magazine's follower follows the bend.
        Quaternion turn = r * Quaternion.Inverse(r0);
        Vector3 localPos = p + turn * (followerRestPos - p0);
        Quaternion localRot = turn * followerRestRot;

        Transform root = path.transform;
        follower.SetPositionAndRotation(root.TransformPoint(localPos), root.rotation * localRot);
    }

    // ------------------------------------------------------------------ placing rounds

    // Round k is the k-th from the BOTTOM of the stack (index 0 of MagazineContents.rounds); the last one is next to feed.
    void Place(RoundSlot slot, int k, float spacing, float sideOffset, float invScale)
    {
        float fromTop = Mathf.Max(0f, fill - 1f - k) * spacing; // distance below the feed lips
        path.Evaluate(fromTop, out Vector3 p, out Quaternion r);

        // A round keeps its column for life, so the zig-zag stays put as the stack rises.
        float side = LeftColumn(k) ? -sideOffset : sideOffset;

        Transform t = slot.go.transform;
        t.localPosition = p + r * (Vector3.right * side + roundPositionOffset * invScale);
        t.localRotation = r * Quaternion.Euler(roundRotationOffset);
        t.localScale = slot.baseScale * invScale;
    }

    /// <summary>Which column round k sits in (k counts from the bottom). The lowest round is on the left unless Flip Stagger is on.</summary>
    bool LeftColumn(int k) => ((k & 1) == 0) != flipStagger;

    void EnsureRoot()
    {
        if (root != null) return;
        root = new GameObject(RootName);
        root.layer = gameObject.layer;
        root.transform.SetParent(path.transform, false);
    }

    void SetRootActive(bool on)
    {
        if (root != null && root.activeSelf != on) root.SetActive(on);
    }

    // ------------------------------------------------------------------ round models

    GameObject PrefabFor(AmmoDatabase db, string ammoId)
    {
        AmmoData ammo = db != null ? db.Get(ammoId) : null;
        if (ammo != null && ammo.cartridgePrefab != null) return ammo.cartridgePrefab;
        if (fallbackRoundPrefab != null) return fallbackRoundPrefab;
        return ProceduralRound();
    }

    void BuildRound(RoundSlot slot, GameObject prefab)
    {
        if (slot.go != null) Destroy(slot.go);

        GameObject go = Instantiate(prefab, root.transform);
        go.name = "Round";
        go.SetActive(false);                 // the caller switches it on once it is placed
        StripToVisual(go);
        SetLayerRecursive(go.transform, gameObject.layer);

        slot.go = go;
        slot.source = prefab;
        slot.baseScale = prefab.transform.localScale;
    }

    /// <summary>
    /// A round in a magazine is only a picture: scripts, joints, colliders and rigidbodies from the ejected-cartridge prefab would
    /// make it fall out, despawn after a few seconds, or push the player around. Done in this order because scripts and joints
    /// can require a Rigidbody.
    /// </summary>
    static void StripToVisual(GameObject go)
    {
        foreach (MonoBehaviour m in go.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(m);
        foreach (Joint j in go.GetComponentsInChildren<Joint>(true)) DestroyImmediate(j);
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        foreach (Rigidbody r in go.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(r);
    }

    static void SetLayerRecursive(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
    }

    /// <summary>A plain brass capsule, used for rounds whose ammo has no model yet.</summary>
    GameObject ProceduralRound()
    {
        if (proceduralTemplate != null) return proceduralTemplate;

        proceduralTemplate = new GameObject("RoundTemplate");
        proceduralTemplate.transform.SetParent(root.transform, false);

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(proceduralTemplate.transform, false);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // the capsule's long axis (Y) becomes the bullet direction (Z)
        float d = Mathf.Max(0.001f, fallbackDiameterMetres);
        float l = Mathf.Max(d, fallbackLengthMetres);
        body.transform.localScale = new Vector3(d, l * 0.5f, d);       // a capsule is 1 wide and 2 tall

        MeshRenderer mr = body.GetComponent<MeshRenderer>();
        var brass = new Material(mr.sharedMaterial) { color = new Color(0.8f, 0.62f, 0.25f) };
        mr.sharedMaterial = brass;

        proceduralTemplate.SetActive(false);
        return proceduralTemplate;
    }

    // ------------------------------------------------------------------ Scene view preview

    void OnDrawGizmos()
    {
        if (Application.isPlaying || !showPreview) return;
        MagazineRoundPath p = GetComponent<MagazineRoundPath>();
        if (p == null) return;
        p.Rebuild();
        if (!p.IsValid) return;

        int n = Mathf.Max(2, previewRounds);
        float inv = 1f / p.WorldScale;
        float spacing = fitToPath ? p.Length / (n - 1) : Mathf.Max(0.0001f, roundSpacing) * inv;
        float half = Mathf.Max(fallbackLengthMetres, 0.001f) * 0.5f * inv;
        float radius = Mathf.Max(fallbackDiameterMetres, 0.001f) * 0.5f * inv;

        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = p.transform.localToWorldMatrix;
        for (int k = 0; k < n; k++)
        {
            p.Evaluate((n - 1 - k) * spacing, out Vector3 pos, out Quaternion rot);
            float side = LeftColumn(k) ? -stagger * inv : stagger * inv;
            Vector3 c = pos + rot * (Vector3.right * side + roundPositionOffset * inv);
            Quaternion q = rot * Quaternion.Euler(roundRotationOffset);
            Vector3 axis = q * Vector3.forward * half;

            Gizmos.color = k == n - 1 ? Color.yellow : new Color(1f, 0.8f, 0.3f, 0.9f);
            Gizmos.DrawLine(c - axis, c + axis);
            Gizmos.DrawWireSphere(c, radius);
        }
        Gizmos.matrix = previous;
    }

    void OnValidate()
    {
        advanceTime = Mathf.Max(0.001f, advanceTime);
        previewRounds = Mathf.Max(2, previewRounds);
        roundSpacing = Mathf.Max(0.0001f, roundSpacing);
    }
}