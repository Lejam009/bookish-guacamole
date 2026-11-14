using UnityEngine;

/// <summary>
/// A place on a weapon prefab where ONE attachment of ONE category can go.
/// Create empty children on the weapon (e.g. "Slot_Optic_Top", "Slot_FrontSight", "Slot_Stock"),
/// position them where the attachment should sit, and add this component.
///
/// This is also the click target in inspect mode, so it needs a collider. If you don't add one,
/// a small trigger box is created automatically. If you already use your universal socket hitbox
/// prefab, put this component on (or above) that object and it will use its collider.
///
/// Picatinny rails: tick isRail, set how many 10 mm slots the rail has and which local axis it
/// runs along. Attachments can then be slid along it (this is how eye relief works).
/// </summary>
public class AttachmentSlot : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("Unique per weapon, used in saves. e.g. optic_top, front_sight, stock")]
    public string slotID = "slot";
    public string displayLabel = "Slot";
    public AttachmentCategory category;
    [Tooltip("Where the attachment's attach point lands. Defaults to this transform.")]
    public Transform mountPoint;

    [Header("Fitment")]
    [Tooltip("Mount tags this slot offers (Picatinny, MLOK, AKRail...). Combined with the weapon's platformTags.")]
    public string[] slotTags;
    [Tooltip("Optional hard whitelist. Empty = any attachment of this category that fits.")]
    public AttachmentData[] whitelist;

    [Header("Picatinny Rail")]
    public bool isRail;
    [Tooltip("Number of 10 mm rail slots along the rail.")]
    public int railSlotCount = 8;
    [Tooltip("Spacing between rail slots in meters. Real Picatinny pitch is ~10 mm.")]
    public float railSlotPitch = 0.01f;
    [Tooltip("Local axis the rail runs along (toward the muzzle is usually +Z).")]
    public Vector3 railAxis = Vector3.forward;
    public int defaultRailIndex = 0;

    [Header("Install Animation")]
    [Tooltip("Local offset the attachment starts from and slides in from (and pops out to).")]
    public Vector3 insertOffset = new Vector3(0f, 0.05f, 0f);

    [Header("Inspect Interaction")]
    [Tooltip("Turn OFF for magazine slots if your reload system already owns clicking them.")]
    public bool showInPicker = true;
    public Collider hitbox;
    public float hitboxSize = 0.06f;

    // ---- runtime state (managed by WeaponAttachments) ----
    public WeaponAttachments Owner { get; internal set; }
    public AttachmentData Installed { get; internal set; }
    public GameObject Instance { get; internal set; }
    public AttachmentPrefabInfo Info { get; internal set; }
    public int RailIndex { get; internal set; }
    internal Vector3 seatPos;
    internal Quaternion seatRot = Quaternion.identity;

    public Transform Mount => mountPoint != null ? mountPoint : transform;

    void Awake()
    {
        if (hitbox == null) hitbox = GetComponentInChildren<Collider>();
        if (hitbox == null)
        {
            BoxCollider box = gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = Vector3.one * hitboxSize;
            hitbox = box;
        }
    }

    void OnDrawGizmos()
    {
        Transform m = Mount;
        Gizmos.color = isRail ? new Color(1f, 0.7f, 0.1f) : new Color(0.2f, 0.8f, 1f);
        Gizmos.matrix = Matrix4x4.TRS(m.position, m.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one * 0.015f);

        if (isRail)
        {
            Vector3 axis = railAxis.normalized;
            for (int i = 0; i < railSlotCount; i++)
                Gizmos.DrawLine(axis * i * railSlotPitch + Vector3.up * 0.003f,
                                axis * i * railSlotPitch - Vector3.up * 0.003f);
        }
    }
}
