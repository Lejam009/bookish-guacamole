using System;

/// <summary>
/// What gets written to a save file for the player's hands. Plain fields only, so it works with
/// JsonUtility.ToJson / FromJson (or any serializer you end up using).
/// </summary>
[Serializable]
public class ItemSaveData
{
    /// <summary>ItemData.id of the held item. Empty = hands are empty.</summary>
    public string itemId;

    /// <summary>Optional per-item state (ammo, durability...) produced by an IItemStateful on the item.</summary>
    public string state;

    public bool HasItem => !string.IsNullOrEmpty(itemId);
}

/// <summary>
/// Put this on an item's prefab (e.g. your future Weapon script) if it has state that must survive a
/// save: ammo in the magazine, a chambered round, fuel, charge... Return anything as a string (JSON is easiest).
/// </summary>
public interface IItemStateful
{
    string CaptureState();
    void RestoreState(string state);
}
