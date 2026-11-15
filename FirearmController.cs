using UnityEngine;

/// <summary>
/// The player's hands on the weapon: reads the keys and decides whether the gun may fire.
///
///   Left mouse (hold)   pull the trigger
///   B                   selector switch: Safe > Semi > Burst > Auto (only the modes the gun has)
///   T                   work the charging handle: clears a misfire, chambers the first round after a magazine change,
///                       releases a locked-back bolt, works a bolt-action
///
/// THE GUN CANNOT FIRE WHILE:
///   - weapon inspect mode is active (or still swinging the gun back to your hands)
///   - the player stands in a SafeZone (no trigger input at all: no shots, no dry-fire clicks, bursts are cancelled)
///   - reaching for / swapping an item, winding up a throw, sprinting, or pressed against a wall
///   - a menu owns the cursor, or the click was the one that re-locked the mouse
/// Handling the gun (selector, charging handle) is allowed in a safe zone, but never during inspect mode, which
/// owns the gun. Weapon-category items only.
///
/// Put this on the Player root next to HeldItemSlot, WeaponADS, WeaponInspect and WeaponRecoil.
/// </summary>
[DefaultExecutionOrder(30)]
public class FirearmController : MonoBehaviour
{
    [Header("References (found automatically if empty)")]
    public HeldItemSlot slot;
    public MouseLook look;
    public PlayerMotor motor;
    public WeaponInspect inspect;
    public WeaponRecoil recoil;

    [Header("Rules")]
    [Tooltip("Can't fire while the gun is pushed back by a wall by more than this (metres). Same idea as aiming.")]
    public float wallBlockPush = 0.1f;
    [Tooltip("Off (realistic): the gun is lowered while sprinting and can't be fired.")]
    public bool canFireWhileSprinting = false;

    [Header("Developer Readout")]
    [Tooltip("Shows mode, magazine and chamber at the bottom right. Real guns have no ammo counter: turn this off when you build your own UI or the inspect-mode chamber check.")]
    public bool showDebugHud = true;

    /// <summary>The firearm in the player's hands (null if none).</summary>
    public Firearm Current => firearm;
    /// <summary>True right now if a trigger pull would be allowed (inspect, safe zone and the other rules checked).</summary>
    public bool CanFireNow => CanFire();

    Firearm firearm;
    Pickupable cachedItem;
    Rigidbody playerRb;
    Collider[] playerColliders;
    bool lockedLastFrame, blockedPress;
    GUIStyle hudStyle;

    void Awake()
    {
        if (slot == null) slot = GetComponent<HeldItemSlot>();
        if (motor == null) motor = GetComponent<PlayerMotor>();
        if (look == null && slot != null) look = slot.look;
        if (look == null) look = GetComponentInChildren<MouseLook>();
        if (inspect == null) inspect = GetComponent<WeaponInspect>();
        if (recoil == null) recoil = GetComponent<WeaponRecoil>();

        playerRb = GetComponent<Rigidbody>();
        playerColliders = GetComponentsInChildren<Collider>();
    }

    void OnDisable() => Detach();

    // ================================================================== held weapon

    void RefreshWeapon()
    {
        Pickupable current = slot != null ? slot.Current : null;
        if (current == cachedItem) return;

        Detach();
        cachedItem = current;
        if (current == null) return;

        firearm = current.GetComponent<Firearm>();
        if (firearm == null) return;

        firearm.HitMask = ~(1 << gameObject.layer); // bullets never hit the shooter
        firearm.OwnerColliders = playerColliders;
        firearm.Fired += OnFired;
        blockedPress = true; // a trigger already held while the gun was picked up does not fire it
    }

    void Detach()
    {
        if (firearm != null)
        {
            firearm.Fired -= OnFired;
            firearm.ReleaseTrigger();
        }
        firearm = null;
        cachedItem = null;
    }

    void OnFired(ShotInfo shot)
    {
        if (recoil != null) recoil.ApplyShot(shot.recoilImpulse);
    }

    // ================================================================== rules

    /// <summary>The gun is in the hands and nothing else owns it. (Selector and charging handle need only this.)</summary>
    bool CanHandle()
    {
        if (firearm == null || slot == null || look == null || motor == null) return false;
        if (!slot.HasItem || slot.HeldData == null || slot.HeldData.category != ItemCategory.Weapon) return false;
        if (slot.IsEquipping || slot.ExternalControl || slot.IsThrowCharging) return false;
        if (inspect != null && inspect.IsInspecting) return false;
        if (!look.LookEnabled || Cursor.lockState != CursorLockMode.Locked) return false;
        if (motor.IsSprinting && !canFireWhileSprinting) return false;
        return true;
    }

    bool CanFire()
    {
        if (!CanHandle()) return false;
        if (SafeZone.PlayerInSafeZone) return false;
        if (slot.WallPushAmount > wallBlockPush) return false;
        return lockedLastFrame; // the click that re-locks the cursor is not a shot
    }

    // ================================================================== input

    void Update()
    {
        RefreshWeapon();

        if (firearm != null)
        {
            firearm.OwnerVelocity = playerRb != null ? Pickupable.GetVelocity(playerRb) : Vector3.zero;

            bool held = GameInput.FireHeld();
            if (!held) blockedPress = false;

            bool allowed = CanFire();
            // Any press that starts (or is carried) through a moment when firing is not allowed never fires:
            // let go and press again. Walking out of a safe zone with the button held does not start shooting.
            if (held && !allowed) blockedPress = true;

            // Not allowed = finger off the trigger AND any burst in progress is cancelled (entering a safe zone mid-burst stops it).
            if (allowed) firearm.SetTrigger(held && !blockedPress);
            else firearm.ReleaseTrigger();

            if (CanHandle())
            {
                if (GameInput.FireModePressed()) firearm.CycleFireMode();
                if (GameInput.RackPressed()) firearm.Rack();
            }
        }

        lockedLastFrame = Cursor.lockState == CursorLockMode.Locked;
    }

    // ================================================================== readout

    void OnGUI()
    {
        if (!showDebugHud || firearm == null) return;

        if (hudStyle == null)
            hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.LowerRight, richText = true };

        string mode = firearm.Mode == FireMode.Safe ? "SAFE" : firearm.Mode.ToString().ToUpperInvariant();
        string mag = firearm.HasMagazine ? $"{firearm.MagazineRounds}/{firearm.Magazine.capacity}" : "no mag";
        string bolt = firearm.BoltOpen ? "  <color=#ffcc33>BOLT OPEN</color>" : "";
        string zone = SafeZone.PlayerInSafeZone ? "\n<color=#66ccff>Safe zone: weapon safe</color>" : "";

        string feed = firearm.CanLoadAnotherRound ? "can chamber another" : "can't chamber another (" + firearm.GetMagazineFeedStatus() + ")";
        string text = $"<b>{mode}</b>   Mag {mag}   Chamber: {firearm.ChamberDescription}{bolt}\n{feed}{zone}";
        GUI.Label(new Rect(Screen.width - 520f, Screen.height - 80f, 500f, 60f), text, hudStyle);
    }
}