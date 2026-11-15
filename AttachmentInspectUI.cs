using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Inspect-mode attachment picker (simple IMGUI so it needs no Canvas setup; swap for your real UI later).
/// Put it on the Player root next to WeaponInspect. It needs no calls from other scripts: it listens to
/// WeaponInspect and uses its hotspot hovering, so it only ever sees what the cursor is really over.
///
/// While inspecting a weapon that has WeaponAttachments:
///   - markers show every slot; hover one to see what is in it
///   - left click a slot: menu to remove the installed attachment or install one from your stash
///   - mouse wheel over a Picatinny slot: slide the attachment along the rail
///   - a stats panel shows the weapon's totals
///   - changes only work in a SafeZone (WeaponAttachments.requireSafeZoneToModify)
///   - everything hides while you spin the gun with right mouse
///
/// Magazine slots: tick "Show In Picker" OFF on them if your reload system owns clicking them.
/// </summary>
public class AttachmentInspectUI : MonoBehaviour
{
    [Header("References (found automatically if empty)")]
    public WeaponInspect inspect;
    public AttachmentInventory inventory;
    public Camera viewCamera;

    WeaponAttachments weapon;
    bool active;
    AttachmentSlot hovered, menuSlot;
    Vector2 menuPos;
    Rect menuRect;
    GUIStyle labelStyle, buttonStyle;

    void Awake()
    {
        if (inspect == null) inspect = GetComponentInParent<WeaponInspect>();
        if (inventory == null) inventory = GetComponentInParent<AttachmentInventory>();
    }

    void OnEnable()
    {
        if (inspect == null) return;
        inspect.Entered += HandleEntered;
        inspect.Exited += Close;
    }

    void OnDisable()
    {
        if (inspect != null)
        {
            inspect.Entered -= HandleEntered;
            inspect.Exited -= Close;
        }
        Close();
    }

    void HandleEntered()
    {
        Pickupable item = inspect.Item;
        weapon = item != null ? item.GetComponent<WeaponAttachments>() : null;
        active = weapon != null;
        hovered = null;
        menuSlot = null;

        if (viewCamera == null && inspect.look != null)
        {
            viewCamera = inspect.look.GetComponent<Camera>();
            if (viewCamera == null) viewCamera = inspect.look.GetComponentInChildren<Camera>();
        }
        if (viewCamera == null) viewCamera = Camera.main;
    }

    void Close()
    {
        active = false;
        weapon = null;
        hovered = null;
        CloseMenu();
    }

    void CloseMenu()
    {
        menuSlot = null;
        if (inspect != null) inspect.PointerBlocked = false;
    }

    void Update()
    {
        if (!active || weapon == null || inspect == null) return;

        if (!inspect.IsInspecting || inspect.IsExiting)
        {
            Close();
            return;
        }

        // Spinning the gun: hide everything, drop any open menu
        if (inspect.IsRotating)
        {
            hovered = null;
            if (menuSlot != null) CloseMenu();
            return;
        }

        // Outside a safe zone there is nothing to see or do: no weapon info, no attachment points
        if (!weapon.CanPlayerModify)
        {
            hovered = null;
            if (menuSlot != null) CloseMenu();
            return;
        }
        if (menuSlot != null) return;

        AttachmentSlot s = AttachmentSlot.FromHotspot(inspect.HoveredHotspot);
        hovered = s != null && s.Owner == weapon && s.showInPicker ? s : null;
        if (hovered == null) return;

        int scroll = GameInput.ScrollStep();
        if (scroll != 0 && hovered.isRail && hovered.Installed != null) weapon.ShiftRail(hovered, scroll);

        if (GameInput.ClickPressed())
        {
            menuSlot = hovered;
            inspect.PointerBlocked = true; // the gun's hotspots stop reacting while the menu is open
            Vector2 p = GameInput.PointerPosition();
            menuPos = new Vector2(p.x, Screen.height - p.y);
        }
    }

    // ------------------------------------------------------------------ drawing

    void EnsureStyles()
    {
        if (labelStyle != null) return;
        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
        buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
    }

    void OnGUI()
    {
        if (!active || weapon == null || inspect == null || viewCamera == null) return;

        // Weapon info and attachment points are only shown in a safe zone
        if (!weapon.CanPlayerModify) return;
        EnsureStyles();

        DrawStatsPanel();

        if (inspect.IsRotating) return;

        DrawMarkers();
        if (menuSlot != null) DrawMenu();
    }

    void DrawMarkers()
    {
        Color old = GUI.color;
        foreach (AttachmentSlot s in weapon.Slots)
        {
            if (!s.showInPicker) continue;
            Vector3 sp = viewCamera.WorldToScreenPoint(s.Mount.position);
            if (sp.z <= 0f) continue;

            bool on = s == hovered || s == menuSlot;
            float size = on ? 14f : 9f;
            GUI.color = on ? new Color(1f, 0.85f, 0.2f, 0.95f) : new Color(1f, 1f, 1f, 0.45f);
            GUI.DrawTexture(new Rect(sp.x - size * 0.5f, Screen.height - sp.y - size * 0.5f, size, size), Texture2D.whiteTexture);

            if (s == hovered && menuSlot == null)
            {
                string installed = s.Installed != null ? s.Installed.displayName : "<i>Empty</i>";
                string hint = s.isRail && s.Installed != null && weapon.CanPlayerModify ? "\nScroll: slide on rail" : "";
                GUI.color = Color.white;
                GUI.Label(new Rect(sp.x + 14f, Screen.height - sp.y - 12f, 320f, 60f),
                          $"<b>{s.displayLabel}</b>: {installed}{hint}", labelStyle);
            }
        }
        GUI.color = old;
    }

    void DrawMenu()
    {
        Event e = Event.current;
        if (e.type == EventType.MouseDown && !menuRect.Contains(e.mousePosition))
        {
            CloseMenu();
            e.Use();
            return;
        }

        // One row per attachment type, except magazines: every magazine is listed on its own with its own rounds.
        var options = new List<MenuOption>();
        if (inventory != null)
            foreach (AttachmentData d in inventory.Owned(menuSlot.category))
            {
                if (!weapon.CanInstall(menuSlot, d, out _)) continue;
                if (d.category == AttachmentCategory.Magazine)
                    foreach (MagazineContents m in inventory.MagazinesOf(d)) options.Add(new MenuOption { data = d, mag = m });
                else
                    options.Add(new MenuOption { data = d });
            }

        const float w = 360f, rowH = 28f;
        int rows = (menuSlot.Installed != null ? 1 : 0) + Mathf.Max(1, options.Count);
        menuRect = new Rect(
            Mathf.Min(menuPos.x, Screen.width - w - 8f),
            Mathf.Min(menuPos.y, Screen.height - (rows * rowH + 40f)),
            w, rows * rowH + 34f);

        GUI.Box(menuRect, $"{menuSlot.displayLabel}  ({menuSlot.category})");
        float y = menuRect.y + 26f;

        if (menuSlot.Installed != null)
        {
            if (GUI.Button(new Rect(menuRect.x + 6f, y, w - 12f, rowH - 2f), $"Remove {menuSlot.Installed.displayName}", buttonStyle))
            {
                MagazineContents removedRounds = weapon.GetMagazineContents(menuSlot); // read before the magazine leaves
                AttachmentData removed = weapon.Remove(menuSlot);
                GiveBack(removed, removedRounds);
                CloseMenu();
                return;
            }
            y += rowH;
        }

        if (options.Count == 0)
            GUI.Label(new Rect(menuRect.x + 10f, y, w - 20f, rowH), "Nothing compatible in your stash.", labelStyle);

        foreach (MenuOption o in options)
        {
            AttachmentData d = o.data;
            string text = o.mag != null
                ? $"{d.displayName}   {o.mag.Count}/{o.mag.capacity} rounds   [{d.stats.Summary()}]"
                : $"{d.displayName}  x{inventory.Count(d)}   [{d.stats.Summary()}]";
            if (GUI.Button(new Rect(menuRect.x + 6f, y, w - 12f, rowH - 2f), text, buttonStyle))
            {
                bool taken = o.mag != null ? inventory.TakeMagazine(d, o.mag) : inventory.TryTake(d);
                if (taken)
                {
                    AttachmentData previous = menuSlot.Installed;
                    MagazineContents previousRounds = weapon.GetMagazineContents(menuSlot); // the old magazine keeps its rounds
                    if (weapon.Install(menuSlot, d, -1, true, o.mag)) GiveBack(previous, previousRounds);
                    else GiveBack(d, o.mag);
                }
                CloseMenu();
                return;
            }
            y += rowH;
        }
    }

    struct MenuOption { public AttachmentData data; public MagazineContents mag; }

    /// <summary>Returns a removed attachment to the stash. A magazine goes back as the same magazine, rounds and all.</summary>
    void GiveBack(AttachmentData data, MagazineContents rounds)
    {
        if (data == null || inventory == null) return;
        if (data.category == AttachmentCategory.Magazine && rounds != null) inventory.AddMagazine(data, rounds);
        else inventory.Add(data);
    }

    void DrawStatsPanel()
    {
        AttachmentStats s = weapon.TotalStats;
        string text =
            $"<b>Weapon</b>\n" +
            $"Weight  {weapon.TotalWeightKg:0.00} kg\n" +
            $"Ergonomics  {s.ergonomics:+0.#;-0.#;0}\n" +
            $"Recoil  x{s.recoilMultiplier:0.00}\n" +
            $"Sway  x{s.swayMultiplier:0.00}\n" +
            $"ADS time  x{s.adsTimeMultiplier:0.00}\n" +
            $"Magnification  {weapon.CurrentMagnification:0.#}x" +
            (s.magazineCapacity > 0 ? $"\nMag capacity  {s.magazineCapacity}" : "");
        GUI.Box(new Rect(16f, 16f, 220f, 150f), GUIContent.none);
        GUI.Label(new Rect(24f, 20f, 210f, 145f), text, labelStyle);

        if (inspect.CanRotate)
            GUI.Label(new Rect(16f, 172f, 360f, 26f), "Hold right mouse: turn the weapon", labelStyle);
    }
}