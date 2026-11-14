using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Master list of every attachment in the game. Used to turn saved attachmentIDs back into assets.
/// Create one (Assets > Create > Weapons > Attachment Database) and drag all AttachmentData into it.
/// </summary>
[CreateAssetMenu(fileName = "AttachmentDatabase", menuName = "Weapons/Attachment Database")]
public class AttachmentDatabase : ScriptableObject
{
    public List<AttachmentData> all = new List<AttachmentData>();

    Dictionary<string, AttachmentData> lookup;

    public AttachmentData Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (lookup == null) Rebuild();
        lookup.TryGetValue(id, out AttachmentData data);
        return data;
    }

    public IEnumerable<AttachmentData> InCategory(AttachmentCategory category)
    {
        foreach (AttachmentData a in all)
            if (a != null && a.category == category) yield return a;
    }

    void Rebuild()
    {
        lookup = new Dictionary<string, AttachmentData>();
        foreach (AttachmentData a in all)
        {
            if (a == null) continue;
            if (string.IsNullOrEmpty(a.attachmentID))
            {
                Debug.LogWarning($"[AttachmentDatabase] '{a.name}' has no attachmentID.", a);
                continue;
            }
            if (lookup.ContainsKey(a.attachmentID))
            {
                Debug.LogWarning($"[AttachmentDatabase] Duplicate attachmentID '{a.attachmentID}' on '{a.name}'.", a);
                continue;
            }
            lookup.Add(a.attachmentID, a);
        }
    }

    void OnEnable() => lookup = null;
    void OnValidate() => lookup = null;
}
