using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Master list of every cartridge type. Turns saved ammo IDs (magazine contents, chambered round) back into assets.
/// Create one (Assets > Create > Weapons > Ammo Database), drag all AmmoData into it, and assign it to each Firearm.
/// </summary>
[CreateAssetMenu(fileName = "AmmoDatabase", menuName = "Weapons/Ammo Database")]
public class AmmoDatabase : ScriptableObject
{
    public List<AmmoData> all = new List<AmmoData>();

    Dictionary<string, AmmoData> lookup;

    public AmmoData Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (lookup == null) Rebuild();
        lookup.TryGetValue(id, out AmmoData data);
        return data;
    }

    void Rebuild()
    {
        lookup = new Dictionary<string, AmmoData>();
        foreach (AmmoData a in all)
        {
            if (a == null) continue;
            if (string.IsNullOrEmpty(a.ammoId))
            {
                Debug.LogWarning($"[AmmoDatabase] '{a.name}' has no ammoId.", a);
                continue;
            }
            if (lookup.ContainsKey(a.ammoId))
            {
                Debug.LogWarning($"[AmmoDatabase] Duplicate ammoId '{a.ammoId}' on '{a.name}'.", a);
                continue;
            }
            lookup.Add(a.ammoId, a);
        }
    }

    void OnEnable() => lookup = null;
    void OnValidate() => lookup = null;
}
