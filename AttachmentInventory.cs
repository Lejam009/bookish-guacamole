using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player's stash of loose attachments (what the inspect picker offers). Put it on the player.
/// Swap or feed this from your real inventory system later; the API is tiny on purpose.
/// </summary>
public class AttachmentInventory : MonoBehaviour
{
    [Serializable] public struct Stack { public AttachmentData data; public int count; }
    [Serializable] public struct SaveEntry { public string attachmentID; public int count; }

    public AttachmentDatabase database;
    [Tooltip("Handy for testing: attachments the player owns at start.")]
    public List<Stack> startingItems = new List<Stack>();

    readonly Dictionary<AttachmentData, int> counts = new Dictionary<AttachmentData, int>();

    void Awake()
    {
        foreach (Stack s in startingItems)
            if (s.data != null) Add(s.data, Mathf.Max(1, s.count));
    }

    public int Count(AttachmentData data) => data != null && counts.TryGetValue(data, out int c) ? c : 0;

    public void Add(AttachmentData data, int amount = 1)
    {
        if (data == null || amount <= 0) return;
        counts[data] = Count(data) + amount;
    }

    public bool TryTake(AttachmentData data, int amount = 1)
    {
        if (data == null || Count(data) < amount) return false;
        counts[data] -= amount;
        return true;
    }

    public IEnumerable<AttachmentData> Owned(AttachmentCategory category)
    {
        foreach (KeyValuePair<AttachmentData, int> kv in counts)
            if (kv.Value > 0 && kv.Key.category == category) yield return kv.Key;
    }

    public List<SaveEntry> Capture()
    {
        var list = new List<SaveEntry>();
        foreach (KeyValuePair<AttachmentData, int> kv in counts)
            if (kv.Value > 0) list.Add(new SaveEntry { attachmentID = kv.Key.attachmentID, count = kv.Value });
        return list;
    }

    public void Restore(List<SaveEntry> saved)
    {
        counts.Clear();
        if (saved == null || database == null) return;
        foreach (SaveEntry e in saved) Add(database.Get(e.attachmentID), e.count);
    }
}
