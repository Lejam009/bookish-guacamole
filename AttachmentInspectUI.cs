using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Inspect-mode attachment picker (simple IMGUI so it needs no Canvas setup; swap for your real UI later).
///
/// While inspecting a weapon:
///   - markers show every slot; hover one to see what is in it
///   - left click a slot: menu to remove the installed attachment or install one from your stash
///   - mouse wheel over a Picatinny slot: slide the attachment along the rail (eye relief)
///   - a stats panel shows the weapon's totals
///   - hidden while right-click rotating; changes need a SafeZone (WeaponAttachments.requireSafeZoneToModify)
///
/// Call Open(weapon) when inspect mode starts and Close() when it ends.
/// </summary>
public class AttachmentInspectUI : MonoBehaviour
{
    [Header("References")]
    public Camera viewCamera;
    public AttachmentInventory inventory;

    [Header("Picking")]
    public LayerMask slotMask = ~0;
    public float maxPickDistance = 3f;

    WeaponAttachments weapon;
    bool active;
    AttachmentSlot hovered, menuSlot;
    Vector2 menuPos;
    Rect menuRect;
    string toast;
    float toastUntil;
    GUIStyle labelStyle, buttonStyle;

    void Awake()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        if (inventory == null) inventory = GetComponentInParent<AttachmentInventory>();
    }

    public void Open(WeaponAttachments w)
    {
        weapon = w;
        active = w != null;
        hovered = null;
        menuSlot = null;
    }

    public void Close()
    {
        active = false;
        weapon = null;
        hovered = null;
        menuSlot = null;
    }

    void Update()
    {
        if (!active || weapon == null || viewCamera == null) return;

        if (WeaponInspectRotator.IsRotating)
        {
            hovered = null;
            menuSlot = null;
            return;
        }
        if (menuSlot != null) return;

        hovered = Pick();
        if (hovered == null) return;

        if (!weapon.CanPlayerModify)
        {
            if (GameInput.ClickPressed()) Toast("Find somewhere safe to work on your weapon.");
            return;
        }

        int scroll = AttachmentInput.ScrollStep();
        if (scroll != 0 && hovered.isRail && hovered.Installed != null) weapon.ShiftRail(hovered, scroll);

        if (GameInput.ClickPressed())
        {
            menuSlot = hovered;
            Vector2 p = AttachmentInput.PointerPosition();
            menuPos = new Vector2(p.x, Screen.height - p.y);
        }
    }

    AttachmentSlot Pick()
    {
        Ray ray = viewCamera.ScreenPointToRay(AttachmentInput.PointerPosition());
        RaycastHit[] hits = Physics.RaycastAll(ray, maxPickDistance, slotMask, QueryTriggerInteraction.Collide);

        AttachmentSlot best = null;
        float bestDist = float.MaxValue;
        foreach (RaycastHit h in hits)
        {
            AttachmentSlot s = h.collider.GetComponentInParent<AttachmentSlot>();
            if (s == null || s.Owner != weapon || !s.showInPicker) continue;
            if (h.distance < bestDist) { bestDist = h.distance; best = s; }
        }
        return best;
    }

    void Toast(string msg)
    {
        toast = msg;
        toastUntil = Time.unscaledTime + 2.5f;
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
        if (!active || weapon == null || viewCamera == null) return;
        EnsureStyles();

        DrawStatsPanel();
        if (Time.unscaledTime < toastUntil)
            GUI.Label(new Rect(Screen.width * 0.5f - 200f, Screen.height - 80f, 400f, 30f), toast, labelStyle);

        if (WeaponInspectRotator.IsRotating) return;

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
                string hint = s.isRail && s.Installed != null ? "\nScroll: slide on rail" : "";
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
            menuSlot = null;
            e.Use();
            return;
        }

        var options = new List<AttachmentData>();
        if (inventory != null)
            foreach (AttachmentData d in inventory.Owned(menuSlot.category))
                if (weapon.CanInstall(menuSlot, d, out _)) options.Add(d);

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
                AttachmentData removed = weapon.Remove(menuSlot);
                if (removed != null && inventory != null) inventory.Add(removed);
                menuSlot = null;
                return;
            }
            y += rowH;
        }

        if (options.Count == 0)
            GUI.Label(new Rect(menuRect.x + 10f, y, w - 20f, rowH), "Nothing compatible in your stash.", labelStyle);

        foreach (AttachmentData d in options)
        {
            string text = $"{d.displayName}  x{inventory.Count(d)}   [{d.stats.Summary()}]";
            if (GUI.Button(new Rect(menuRect.x + 6f, y, w - 12f, rowH - 2f), text, buttonStyle))
            {
                if (inventory.TryTake(d))
                {
                    AttachmentData previous = menuSlot.Installed;
                    if (weapon.Install(menuSlot, d)) { if (previous != null) inventory.Add(previous); }
                    else inventory.Add(d);
                }
                menuSlot = null;
                return;
            }
            y += rowH;
        }
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
    }
}
