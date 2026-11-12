using UnityEngine;

/// <summary>
/// Looks at what's in front of the camera and handles the interaction keys.
///   E            pick up what you're looking at (swaps if your hands are full)
///   G (tap)      set the held item down
///   G (hold)     wind up, release to throw (heavier items fly shorter)
///
/// Put this on the Player root next to HeldItemSlot. It does nothing while the cursor is free
/// (inspect mode / menus), so it never fights other systems for input.
/// </summary>
public class PlayerInteractor : MonoBehaviour
{
    [Header("References")]
    public MouseLook look;
    public HeldItemSlot slot;

    [Header("Reach")]
    public float interactRange = 2.2f;
    [Tooltip("Aim forgiveness: if the exact center misses, a sweep of this radius is tried.")]
    public float probeRadius = 0.12f;
    public LayerMask interactMask = ~0;

    [Header("Drop / Throw")]
    [Tooltip("Releasing G faster than this is a plain drop; holding longer starts a throw wind-up.")]
    public float tapTime = 0.25f;
    public float maxChargeTime = 0.9f;

    [Header("Prompt")]
    [Tooltip("The UI text shown when you look at an item. Create one with GameObject > UI > Interaction Prompt. Found automatically if left empty.")]
    public InteractionPrompt prompt;

    /// <summary>The item you're currently looking at (null if none).</summary>
    public Pickupable Target { get; private set; }

    bool dropHeld;
    float dropStart;

    void Awake()
    {
        if (slot == null) slot = GetComponent<HeldItemSlot>();
        if (look == null && slot != null) look = slot.look;
        if (look == null) look = GetComponentInChildren<MouseLook>();
        if (prompt == null)
        {
#if UNITY_2022_2_OR_NEWER
            prompt = FindFirstObjectByType<InteractionPrompt>();
#else
            prompt = FindObjectOfType<InteractionPrompt>();
#endif
        }
    }

    void Update()
    {
        if (look == null || slot == null) return;

        bool active = look.LookEnabled && Cursor.lockState == CursorLockMode.Locked;
        if (!active)
        {
            Target = null;
            CancelCharge();
        }
        else
        {
            FindTarget();
            if (GameInput.InteractPressed() && Target != null) slot.TryEquip(Target);
            HandleDrop();
        }

        UpdatePrompt();
    }

    void UpdatePrompt()
    {
        if (prompt == null) return;
        if (Target == null)
        {
            prompt.Hide();
            return;
        }

        PromptKind kind = !slot.CanLift(Target) ? PromptKind.TooHeavy
                        : slot.HasItem ? PromptKind.Swap
                        : PromptKind.Pickup;
        prompt.Show(kind, Target.DisplayName, Target.MassKg);
    }

    void FindTarget()
    {
        Transform cam = look.transform;
        int mask = interactMask.value & ~(1 << gameObject.layer);
        Pickupable found = null;

        if (Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, interactRange, mask,
                QueryTriggerInteraction.Ignore))
            found = hit.collider.GetComponentInParent<Pickupable>();

        if (found == null && Physics.SphereCast(cam.position, probeRadius, cam.forward, out hit, interactRange,
                mask, QueryTriggerInteraction.Ignore))
            found = hit.collider.GetComponentInParent<Pickupable>();

        if (found != null && (found.IsHeld || found.data == null)) found = null;
        Target = found;
    }

    void HandleDrop()
    {
        if (!slot.HasItem)
        {
            if (dropHeld) CancelCharge();
            return;
        }

        if (GameInput.DropPressed())
        {
            dropHeld = true;
            dropStart = Time.time;
        }
        if (!dropHeld) return;

        float held = Time.time - dropStart;
        float charge = Mathf.Clamp01((held - tapTime) / maxChargeTime);
        slot.SetThrowCharge(charge);

        if (!GameInput.DropHeld())
        {
            dropHeld = false;
            slot.SetThrowCharge(0f);
            if (held < tapTime) slot.Drop();
            else slot.Throw(charge);
        }
    }

    void CancelCharge()
    {
        if (!dropHeld) return;
        dropHeld = false;
        if (slot != null) slot.SetThrowCharge(0f);
    }
}