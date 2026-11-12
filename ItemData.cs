using UnityEngine;

public enum ItemCategory { Weapon, Gear, Consumable, Misc }

/// <summary>
/// One asset per kind of item (an AK, a canteen, a medkit...). Create with
/// Right-click > Create > Game > Item Data.
///
/// This is the "what is it" description. The physical object in the world is a prefab that has
/// a Rigidbody, colliders and a Pickupable component pointing back at this asset.
/// </summary>
[CreateAssetMenu(fileName = "NewItem", menuName = "Game/Item Data", order = 0)]
public class ItemData : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Permanent unique ID written into save files. Auto-filled from the asset name. Don't change it once saves exist.")]
    public string id;
    public string displayName;
    public ItemCategory category = ItemCategory.Misc;

    [Header("World Prefab")]
    [Tooltip("Model + colliders + Rigidbody + Pickupable. Also the prefab spawned into the hands when a save is loaded.")]
    public GameObject prefab;

    [Header("Physical")]
    public float massKg = 1f;
    [Tooltip("Pivot-to-front length in metres, plus a bit. Used to pull the item back when you push it into a wall.")]
    public float lengthMeters = 0.4f;

    [Header("Hold Pose (camera space)")]
    [Tooltip("IMPORTANT: put the model's pivot where the hand grips it, so the pose below is the hand position.")]
    public Vector3 holdPosition = new Vector3(0.18f, -0.2f, 0.4f);
    public Vector3 holdEuler = Vector3.zero;

    [Header("Sprint Pose (camera space)")]
    public Vector3 sprintPosition = new Vector3(0.14f, -0.3f, 0.3f);
    public Vector3 sprintEuler = new Vector3(18f, 22f, 8f);

    void OnValidate()
    {
        massKg = Mathf.Max(0.01f, massKg);
        if (string.IsNullOrEmpty(id)) id = name.ToLowerInvariant().Replace(' ', '_');
        if (string.IsNullOrEmpty(displayName)) displayName = name;
    }
}
