using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A clickable spot on a weapon during inspect mode (the magazine well, the chamber, the charging handle...).
/// Place it on an EMPTY CHILD of the weapon prefab, add a Box or Sphere Collider to the same object, and size
/// the collider over the part you want clickable. (It is made a trigger automatically.)
///
/// It only reacts while WeaponInspect is active and the cursor is over it. Use the UnityEvents in the Inspector,
/// or from code subscribe to HoverEntered / HoverExited / Pressed / Released. Check hotspotId to know which one.
/// Any system (magazine dragging, chamber check, attachments) can build on this without changing WeaponInspect.
///
/// The cyan outline in the Scene view shows the clickable area.
/// </summary>
[DisallowMultipleComponent]
public class InspectHotspot : MonoBehaviour
{
    [Tooltip("Name for your own scripts to tell hotspots apart, e.g. \"magwell\", \"chamber\".")]
    public string hotspotId = "";

    [Header("Events")]
    public UnityEvent onHoverEnter;
    public UnityEvent onHoverExit;
    public UnityEvent onPressed;
    public UnityEvent onReleased;

    public event Action<InspectHotspot> HoverEntered;
    public event Action<InspectHotspot> HoverExited;
    public event Action<InspectHotspot> Pressed;
    public event Action<InspectHotspot> Released;

    public bool IsHovered { get; private set; }
    public bool IsPressed { get; private set; }

    void Awake()
    {
        // Triggers never block the player or other objects; they only answer the cursor ray.
        foreach (Collider col in GetComponentsInChildren<Collider>(true))
            col.isTrigger = true;
    }

    /// <summary>Called by WeaponInspect.</summary>
    public void NotifyHover(bool hovered)
    {
        if (hovered == IsHovered) return;
        IsHovered = hovered;
        if (hovered)
        {
            onHoverEnter?.Invoke();
            HoverEntered?.Invoke(this);
        }
        else
        {
            onHoverExit?.Invoke();
            HoverExited?.Invoke(this);
        }
    }

    /// <summary>Called by WeaponInspect.</summary>
    public void NotifyPressed(bool isDown)
    {
        if (isDown == IsPressed) return;
        IsPressed = isDown;
        if (isDown)
        {
            onPressed?.Invoke();
            Pressed?.Invoke(this);
        }
        else
        {
            onReleased?.Invoke();
            Released?.Invoke(this);
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = IsHovered ? Color.yellow : new Color(0f, 1f, 1f, 0.9f);

        BoxCollider box = GetComponent<BoxCollider>();
        if (box != null) Gizmos.DrawWireCube(box.center, box.size);

        SphereCollider sphere = GetComponent<SphereCollider>();
        if (sphere != null) Gizmos.DrawWireSphere(sphere.center, sphere.radius);
    }
}
