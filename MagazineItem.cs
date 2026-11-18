using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The rounds inside ONE physical magazine. It lives on the magazine's model (the prefab of a Magazine-category
/// AttachmentData), so every magazine in the world tracks its own rounds: fire one empty, swap to a full one, and each
/// keeps its own count.
///
/// You don't have to add this yourself: WeaponAttachments adds it to a magazine the moment it is installed. Add it to
/// the prefab only if you want to peek at it: while the game runs, the Inspector shows the live round list.
///
/// A magazine that is NOT on a gun (in the player's stash) has no model, so the stash (AttachmentInventory) keeps the same
/// MagazineContents object for it. The object just moves between the gun and the stash; it is never copied or recreated,
/// which is what keeps the count right.
///
/// This script also covers a magazine lying on the ground (MagazineItem.Drop): it falls out of a gun, keeps its rounds, and
/// you walk up and press F to pick it up into your stash. Nothing to set up for that either.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)] // after the weapon has been posed this frame, so a magazine sliding out of a well never trails the gun
public class MagazineItem : MonoBehaviour
{
    [Tooltip("Live contents. Read-only in practice: look at it while playing to see what is inside.")]
    public MagazineContents contents = new MagazineContents();

    // ================================================================== lying on the ground

    /// <summary>Every magazine currently lying on the ground.</summary>
    public static readonly List<MagazineItem> Dropped = new List<MagazineItem>();

    [Header("When lying on the ground")]
    [Tooltip("Which magazine this is (set when it is dropped).")]
    public AttachmentData data;
    [Tooltip("How close (metres) the player must be to pick it up.")]
    public float pickupRadius = 2f;

    bool isDropped;
    AttachmentInventory pickupInventory;
    float droppedAt;
    bool showPrompt;
    GUIStyle promptStyle;

    /// <summary>True while this magazine is a loose object on the ground.</summary>
    public bool IsDropped => isDropped;

    /// <summary>Spawns the magazine model with physics at a pose, rounds kept. Returns null (and stows it) if the asset has no prefab.</summary>
    public static MagazineItem Drop(AttachmentData data, MagazineContents contents, Vector3 position, Quaternion rotation,
                                    Vector3 scale, Vector3 impulse, AttachmentInventory inventory, Collider[] ignoreColliders)
    {
        if (data == null || contents == null) return null;
        if (data.prefab == null)
        {
            if (inventory != null) inventory.AddMagazine(data, contents);
            return null;
        }

        GameObject go = Object.Instantiate(data.prefab, position, rotation);
        go.name = data.attachmentID + " (dropped)";
        go.transform.localScale = scale;

        // Start clean: whatever physics the prefab had is replaced by one box that fits the model.
        foreach (Collider c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
        foreach (Rigidbody r in go.GetComponentsInChildren<Rigidbody>()) Object.Destroy(r);

        Bounds local = new Bounds(Vector3.zero, Vector3.one * 0.05f);
        bool has = false;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            Bounds wb = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = wb.center + new Vector3(
                    (i & 1) == 0 ? -wb.extents.x : wb.extents.x,
                    (i & 2) == 0 ? -wb.extents.y : wb.extents.y,
                    (i & 4) == 0 ? -wb.extents.z : wb.extents.z);
                Vector3 lc = go.transform.InverseTransformPoint(corner);
                if (!has) { local = new Bounds(lc, Vector3.zero); has = true; }
                else local.Encapsulate(lc);
            }
        }
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.center = local.center;
        box.size = Vector3.Max(local.size, Vector3.one * 0.01f);

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.25f;
        rb.AddForce(impulse, ForceMode.VelocityChange);

        // The magazine starts right next to the player: it must not shove them around.
        if (ignoreColliders != null)
            foreach (Collider c in ignoreColliders)
                if (c != null) Physics.IgnoreCollision(box, c);

        MagazineItem item = go.GetComponent<MagazineItem>();
        if (item == null) item = go.AddComponent<MagazineItem>();
        item.data = data;
        item.contents = contents;
        item.pickupInventory = inventory;
        item.droppedAt = Time.time;
        item.isDropped = true;
        Dropped.Add(item);
        return item;
    }

    // ================================================================== sliding out of the well first

    // While sliding the magazine has no physics: it rides the gun's magwell axis, then becomes a normal falling object.
    bool sliding;
    Transform slideMount;
    Vector3 slideStartLocal, slideDirLocal;
    Quaternion slideRotLocal;
    float slideTravelled, slideDistance, slideSpeed, slideMinSpeed;
    Rigidbody slideBody;
    BoxCollider slideBox;

    /// <summary>
    /// Like Drop, but the magazine first slides out of the magwell along the well's axis (the slot's Insert Offset direction, which follows the
    /// gun as it moves), picking up speed from gravity along that axis. Only after it has travelled slideDistance metres does it
    /// become a free falling object, so it can't drop straight through the magwell.
    /// </summary>
    public static MagazineItem DropGuided(AttachmentData data, MagazineContents contents, Vector3 position, Quaternion rotation, Vector3 scale,
                                          Transform mount, Vector3 insertOffsetLocal, float slideDistance, float startSpeed,
                                          AttachmentInventory inventory, Collider[] ignoreColliders)
    {
        MagazineItem item = Drop(data, contents, position, rotation, scale, Vector3.zero, inventory, ignoreColliders);
        if (item == null) return null;
        if (mount == null || slideDistance <= 0.0001f || insertOffsetLocal.sqrMagnitude < 1e-8f) return item;

        item.slideBody = item.GetComponent<Rigidbody>();
        item.slideBox = item.GetComponent<BoxCollider>();
        if (item.slideBody != null) item.slideBody.isKinematic = true;
        if (item.slideBox != null) item.slideBox.isTrigger = true;

        item.slideMount = mount;
        item.slideStartLocal = mount.InverseTransformPoint(position);
        item.slideRotLocal = Quaternion.Inverse(mount.rotation) * rotation;
        item.slideDirLocal = insertOffsetLocal.normalized;
        item.slideDistance = slideDistance;
        item.slideTravelled = 0f;
        item.slideMinSpeed = Mathf.Max(0.01f, startSpeed);
        item.slideSpeed = item.slideMinSpeed;
        item.sliding = true;
        return item;
    }

    void LateUpdate()
    {
        if (!sliding) return;

        if (slideMount == null) { EndSlide(Vector3.down); return; } // the gun is gone: just fall

        Vector3 axis = slideMount.TransformDirection(slideDirLocal).normalized;   // out of the well, in the gun's current pose
        float dt = Time.deltaTime;
        slideSpeed = Mathf.Max(slideMinSpeed, slideSpeed + Vector3.Dot(Physics.gravity, axis) * dt); // gravity along the well speeds it up, never stalls
        slideTravelled += slideSpeed * dt;

        if (slideTravelled >= slideDistance) { EndSlide(axis); return; }

        transform.SetPositionAndRotation(slideMount.TransformPoint(slideStartLocal) + axis * slideTravelled,
                                         slideMount.rotation * slideRotLocal);
    }

    void EndSlide(Vector3 axis)
    {
        sliding = false;
        if (slideBox != null) slideBox.isTrigger = false;
        if (slideBody != null)
        {
            slideBody.isKinematic = false;
            Pickupable.SetVelocity(slideBody, axis * slideSpeed); // leaves the well at the speed it had
        }
    }

    void OnDestroy() => Dropped.Remove(this);

    void Update()
    {
        showPrompt = false;
        if (!isDropped || pickupInventory == null || Time.time - droppedAt < 0.5f) return;
        if (NearestDropped(pickupInventory.transform.position, pickupRadius) != this) return;

        showPrompt = true;
        if (GameInput.InteractPressed())
        {
            pickupInventory.AddMagazine(data, contents);
            Destroy(gameObject);
        }
    }

    static MagazineItem NearestDropped(Vector3 from, float radius)
    {
        MagazineItem best = null;
        float bestDist = radius;
        foreach (MagazineItem m in Dropped)
        {
            float dist = Vector3.Distance(from, m.transform.position);
            if (dist <= bestDist) { bestDist = dist; best = m; }
        }
        return best;
    }

    void OnGUI()
    {
        if (!showPrompt) return;
        if (promptStyle == null)
            promptStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(Screen.width * 0.5f - 200f, Screen.height * 0.62f, 400f, 30f),
                  $"F: pick up magazine ({contents.Count}/{contents.capacity})", promptStyle);
    }

    // ================================================================== new magazines

    /// <summary>A brand-new magazine of this type, filled the way the asset says (Loaded Ammo / Loaded Rounds, or empty).</summary>
    public static MagazineContents CreateFresh(AttachmentData data, int fallbackCapacity = 30)
    {
        var c = new MagazineContents
        {
            magazineId = data != null ? data.attachmentID : "",
            caliber = data != null ? data.magazineCaliber : "",
            capacity = data != null && data.stats.magazineCapacity > 0 ? data.stats.magazineCapacity : Mathf.Max(1, fallbackCapacity)
        };
        if (data != null && data.loadedAmmo != null) c.Fill(data.loadedAmmo, data.loadedRounds);
        return c;
    }
}