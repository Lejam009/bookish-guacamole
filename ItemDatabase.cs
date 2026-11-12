using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Lookup table from item ID to ItemData, used when a save file says "the player was holding 'ak74'".
/// Create ONE of these (Create > Game > Item Database). Then right-click its header in the Inspector and
/// choose "Collect All Item Data In Project" to fill the list automatically.
/// </summary>
[CreateAssetMenu(fileName = "ItemDatabase", menuName = "Game/Item Database", order = 1)]
public class ItemDatabase : ScriptableObject
{
    public List<ItemData> items = new List<ItemData>();

    Dictionary<string, ItemData> lookup;

    public ItemData Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (lookup == null) Rebuild();
        lookup.TryGetValue(id, out ItemData data);
        return data;
    }

    void Rebuild()
    {
        lookup = new Dictionary<string, ItemData>();
        foreach (ItemData item in items)
        {
            if (item == null || string.IsNullOrEmpty(item.id)) continue;
            if (lookup.ContainsKey(item.id))
                Debug.LogWarning($"ItemDatabase: duplicate item ID '{item.id}' ({item.name}).", this);
            else
                lookup.Add(item.id, item);
        }
    }

    void OnEnable() => lookup = null;
    void OnValidate() => lookup = null;

#if UNITY_EDITOR
    [ContextMenu("Collect All Item Data In Project")]
    void CollectAll()
    {
        items.Clear();
        foreach (string guid in AssetDatabase.FindAssets("t:ItemData"))
        {
            ItemData data = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if (data != null) items.Add(data);
        }
        lookup = null;
        EditorUtility.SetDirty(this);
    }
#endif
}
