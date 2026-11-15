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
/// </summary>
[DisallowMultipleComponent]
public class MagazineItem : MonoBehaviour
{
    [Tooltip("Live contents. Read-only in practice: look at it while playing to see what is inside.")]
    public MagazineContents contents = new MagazineContents();

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
