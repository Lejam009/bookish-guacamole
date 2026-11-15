using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player's stash of loose attachments (what the inspect picker offers). Put it on the player.
/// Swap or feed this from your real inventory system later; the API is tiny on purpose.
/// </summary>
public class AttachmentInventory : MonoBehaviour
{
    [Serializable]
    public class Stack
    {
        public AttachmentData data;
        public int count = 1;
        [Tooltip("MAGAZINES ONLY: the ammo these magazines start with. Empty = use the magazine asset's Loaded Ammo (and if that is empty too, they start empty).")]
        public AmmoData ammo;
        [Tooltip("MAGAZINES ONLY: rounds in EACH of these magazines when Ammo is set. -1 = full.")]
        public int rounds = -1;
    }
    [Serializable] public struct SaveEntry { public string attachmentID; public int count; public List<string> magazineJson; }

    public AttachmentDatabase database;
    [Tooltip("Handy for testing: attachments the player owns at start.")]
    public List<Stack> startingItems = new List<Stack>();

    readonly Dictionary<AttachmentData, int> counts = new Dictionary<AttachmentData, int>();
    // Magazines are kept one by one (each with its own rounds) instead of as a plain count.
    readonly Dictionary<AttachmentData, List<MagazineContents>> magazines = new Dictionary<AttachmentData, List<MagazineContents>>();

    void Awake()
    {
        foreach (Stack s in startingItems)
        {
            if (s == null || s.data == null) continue;
            int n = Mathf.Max(1, s.count);
            if (s.data.category == AttachmentCategory.Magazine && s.ammo != null)
            {
                for (int i = 0; i < n; i++)
                {
                    MagazineContents m = MagazineItem.CreateFresh(s.data);
                    m.rounds.Clear();
                    m.Fill(s.ammo, s.rounds);
                    AddMagazine(s.data, m);
                }
            }
            else Add(s.data, n);
        }
    }

    static bool IsMagazine(AttachmentData d) => d != null && d.category == AttachmentCategory.Magazine;

    public int Count(AttachmentData data)
    {
        if (data == null) return 0;
        if (IsMagazine(data)) return magazines.TryGetValue(data, out List<MagazineContents> list) ? list.Count : 0;
        return counts.TryGetValue(data, out int c) ? c : 0;
    }

    /// <summary>Adds attachments. Magazines added this way are brand new (filled as their asset says).</summary>
    public void Add(AttachmentData data, int amount = 1)
    {
        if (data == null || amount <= 0) return;
        if (IsMagazine(data)) { for (int i = 0; i < amount; i++) AddMagazine(data, null); return; }
        counts[data] = Count(data) + amount;
    }

    /// <summary>Puts a specific magazine (with its rounds) into the stash. Null contents = a brand-new one.</summary>
    public void AddMagazine(AttachmentData data, MagazineContents contents)
    {
        if (!IsMagazine(data)) return;
        if (!magazines.TryGetValue(data, out List<MagazineContents> list)) magazines[data] = list = new List<MagazineContents>();
        list.Add(contents ?? MagazineItem.CreateFresh(data));
    }

    /// <summary>Every magazine of this type in the stash, each with its own rounds.</summary>
    public IReadOnlyList<MagazineContents> MagazinesOf(AttachmentData data)
        => magazines.TryGetValue(data, out List<MagazineContents> list) ? list : (IReadOnlyList<MagazineContents>)new List<MagazineContents>();

    /// <summary>Takes one specific magazine out of the stash (the same object you got from MagazinesOf).</summary>
    public bool TakeMagazine(AttachmentData data, MagazineContents contents)
        => data != null && magazines.TryGetValue(data, out List<MagazineContents> list) && list.Remove(contents);

    public bool TryTake(AttachmentData data, int amount = 1)
    {
        if (data == null || Count(data) < amount) return false;
        if (IsMagazine(data))
        {
            List<MagazineContents> list = magazines[data];
            list.RemoveRange(list.Count - amount, amount);
            return true;
        }
        counts[data] -= amount;
        return true;
    }

    public IEnumerable<AttachmentData> Owned(AttachmentCategory category)
    {
        if (category == AttachmentCategory.Magazine)
        {
            foreach (KeyValuePair<AttachmentData, List<MagazineContents>> kv in magazines)
                if (kv.Value.Count > 0) yield return kv.Key;
            yield break;
        }
        foreach (KeyValuePair<AttachmentData, int> kv in counts)
            if (kv.Value > 0 && kv.Key.category == category) yield return kv.Key;
    }

    public List<SaveEntry> Capture()
    {
        var list = new List<SaveEntry>();
        foreach (KeyValuePair<AttachmentData, int> kv in counts)
            if (kv.Value > 0) list.Add(new SaveEntry { attachmentID = kv.Key.attachmentID, count = kv.Value });
        foreach (KeyValuePair<AttachmentData, List<MagazineContents>> kv in magazines)
        {
            if (kv.Value.Count == 0) continue;
            var json = new List<string>();
            foreach (MagazineContents m in kv.Value) json.Add(JsonUtility.ToJson(m));
            list.Add(new SaveEntry { attachmentID = kv.Key.attachmentID, count = kv.Value.Count, magazineJson = json });
        }
        return list;
    }

    public void Restore(List<SaveEntry> saved)
    {
        counts.Clear();
        magazines.Clear();
        if (saved == null || database == null) return;
        foreach (SaveEntry e in saved)
        {
            AttachmentData data = database.Get(e.attachmentID);
            if (data == null) continue;
            if (IsMagazine(data) && e.magazineJson != null && e.magazineJson.Count > 0)
                foreach (string json in e.magazineJson) AddMagazine(data, JsonUtility.FromJson<MagazineContents>(json));
            else
                Add(data, e.count);
        }
    }
}