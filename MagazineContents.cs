using System;
using System.Collections.Generic;

/// <summary>
/// The rounds inside ONE magazine. A plain serializable object (not a component), so a future
/// reload system can keep these in plate-carrier pouches, hand them to Firearm.InsertMagazine(),
/// and save them with JsonUtility.
///
/// Rounds are a stack of ammo IDs: index 0 is the bottom, the LAST entry is the round that feeds next.
/// Mixed ammo (a tracer every fifth round, say) works for free.
/// </summary>
[Serializable]
public class MagazineContents
{
    [UnityEngine.Tooltip("AttachmentData.attachmentID of the magazine type (empty for a virtual magazine with no model).")]
    public string magazineId = "";
    [UnityEngine.Tooltip("Only cartridges with this caliber fit. Empty = takes the caliber of the first round loaded.")]
    public string caliber = "";
    public int capacity = 30;
    public List<string> rounds = new List<string>();

    public int Count => rounds.Count;
    public bool IsEmpty => rounds.Count == 0;
    public bool IsFull => rounds.Count >= capacity;
    /// <summary>How many more rounds fit (capacity minus what is inside).</summary>
    public int FreeSpace => Math.Max(0, capacity - rounds.Count);
    /// <summary>0 = empty, 1 = full.</summary>
    public float FillFraction => capacity > 0 ? (float)rounds.Count / capacity : 0f;
    /// <summary>ID of the round that will feed next (null if empty).</summary>
    public string TopAmmoId => rounds.Count > 0 ? rounds[rounds.Count - 1] : null;

    /// <summary>Same as <see cref="TopAmmoId"/>; reads the next round without removing it.</summary>
    public string Peek() => TopAmmoId;

    public bool CanLoad(AmmoData ammo)
    {
        if (ammo == null || IsFull) return false;
        return string.IsNullOrEmpty(caliber) || caliber == ammo.caliber;
    }

    /// <summary>Press one cartridge in on top. Fails if the magazine is full or the caliber is wrong.</summary>
    public bool TryPush(AmmoData ammo)
    {
        if (!CanLoad(ammo)) return false;
        if (string.IsNullOrEmpty(caliber)) caliber = ammo.caliber;
        rounds.Add(ammo.ammoId);
        return true;
    }

    /// <summary>Remove and return the top round's ID (null if empty). This is what the bolt strips off.</summary>
    public string Pop()
    {
        if (rounds.Count == 0) return null;
        string id = rounds[rounds.Count - 1];
        rounds.RemoveAt(rounds.Count - 1);
        return id;
    }

    /// <summary>Fill with <paramref name="count"/> rounds of one type (count below 0 = fill to capacity).</summary>
    public void Fill(AmmoData ammo, int count = -1)
    {
        if (ammo == null) return;
        int target = count < 0 ? capacity : Math.Min(count, capacity);
        while (rounds.Count < target && TryPush(ammo)) { }
    }
}