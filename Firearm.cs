using System;
using UnityEngine;
using UnityEngine.Events;

public enum FireMode { Safe, Semi, Burst, Auto }

/// <summary>SelfLoading: gas / recoil operated, cycles itself after every shot (AR-15, AK, most pistols).
/// Manual: bolt, pump or lever, the spent case stays in the chamber until YOU work the action.</summary>
public enum ActionType { SelfLoading, Manual }

/// <summary>What is physically sitting in the chamber right now.</summary>
public enum ChamberState { Empty, Live, Spent }

/// <summary>Whether the magazine can hand the bolt another round.</summary>
public enum MagazineFeedStatus { Ready, NoMagazine, MagazineEmpty, WrongCaliber }

public enum TriggerResult { Fired, Misfire, DryFire, HammerDown, BoltOpen, Safe, Busy }

/// <summary>
/// One way a magazine insertion can go wrong: a line the magazine slides along after it misses the magwell.
/// The Axis empty's position is the deepest point the magazine reaches; its blue/Z arrow points away from the gun.
/// </summary>
[Serializable]
public class MagazineMissAxis
{
    [Tooltip("Empty child of the weapon. Position = the deepest point the magazine reaches. Blue/Z arrow = the way the magazine travels out, away from the gun.")]
    public Transform axis;
    [Tooltip("How far (metres) the line runs. The miss is over when you pull the magazine out past this, and then you can try again.")]
    [Min(0.01f)] public float length = 0.08f;
    [Tooltip("Tick this for a miss that slams the magazine into the dust cover (e.g. an axis on the right side of an AR-15): when this miss happens the cover is knocked shut. " +
             "Needs the Dust Cover set up on the Firearm.")]
    public bool closesDustCover;
    [Tooltip("How far along this line the magazine has to slide before it hits the dust cover and knocks it shut. 0 = the outer end of the line, 1 = the deepest point (the empty). " +
             "If the magazine is pulled back out before reaching this, the cover stays as it was. Only used when Closes Dust Cover is ticked.")]
    [Range(0f, 1f)] public float closesAtDepth = 0.5f;
}

/// <summary>
/// A small spring-animated part of the gun (bolt catch, latch, button, selector...): drag the transform in, pick Rotate or Move,
/// and give the amount. It swings or slides from where it sits in the prefab (the rest pose) with a little spring.
/// Every such part on the Firearm uses these same settings.
/// </summary>
[Serializable]
public class PartMotion
{
    public enum Kind { Rotate, Move }

    [Tooltip("The part that animates. Leave empty for no animation.")]
    public Transform part;
    [Tooltip("Rotate: the part turns. Move: the part slides.")]
    public Kind kind = Kind.Rotate;
    [Tooltip("Rotate: the axis it turns around. Move: the direction it slides (in the space of the part's PARENT). " +
             "Rotate the part in the Scene view to find which axis (X, Y or Z) does what you want. Use a negative amount to go the other way.")]
    public Vector3 axis = Vector3.right;
    [Tooltip("Rotate: degrees. Move: metres. How far the part goes from its rest pose (the pose it has in the prefab) when it is 'active'. " +
             "(The fire selector ignores this and has an amount per fire mode instead.)")]
    public float amount = 15f;
    [Tooltip("Snappiness of the motion. Higher = faster.")]
    public float stiffness = 1000f;
    [Tooltip("Below ~0.7 it overshoots a little and settles. 1 = no overshoot.")]
    [Range(0.2f, 1.5f)] public float dampingRatio = 0.8f;

    Vector3 restPos;
    Quaternion restRot = Quaternion.identity;
    float value, vel;   // in degrees (Rotate) or metres (Move)
    bool captured, snap = true;

    /// <summary>Fills in all the settings in one go.</summary>
    public PartMotion Set(Transform t, Kind k, Vector3 ax, float amt, float stiff, float damp)
    {
        part = t; kind = k; axis = ax; amount = amt; stiffness = stiff; dampingRatio = damp;
        return this;
    }

    /// <summary>Remembers the rest pose. Called once when the weapon wakes up.</summary>
    public void Capture()
    {
        if (part == null) return;
        restPos = part.localPosition;
        restRot = part.localRotation;
        captured = true;
    }

    /// <summary>The next Tick jumps straight to its target instead of gliding (used when a save is loaded).</summary>
    public void Snap() => snap = true;

    /// <summary>Moves the part toward "active" (the full Amount) or back to rest. `root` is the weapon: Move distances are measured in its units.</summary>
    public void Tick(Transform root, bool active, float dt) => TickTo(root, active ? amount : 0f, dt);

    /// <summary>Moves the part toward an exact position, in degrees (Rotate) or metres (Move) from rest.</summary>
    public void TickTo(Transform root, float target, float dt)
    {
        if (part == null) return;
        if (!captured) Capture();

        if (snap)
        {
            value = target; vel = 0f; snap = false;
        }
        else
        {
            dt = Mathf.Min(dt, 0.033f);
            float c = 2f * dampingRatio * Mathf.Sqrt(stiffness);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.002f));
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                vel += (-stiffness * (value - target) - c * vel) * h;
                value += vel * h;
            }
        }

        Vector3 a = axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.right;
        if (kind == Kind.Rotate)
        {
            part.localRotation = restRot * Quaternion.AngleAxis(value, a);
            return;
        }

        // Move: the DISTANCE is in the weapon's units, so an odd import scale on a parent can't stretch it.
        Transform parent = part.parent;
        if (parent == null || root == null)
        {
            part.localPosition = restPos + a * value;
            return;
        }
        Vector3 dirWorld = parent.TransformDirection(a).normalized;
        Vector3 worldDisp = root.TransformVector(root.InverseTransformDirection(dirWorld) * value);
        part.localPosition = restPos + parent.InverseTransformVector(worldDisp);
    }
}

/// <summary>Everything that left the gun in one shot (passed to Firearm.Fired).</summary>
public struct ShotInfo
{
    public AmmoData ammo;
    public Vector3 position;
    public Vector3 direction;
    public float muzzleVelocity;
    public float bulletMassKg;
    /// <summary>Momentum pushed back into the shooter, in N*s. WeaponRecoil turns this into motion.</summary>
    public float recoilImpulse;
}

/// <summary>
/// The weapon itself. Put this on the WORLD prefab of a gun (same object as Pickupable, WeaponSights, WeaponAttachments).
/// It owns the gun's mechanical state; FirearmController (on the player) pulls the trigger for it.
///
/// THE CHAMBER IS REAL STATE, not a counter:
///   Chamber = Empty / Live (a loaded round) / Spent (a fired case)
///   Magazine = a separate MagazineContents (or none)
///   Rounds you can still fire = rounds in the magazine + 1 if the chamber is Live.
/// So a gun with a round chambered fires with NO magazine in it. Firing a self-loading gun: the shot spends the
/// round, the action ejects the case, strips the next round from the magazine into the chamber and re-cocks the hammer.
/// With the magazine empty the bolt locks back (if a magazine is in); with no magazine it closes on an empty chamber.
///
/// Realistic behaviour included: hammer that stays down after a click or a misfire (rack to fix it), primer misfires,
/// bolt hold-open, racking the charging handle throws the live round out of the chamber, manual actions that leave the
/// spent case in the chamber, selector switch (Safe / Semi / Burst / Auto), cyclic rate, barrel-length muzzle velocity,
/// a bullet zeroed to the sights that falls under gravity and drag, and recoil from momentum (see WeaponRecoil).
///
/// SETUP:
///   1. Add Firearm to the weapon prefab. Set Caliber, Rounds Per Minute, which fire modes it has.
///   2. Create an empty child at the end of the barrel: +Z pointing the way the bullet leaves, +Y up. Drag it into Muzzle.
///   3. (Recommended) Empty child where the stock touches the shoulder (or the web of the hand for a pistol) into
///      Recoil Pivot. The height of the muzzle above it is what makes the gun climb.
///   4. (Optional) Ejection Port, Bolt Carrier (it will slide back and spring forward), sounds, UnityEvents.
///   5. Assign an Ammo Database. To start loaded, set Start Ammo. For a visible magazine put it in WeaponAttachments >
///      Default Loadout in the slot named "Magazine Slot Id" below; this component fills it.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Pickupable))]
[DefaultExecutionOrder(55)] // after HeldItemSlot (50) has posed the gun, so the muzzle is where it will be on screen
public class Firearm : MonoBehaviour, IItemStateful
{
    const float MoaToRad = 0.00029089f; // 1/60 of a degree

    [Header("Identity")]
    [Tooltip("Only cartridges and magazines with this caliber text fit. e.g. 5.56x45")]
    public string caliber = "5.56x45";
    [Tooltip("Needed to turn saved ammo IDs back into ammo (magazine contents, chambered round).")]
    public AmmoDatabase ammoDatabase;

    [Header("Parts (child transforms)")]
    [Tooltip("End of the barrel. +Z = direction the bullet leaves, +Y = up.")]
    public Transform muzzle;
    [Tooltip("Where the stock meets the shoulder (pistol: the web of the hand). Recoil twists the gun about this point. " +
             "Its ORIENTATION matters: blue/Z = the way the bullet leaves, green/Y = up, red/X = right. Select the weapon to see these axes in the Scene view.")]
    public Transform recoilPivot;
    [Tooltip("How much of the bore's sideways offset from the pivot turns into sideways kick (the red line in the Scene view). " +
             "1 = as measured. 0 = the gun only climbs and kicks straight back, with just a little random wobble.")]
    [Range(0f, 1f)] public float recoilLateralScale = 1f;
    [Tooltip("Where cases are thrown from. +X of this transform is the direction they fly (set Ejection Direction to change).")]
    public Transform ejectionPort;
    [Tooltip("Optional. The bolt carrier / slide. It is moved along Bolt Axis by a little spring simulation.")]
    public Transform boltCarrier;
    [Tooltip("Optional. Where the chambered round is shown. Make an EMPTY as a child of the Bolt Carrier (so the round rides back with the bolt) at the chamber, " +
             "and turn it so its blue/Z arrow points down the barrel. The cartridge prefab appears here only while a round is in the chamber: " +
             "the Cartridge Prefab of the ammo for a live round, its Casing Prefab for a fired case. Nothing is shown when the chamber is empty. " +
             "(Both prefabs are set on the Ammo Data asset. Their Rigidbody and colliders are removed on this copy.)")]
    public Transform chamberRoundMount;

    [Header("Reload Interaction Hotspots (optional)")]
    [Tooltip("Clickable spots used in reload interaction mode (hold R to enter, outside a safe zone). For each one: make an EMPTY CHILD of " +
             "the weapon at the part, add a Box Collider sized over it, and add an InspectHotspot. Then drag that object here. " +
             "Bolt catch: click to release a locked-back bolt.")]
    public InspectHotspot boltCatchHotspot;
    [Tooltip("Magazine release button: click to unlock the magazine (T does the same while you're holding the magazine with the mouse).")]
    public InspectHotspot magazineReleaseHotspot;
    [Tooltip("Fire selector switch: click to cycle the fire mode, same as pressing B.")]
    public InspectHotspot selectorHotspot;
    [Tooltip("Charging handle: press and drag it back along the bolt's travel to rack the gun. Needs a Bolt Carrier assigned so there is something to slide. " +
             "(The magazine itself is clicked through its magazine slot's own hitbox: size that over the magazine.)")]
    public InspectHotspot chargingHandleHotspot;
    [Tooltip("How far back (0 to 1 of the bolt travel) the handle has to be pulled before the gun ejects what's in the chamber.")]
    [Range(0.5f, 1f)] public float boltDragEjectAt = 0.85f;
    [Tooltip("While you keep holding the handle: how close to fully forward (0 to 1 of the travel) you have to push it for the bolt to count as closed again. " +
             "It then strips the next round into the chamber, and pulling the handle back again ejects that round, cycling the gun as many times as you like without letting go.")]
    [Range(0f, 0.3f)] public float boltDragResetAt = 0.05f;

    [Header("Magazine Miss Axes (optional)")]
    [Tooltip("What a missed magazine insertion looks like. Make EMPTY children of the weapon around the magwell, one per way the magazine can go wrong " +
             "(slipping off the front lip, the rear lip, jamming crooked...). Drag them in here. When the magazine misses the well (pushed in too fast or not lined up), " +
             "a RANDOM one is picked and the magazine travels along it until you pull it out past its Length, then you can try again. " +
             "Each empty: its POSITION is the deepest point the magazine reaches, and its blue/Z arrow points AWAY from the gun, along the line the magazine travels. " +
             "Select the weapon to see them as magenta lines. With none, a too-fast insertion has no special effect. " +
             "Tick Closes Dust Cover on a miss that should slam into the dust cover.")]
    public MagazineMissAxis[] magazineMissAxes;

    [Header("Bolt Catch / Bolt Release (visual)")]
    [Tooltip("The bolt catch lever (the AR-15's lever on the left of the receiver, the 1911's slide stop). It moves by Amount when the bolt locks back, " +
             "and returns when the bolt is released or closed. Pose the model with the bolt CLOSED.")]
    public PartMotion boltCatchLever = new PartMotion { kind = PartMotion.Kind.Rotate, axis = Vector3.right, amount = 15f, stiffness = 900f, dampingRatio = 0.55f };
    [Tooltip("When the bolt has a Bolt Carrier assigned, the catch only engages once the carrier has travelled this fraction of the way back (the catch can't catch a bolt that isn't there yet).")]
    [Range(0f, 1f)] public float boltCatchEngageAt = 0.9f;

    [Header("Charging Handle Latch (visual)")]
    [Tooltip("The latch on the charging handle (the AR-15's two-finger latch). It is pressed in while you hold the handle and springs back when you let go. " +
             "Usually a child of the charging handle. Pick Rotate or Move.")]
    public PartMotion chargingHandleLatch = new PartMotion { kind = PartMotion.Kind.Rotate, axis = Vector3.right, amount = 20f, stiffness = 900f, dampingRatio = 0.7f };

    [Header("Magazine Release Button (visual)")]
    [Tooltip("The magazine release button. It is pushed in when the release is pressed (clicked, or T) and springs back. Usually Move.")]
    public PartMotion magazineReleaseButton = new PartMotion { kind = PartMotion.Kind.Move, axis = Vector3.right, amount = 0.004f };
    [Tooltip("Seconds the button stays pushed in per press.")]
    public float magazineReleasePressTime = 0.15f;
    [Tooltip("When the magazine drops out of the gun, the muzzle dips a little (degrees, about this much). 0 = none. A full magazine is heavy, so a small dip is natural.")]
    public float magazineDropKickDegrees = 1.5f;
    [Tooltip("How long that dip lasts in total (seconds, down and back up). The dip only moves the gun, not the camera.")]
    public float magazineDropKickSeconds = 0.3f;
    [Tooltip("When the magazine release is pressed and the magazine falls out: how far (metres) it slides out along the magwell's guide axis " +
             "(the magazine slot's Insert Offset direction) before it becomes a free falling object. Make it at least as long as the part of the magazine that sits " +
             "inside the magwell, so it clears the well instead of dropping through it. 0 = it just drops.")]
    public float magazineSlideDistance = 0.12f;
    [Tooltip("The speed (m/s) the magazine starts sliding out at. Gravity along the well adds to it, it never goes slower than this.")]
    public float magazineSlideStartSpeed = 0.15f;

    [Header("Dust Cover / Ejection Port Cover (visual)")]
    [Tooltip("The spring-loaded cover over the ejection port (the AR-15's dust cover). Pose the model with it CLOSED. It swings to its open pose by Amount. " +
             "Pick Rotate or Move. Click it in reload interaction mode (Dust Cover Hotspot) to open or close it.")]
    public PartMotion dustCover = new PartMotion { kind = PartMotion.Kind.Rotate, axis = Vector3.right, amount = 100f, stiffness = 600f, dampingRatio = 0.55f };
    [Tooltip("Clickable spot over the dust cover for reload interaction mode: click to open or close it. An empty child of the weapon with a Box Collider and an InspectHotspot, " +
             "like the other hotspots above. (It can also be the cover itself if it has a collider.)")]
    public InspectHotspot dustCoverHotspot;
    [Tooltip("The weapon starts with the cover open.")]
    public bool dustCoverStartsOpen = false;
    [Tooltip("Firing with the cover closed throws it open (the case and the bolt carrier blow it open). It then stays open until you close it.")]
    public bool dustCoverOpensWhenFired = true;
    [Tooltip("Pulling the bolt back (charging handle or the action cycling) also pushes the cover open once the bolt has travelled this fraction of the way back. 0 = off. Needs a Bolt Carrier.")]
    [Range(0f, 1f)] public float dustCoverOpensAtBolt = 0.15f;

    [Header("Fire Selector Switch (visual)")]
    [Tooltip("The selector lever / switch on the model. It moves to the position of the current fire mode, so what the player sees is always what the gun will do. " +
             "Pick Rotate or Move. The positions are set per fire mode below (the Amount in here is not used).")]
    public PartMotion selectorLever = new PartMotion { kind = PartMotion.Kind.Rotate, axis = Vector3.right, stiffness = 700f, dampingRatio = 1f };
    [Tooltip("Position when the selector is on SAFE: degrees (Rotate) or metres (Move) from the pose in the prefab. Leave 0 and pose the model on safe.")]
    public float selectorAngleSafe = 0f;
    [Tooltip("Position on SEMI. AR-15: 90 degrees. AK: 40 degrees.")]
    public float selectorAngleSemi = 90f;
    [Tooltip("Position on BURST (only if the gun has burst).")]
    public float selectorAngleBurst = 135f;
    [Tooltip("Position on AUTO. AR-15: 180 degrees. AK: 20 degrees (the AK's switch only travels about 40 degrees: safe, then auto, then semi).")]
    public float selectorAngleAuto = 180f;

    // Old single-transform fields. Kept hidden only so existing weapons carry their settings over to the new parts above.
    [HideInInspector] public Transform boltCatch;
    [HideInInspector] public Vector3 boltCatchAxis = Vector3.right;
    [HideInInspector] public float boltCatchOpenAngle = 15f;
    [HideInInspector] public float boltCatchStiffness = 900f;
    [HideInInspector] public float boltCatchDampingRatio = 0.55f;
    [HideInInspector] public Transform selectorSwitch;
    [HideInInspector] public Vector3 selectorAxis = Vector3.right;

    [Header("Action")]
    public ActionType action = ActionType.SelfLoading;
    [Tooltip("Cyclic rate: rounds per minute while the trigger is held. M16 ~800, AK-47 ~600, Glock 17 ~1100 (semi only in practice).")]
    public float roundsPerMinute = 750f;
    public bool allowSemi = true;
    public bool allowBurst = false;
    public bool allowAuto = true;
    [Tooltip("Rounds per burst. A burst finishes even if you let go of the trigger, then needs a new pull.")]
    [Min(2)] public int burstCount = 3;
    public FireMode startMode = FireMode.Semi;
    [Tooltip("Seconds the selector switch takes to move (no firing meanwhile).")]
    public float selectorTime = 0.18f;
    [Tooltip("Seconds to work the charging handle / bolt (no firing meanwhile).")]
    public float rackTime = 0.5f;
    [Tooltip("A self-loading gun with a magazine in it locks its bolt back when the magazine runs dry.")]
    public bool holdOpenOnEmpty = true;
    [Tooltip("After a click on an empty chamber or a misfire, the hammer stays down until the action is cycled (most semi-autos). " +
             "Off = every trigger pull re-cocks, like a double-action revolver.")]
    public bool hammerStaysDown = true;
    [Tooltip("Seconds a trigger press is remembered while the gun is still cycling, so fast clicking doesn't lose shots.")]
    public float pressBufferTime = 0.1f;

    [Header("Barrel & Ballistics")]
    [Tooltip("Longer barrel = more muzzle velocity (see the ammo's Velocity Per Barrel Meter). AR-15 carbine 0.41, AK 0.415, pistol 0.1.")]
    public float barrelLengthMeters = 0.41f;
    [Tooltip("Distance where your sights and the bullet's path cross. The bullet climbs to meet the sight line here, then drops past it.")]
    public float zeroDistanceMeters = 100f;
    [Tooltip("The gun's own accuracy in minutes of angle, combined with the ammo's. Match rifle ~0.5, service rifle ~2, SMG ~4.")]
    public float weaponDispersionMoa = 1.5f;
    [Tooltip("Height of the sights above the bore (m), used when the weapon has no WeaponSights anchor. AR-15 ~0.07, pistol ~0.025.")]
    public float fallbackSightHeight = 0.065f;

    [Header("Recoil (momentum)")]
    [Tooltip("Hot propellant gas leaves the muzzle faster than the bullet, adding to the push. Real-world value is about 1.5.")]
    public float gasVelocityFactor = 1.5f;
    [Tooltip("Fraction of the free-recoil impulse that reaches the shooter. Gas systems, buffers and heavy bolt carriers soak up part: ~0.75 for a gas rifle, 1 for a fixed-breech gun.")]
    [Range(0.2f, 1.2f)] public float recoilTransfer = 0.75f;
    [Tooltip("Used when Muzzle and Recoil Pivot are not both set. Height of the bore above the shoulder contact (m).")]
    public float fallbackBoreHeight = 0.05f;
    [Tooltip("Used when Muzzle and Recoil Pivot are not both set. Distance from the shoulder to the gun's centre of mass (m).")]
    public float fallbackCenterOfMass = 0.3f;
    [Tooltip("Sideways offset of the bore from the shoulder contact (m, +right). A little of this makes the gun drift sideways, as real guns do.")]
    public float fallbackLateralOffset = 0.005f;

    [Header("Ejection Physics")]
    [Tooltip("Direction (in the Ejection Port's space) a case flies. Default: out to the right and a bit up/back.")]
    public Vector3 ejectionDirection = new Vector3(1f, 0.5f, -0.3f);
    public float ejectionSpeed = 3f;
    public float ejectionSpin = 25f;
    public float casingLifetime = 12f;

    [Header("Bolt Animation (spring)")]
    [Tooltip("Metres the carrier slides back.")]
    public float boltTravel = 0.07f;
    [Tooltip("Local direction the carrier moves when it goes back (toward the shooter).")]
    public Vector3 boltAxis = Vector3.back;
    [Tooltip("Optional. The charging handle, when it is a SEPARATE part from the bolt carrier. It can sit anywhere in the hierarchy, even as the parent " +
             "of the bolt carrier. When you drag it back it takes the carrier with it. Leave empty if the handle IS the carrier (or has none).")]
    public Transform chargingHandle;
    [Tooltip("Metres the handle slides back when pulled. 0 = same as Bolt Travel.")]
    public float chargingHandleTravel = 0f;
    [Tooltip("OFF (AR-15, bolt-actions): the handle only moves while you hold it and springs forward when you let go. The carrier moves on its own, so the " +
             "handle stays still while firing and while the bolt is locked open.\nON (AK-47): the handle is fixed to the carrier and moves with it, also when firing.")]
    public bool chargingHandleReciprocates = false;
    [Tooltip("How quickly the handle springs back forward after you let go (seconds, smaller = faster).")]
    public float chargingHandleReturnTime = 0.07f;
    [Tooltip("Speed (m/s) the carrier is thrown back at when a round is fired.")]
    public float boltCycleSpeed = 4f;
    public float boltStiffness = 1800f;
    public float boltDamping = 30f;

    [Header("Magazine Link")]
    [Tooltip("slotID of the WeaponAttachments slot that shows the magazine. The attachment is the model, this component holds the rounds. " +
             "Empty = no link: a virtual magazine is used.")]
    public string magazineSlotId = "magazine";
    [Tooltip("Capacity of the magazine when its attachment doesn't say (AttachmentStats.magazineCapacity).")]
    public int fallbackMagazineCapacity = 30;

    [Header("Start State (when not restored from a save)")]
    public bool startLoaded = true;
    public AmmoData startAmmo;
    [Tooltip("Rounds in the magazine at start. -1 = full.")]
    public int startMagazineRounds = -1;
    [Tooltip("Start with a round already in the chamber (the usual '+1').")]
    public bool startChambered = true;
    [Tooltip("Only for weapons WITHOUT a magazine slot: start with a virtual magazine inserted.")]
    public bool startWithVirtualMagazine = true;

    [Header("Sounds (optional)")]
    public AudioClip fireSound;
    public AudioClip dryFireSound;
    public AudioClip rackSound;
    [Range(0f, 1f)] public float soundVolume = 1f;

    [Header("Events")]
    public UnityEvent onFired;
    [Tooltip("Dry fire, misfire or hammer-down click.")]
    public UnityEvent onClick;
    public UnityEvent onRack;

    // ------------------------------------------------------------------ public state

    public FireMode Mode { get; private set; }
    public ChamberState Chamber { get; private set; }
    public AmmoData ChamberedAmmo { get; private set; }
    /// <summary>Bolt locked back / held open. The chamber is empty and nothing can fire until it is released.</summary>
    public bool BoltOpen { get; private set; }
    public bool HammerCocked { get; private set; }
    /// <summary>The magazine in the gun (null = none inserted).</summary>
    public MagazineContents Magazine => magazine;
    public bool HasMagazine => magazine != null;
    public int MagazineRounds => magazine != null ? magazine.Count : 0;
    /// <summary>Rounds that can still be fired: what is in the magazine, plus one if the chamber is Live.</summary>
    public int RoundsAvailable => MagazineRounds + (Chamber == ChamberState.Live ? 1 : 0);
    /// <summary>Capacity of the inserted magazine (0 with no magazine).</summary>
    public int MagazineCapacity => magazine != null ? magazine.capacity : 0;
    /// <summary>How many more rounds the inserted magazine takes.</summary>
    public int MagazineSpace => magazine != null ? magazine.FreeSpace : 0;
    public bool MagazineIsFull => magazine != null && magazine.IsFull;
    public bool IsBusy => Time.time < busyUntil;
    public float CycleInterval => 60f / Mathf.Max(1f, roundsPerMinute);
    public bool IsHeld => pickup != null && pickup.IsHeld;
    public Transform MuzzleTransform => muzzle != null ? muzzle : transform;
    /// <summary>
    /// Everything you are holding, in kg: the weapon (its ItemData mass), every attachment (magazine included)
    /// and the cartridges in the magazine and chamber. Recoil, handling and the inspect weight all use this.
    /// </summary>
    public float TotalMassKg
    {
        get
        {
            float bare = attachments != null ? attachments.TotalWeightKg : (pickup != null ? pickup.MassKg : 3.5f);
            return bare + LoadedAmmoMassKg();
        }
    }

    // Cached because WeaponRecoil asks every frame: only re-summed when the rounds change.
    MagazineContents massMag; int massCount = -1; ChamberState massChamber; AmmoData massChamberAmmo; float massCache;

    float LoadedAmmoMassKg()
    {
        if (massMag == magazine && massCount == MagazineRounds && massChamber == Chamber && massChamberAmmo == ChamberedAmmo)
            return massCache;

        float kg = 0f;
        if (magazine != null && ammoDatabase != null)
            foreach (string id in magazine.rounds)
            {
                AmmoData a = ammoDatabase.Get(id);
                if (a != null) kg += a.CartridgeMassKg;
            }
        if (ChamberedAmmo != null)
        {
            if (Chamber == ChamberState.Live) kg += ChamberedAmmo.CartridgeMassKg;
            else if (Chamber == ChamberState.Spent) kg += ChamberedAmmo.CasingMassKg;
        }

        massMag = magazine; massCount = MagazineRounds; massChamber = Chamber; massChamberAmmo = ChamberedAmmo;
        massCache = kg;
        return kg;
    }
    /// <summary>The recoil pivot in this weapon's local space (zero if none is set).</summary>
    public Vector3 RecoilPivotLocal => recoilPivot != null ? transform.InverseTransformPoint(recoilPivot.position) : Vector3.zero;
    /// <summary>Orientation of the Recoil Pivot relative to the weapon root (its blue axis is the way the bullet leaves).</summary>
    public Quaternion RecoilPivotLocalRotation => recoilPivot != null ? Quaternion.Inverse(transform.rotation) * recoilPivot.rotation : Quaternion.identity;

    /// <summary>What bullets can hit. FirearmController sets this to everything except the player.</summary>
    public int HitMask { get; set; } = Physics.DefaultRaycastLayers;
    /// <summary>Cases ignore these so they never shove the shooter. FirearmController fills it in.</summary>
    public Collider[] OwnerColliders { get; set; }
    /// <summary>Shooter's velocity, handed to ejected cases so they inherit the motion.</summary>
    public Vector3 OwnerVelocity { get; set; }

    // ------------------------------------------------------------------ events

    public event Action<ShotInfo> Fired;
    /// <summary>Dry fire, misfire or hammer-down click.</summary>
    public event Action<TriggerResult> TriggerFailed;
    public event Action<FireMode> ModeChanged;
    public event Action Racked;
    /// <summary>A round or case left the chamber (ammo, wasSpent). A live one is a loose round the player could pick up.</summary>
    public event Action<AmmoData, bool> RoundEjected;
    public event Action<MagazineContents> MagazineInserted;
    /// <summary>The magazine left the gun (or its attachment was removed). Catch this to put it in the player's pouches.</summary>
    public event Action<MagazineContents> MagazineEjected;
    /// <summary>Anything a HUD or inspect screen would show has changed.</summary>
    public event Action StateChanged;

    // ------------------------------------------------------------------ internals

    [Serializable]
    class State
    {
        public int version;
        public int chamber;
        public string chamberAmmoId;
        public bool boltOpen;
        public bool hammerCocked;
        public bool dustCoverOpen;
        public int mode;
        public bool hasMagazine;
        public MagazineContents magazine;
    }

    Pickupable pickup;
    WeaponAttachments attachments;
    WeaponSights sights;
    MagazineContents magazine;
    bool initialized, syncingAttachments;

    bool triggerHeld, semiLatch, triggerSpent;
    float pressBuffer, nextFireTime, busyUntil;
    int burstRemaining;

    bool startFillPending;
    float boltDragTarget;
    bool boltDragPassed, boltDragLock;
    Vector3 boltRestLocal;
    Vector3 boltRestRoot, handleRestRoot, handleRestLocal;
    float handlePos, handleVel, boltDragFloor;
    bool handleCoupled; // after a release the handle rides the carrier home instead of springing on its own
    float boltPos, boltVel, rackHoldUntil;

    // ================================================================== lifecycle

    void Awake()
    {
        pickup = GetComponent<Pickupable>();
        attachments = GetComponent<WeaponAttachments>();
        sights = GetComponent<WeaponSights>();
        if (attachments != null) attachments.Changed += OnAttachmentChanged;
        if (boltCarrier != null)
        {
            boltRestLocal = boltCarrier.localPosition;
            // Remembered relative to the WEAPON, not the carrier's parent: a carrier that is a child of the handle must not
            // inherit the handle's movement on top of its own.
            boltRestRoot = transform.InverseTransformPoint(boltCarrier.position);
        }
        if (chargingHandle != null && chargingHandle != boltCarrier)
        {
            handleRestLocal = chargingHandle.localPosition;
            handleRestRoot = transform.InverseTransformPoint(chargingHandle.position);
        }
        MigrateLegacyParts();
        boltCatchLever.Capture();
        selectorLever.Capture();
        chargingHandleLatch.Capture();
        magazineReleaseButton.Capture();
        dustCover.Capture();
        DustCoverOpen = dustCoverStartsOpen;
        Mode = ValidMode(startMode);
        HammerCocked = false;

        if (muzzle == null)
            Debug.LogWarning("[Firearm] No Muzzle assigned: bullets will start at the weapon's pivot.", this);
    }

    void OnDestroy()
    {
        if (attachments != null) attachments.Changed -= OnAttachmentChanged;
    }

    void Start()
    {
        if (initialized) return; // a save was restored
        initialized = true;

        if (!startLoaded || startAmmo == null) return;

        bool hasMagSlot = TryGetMagazineSlot(out _);

        // The weapon's own default magazine may not be installed yet (attachments install their default loadout in their own
        // Start). Remember to fill THAT magazine the moment it appears, so the start rounds aren't lost to script order.
        // Only when the loadout really installs one: a gun that starts with no magazine stays that way, and a magazine
        // the player installs later is never touched.
        if (hasMagSlot && magazine == null && attachments.HasDefaultFor(magazineSlotId)) startFillPending = true;
        if (!hasMagSlot && startWithVirtualMagazine && magazine == null)
            magazine = new MagazineContents { caliber = caliber, capacity = Mathf.Max(1, fallbackMagazineCapacity) };

        if (magazine != null)
        {
            magazine.rounds.Clear(); // the start state decides what the starting magazine holds
            magazine.Fill(startAmmo, startMagazineRounds);
        }

        if (startChambered)
        {
            Chamber = ChamberState.Live;
            ChamberedAmmo = startAmmo;
            HammerCocked = true;
        }
        StateChanged?.Invoke();
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        UpdateBolt(dt);
        UpdateBoltCatch(dt);
        chargingHandleLatch.Tick(transform, BoltDragging, dt);
        magazineReleaseButton.Tick(transform, Time.time < magReleasePressedUntil, dt);
        UpdateDustCover(dt);
        UpdateSelector(dt);
        FireLoop(dt);
        UpdateChamberVisual();
    }

    // ================================================================== chamber visual

    GameObject chamberVisual;
    ChamberState shownChamber = ChamberState.Empty;
    AmmoData shownAmmo;

    /// <summary>
    /// Keeps a model of the chambered round on Chamber Round Mount: the live cartridge, the spent case, or nothing.
    /// It follows the Chamber state, so loading, firing, racking and loading from a save all show the right thing.
    /// </summary>
    void UpdateChamberVisual()
    {
        if (chamberRoundMount == null) return;

        ChamberState state = ChamberedAmmo != null ? Chamber : ChamberState.Empty;
        AmmoData ammoNow = state == ChamberState.Empty ? null : ChamberedAmmo;
        bool upToDate = state == shownChamber && ammoNow == shownAmmo && (state == ChamberState.Empty || chamberVisual != null || PrefabFor(ammoNow, state) == null);
        if (upToDate) return;

        if (chamberVisual != null) { Destroy(chamberVisual); chamberVisual = null; }
        shownChamber = state;
        shownAmmo = ammoNow;
        if (state == ChamberState.Empty) return;

        GameObject prefab = PrefabFor(ammoNow, state);
        if (prefab == null) return;

        chamberVisual = Instantiate(prefab, chamberRoundMount);
        Transform t = chamberVisual.transform;
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;

        // Keep the round its real size even if the parts it hangs on are scaled (a carrier imported at scale 100, say).
        Vector3 parentScale = chamberRoundMount.lossyScale;
        Vector3 want = prefab.transform.localScale;
        t.localScale = new Vector3(want.x / Mathf.Max(0.0001f, parentScale.x),
                                   want.y / Mathf.Max(0.0001f, parentScale.y),
                                   want.z / Mathf.Max(0.0001f, parentScale.z));

        // It is only a model here: no physics, no self-destruct timers copied from the ejected version.
        foreach (Rigidbody rb in chamberVisual.GetComponentsInChildren<Rigidbody>())
        {
            rb.isKinematic = true;
            rb.detectCollisions = false;
            Destroy(rb);
        }
        foreach (Collider c in chamberVisual.GetComponentsInChildren<Collider>())
        {
            c.enabled = false;
            Destroy(c);
        }
    }

    static GameObject PrefabFor(AmmoData ammoData, ChamberState state)
    {
        if (ammoData == null) return null;
        return state == ChamberState.Live ? ammoData.cartridgePrefab : state == ChamberState.Spent ? ammoData.casingPrefab : null;
    }

    // ================================================================== dust cover

    /// <summary>True while the ejection port cover is open.</summary>
    public bool DustCoverOpen { get; private set; }

    float prevBoltFraction;

    public void OpenDustCover() => DustCoverOpen = true;
    public void CloseDustCover() => DustCoverOpen = false;
    public void ToggleDustCover() => DustCoverOpen = !DustCoverOpen;

    void UpdateDustCover(float dt)
    {
        // The bolt coming back pushes the cover open. Only the moment it passes the mark counts, so a cover you close
        // while the bolt is locked back stays closed.
        if (dustCoverOpensAtBolt > 0.0001f && boltCarrier != null)
        {
            float f = BoltFraction;
            if (prevBoltFraction < dustCoverOpensAtBolt && f >= dustCoverOpensAtBolt) DustCoverOpen = true;
            prevBoltFraction = f;
        }
        dustCover.Tick(transform, DustCoverOpen, dt);
    }

    void OnValidate()
    {
        MigrateLegacyParts(); // shows an old weapon's bolt catch / selector settings in the new fields
        roundsPerMinute = Mathf.Max(1f, roundsPerMinute);
        barrelLengthMeters = Mathf.Max(0.02f, barrelLengthMeters);
        if (action == ActionType.Manual)
        {
            allowSemi = true;
            allowBurst = false;
            allowAuto = false;
        }
    }

    // ================================================================== trigger

    /// <summary>
    /// Called every frame by whoever owns the trigger (FirearmController). true = finger on the trigger.
    /// The shooting itself happens in this component's LateUpdate so it uses this frame's final muzzle pose.
    /// </summary>
    public void SetTrigger(bool held)
    {
        if (held && !triggerHeld)
        {
            pressBuffer = pressBufferTime;
            semiLatch = false;
            triggerSpent = false;
            if (Mode == FireMode.Burst) burstRemaining = Mathf.Max(2, burstCount);
        }
        else if (!held && triggerHeld)
        {
            semiLatch = false;
            triggerSpent = false;
        }
        triggerHeld = held;
    }

    /// <summary>Let go of the trigger and cancel any burst in progress.</summary>
    public void ReleaseTrigger()
    {
        SetTrigger(false);
        burstRemaining = 0;
        pressBuffer = 0f;
    }

    bool WantsToShoot()
    {
        if (triggerSpent) return false;
        switch (Mode)
        {
            case FireMode.Semi: return pressBuffer > 0f && !semiLatch;
            case FireMode.Burst: return burstRemaining > 0;
            case FireMode.Auto: return triggerHeld;
            default: return false;
        }
    }

    void FireLoop(float dt)
    {
        // A gun lying on the floor never fires itself.
        if (!IsHeld)
        {
            if (triggerHeld || burstRemaining > 0) ReleaseTrigger();
            return;
        }

        if (pressBuffer > 0f) pressBuffer -= dt;

        // More than one round can be due in a long frame; fire them all, but never an unbounded pile.
        for (int guard = 0; guard < 4 && WantsToShoot(); guard++)
        {
            TriggerResult result = PullTrigger();
            if (result == TriggerResult.Busy) break;

            if (result == TriggerResult.Fired)
            {
                pressBuffer = 0f;
                if (Mode == FireMode.Semi) semiLatch = true;
                if (Mode == FireMode.Burst) burstRemaining--;
            }
            else
            {
                // A click (or nothing to do): once per pull, then wait for the trigger to be released.
                burstRemaining = 0;
                pressBuffer = 0f;
                triggerSpent = true;
                break;
            }
        }
    }

    /// <summary>
    /// One trigger pull: fires if a live round is chambered and the hammer is cocked, otherwise clicks.
    /// The magazine is never looked at here. That is the point: only the chamber matters.
    /// </summary>
    public TriggerResult PullTrigger()
    {
        if (Mode == FireMode.Safe) return TriggerResult.Safe;
        if (Time.time < busyUntil || Time.time < nextFireTime) return TriggerResult.Busy;
        if (BoltOpen) return TriggerResult.BoltOpen;

        if (hammerStaysDown && !HammerCocked)
        {
            Click(TriggerResult.HammerDown);
            return TriggerResult.HammerDown;
        }

        if (Chamber != ChamberState.Live || ChamberedAmmo == null)
        {
            // Hammer falls on nothing (empty chamber) or on a fired case.
            if (hammerStaysDown) HammerCocked = false;
            Click(TriggerResult.DryFire);
            return TriggerResult.DryFire;
        }

        if (UnityEngine.Random.value < ChamberedAmmo.misfireChance)
        {
            // Primer didn't light. The live round stays in the chamber; rack the action to clear it.
            if (hammerStaysDown) HammerCocked = false;
            Click(TriggerResult.Misfire);
            return TriggerResult.Misfire;
        }

        Shoot();
        return TriggerResult.Fired;
    }

    void Click(TriggerResult result)
    {
        PlayClip(dryFireSound);
        onClick?.Invoke();
        TriggerFailed?.Invoke(result);
        StateChanged?.Invoke();
    }

    // ================================================================== the shot

    void Shoot()
    {
        AmmoData ammo = ChamberedAmmo;
        float speed = MuzzleVelocity(ammo);
        GetShotRay(ammo, speed, out Vector3 origin, out Vector3 direction);

        BulletManager.Instance.Fire(origin, direction * speed, ammo, HitMask);

        // Momentum conservation: the gun is pushed back with the momentum of everything that left the barrel.
        // Bullet: m*v. Powder gas: roughly its mass times ~1.5x the bullet velocity.
        float impulse = (ammo.BulletMassKg * speed + ammo.PowderMassKg * gasVelocityFactor * speed)
                        * recoilTransfer
                        * (attachments != null ? attachments.TotalStats.recoilMultiplier : 1f);

        // The round is spent, but its case is still in the chamber until the action cycles.
        Chamber = ChamberState.Spent;
        HammerCocked = false;

        // Keep the cyclic rhythm exact while firing continuously; a fresh pull starts a new rhythm.
        float interval = CycleInterval;
        nextFireTime = (Time.time - nextFireTime < interval) ? nextFireTime + interval : Time.time + interval;

        if (boltCarrier != null) boltVel += boltCycleSpeed;
        if (action == ActionType.SelfLoading) CycleAction();
        if (dustCoverOpensWhenFired) DustCoverOpen = true;

        PlayClip(fireSound);
        onFired?.Invoke();
        Fired?.Invoke(new ShotInfo
        {
            ammo = ammo,
            position = origin,
            direction = direction,
            muzzleVelocity = speed,
            bulletMassKg = ammo.BulletMassKg,
            recoilImpulse = impulse
        });
        StateChanged?.Invoke();
    }

    /// <summary>Muzzle velocity in m/s for this ammo from this barrel, with attachment effects.</summary>
    public float MuzzleVelocity(AmmoData ammo)
    {
        if (ammo == null) return 0f;
        float v = ammo.muzzleVelocity + (barrelLengthMeters - ammo.referenceBarrelMeters) * ammo.velocityPerBarrelMeter;
        if (attachments != null) v *= attachments.TotalStats.muzzleVelocityMultiplier;
        return Mathf.Max(50f, v);
    }

    void GetShotRay(AmmoData ammo, float speed, out Vector3 origin, out Vector3 direction)
    {
        Transform m = MuzzleTransform;
        origin = m.position;
        Vector3 right = m.right;
        Vector3 up = m.up;

        // Zero: raise the bullet's path just enough to cross the line of sight at the zero distance.
        // (The sights sit above the bore, so the bullet starts below the sight line and has to climb to meet it.)
        float elevation = BulletManager.SolveZeroElevation(ammo, speed, SightHeight(), zeroDistanceMeters);
        Vector3 forward = Quaternion.AngleAxis(-elevation * Mathf.Rad2Deg, right) * m.forward;

        // Dispersion: weapon and ammo accuracy add in quadrature; a point inside the cone.
        float moa = Mathf.Sqrt(weaponDispersionMoa * weaponDispersionMoa + ammo.dispersionMoa * ammo.dispersionMoa);
        if (attachments != null) moa *= attachments.TotalStats.spreadMultiplier;
        Vector2 spread = UnityEngine.Random.insideUnitCircle * (moa * 0.5f * MoaToRad * Mathf.Rad2Deg);
        forward = Quaternion.AngleAxis(spread.x, up) * Quaternion.AngleAxis(spread.y, right) * forward;
        direction = forward.normalized;

        // Barrel poked into a wall: the bullet starts on the near side of it and hits it straight away.
        if (Physics.Linecast(transform.position, origin, out RaycastHit wall, HitMask, QueryTriggerInteraction.Ignore))
            origin = wall.point + wall.normal * 0.002f;
    }

    float SightHeight()
    {
        Transform anchor = sights != null ? sights.CurrentSightAnchor : null;
        if (anchor == null || muzzle == null) return fallbackSightHeight;
        float h = Vector3.Dot(anchor.position - muzzle.position, muzzle.up);
        return Mathf.Max(0f, h);
    }

    /// <summary>
    /// Geometry WeaponRecoil needs: how high the bore sits above the shoulder contact, how far the centre of mass is
    /// from it, and the sideways offset. Measured from the Muzzle and Recoil Pivot transforms when both are set.
    /// </summary>
    public void GetRecoilGeometry(out float boreHeight, out float centerOfMassDistance, out float lateralOffset)
    {
        boreHeight = fallbackBoreHeight;
        centerOfMassDistance = fallbackCenterOfMass;
        lateralOffset = fallbackLateralOffset;
        if (muzzle == null || recoilPivot == null) return;

        // Everything is measured in the RECOIL PIVOT's own axes, so rotating that empty matters:
        //   blue (forward) = the way the bullet leaves, green (up) = the gun's up, red (right) = the gun's right.
        // (The muzzle's own rotation no longer affects the recoil direction, only where bullets start and point.)
        Vector3 toMuzzle = muzzle.position - recoilPivot.position;
        boreHeight = Vector3.Dot(toMuzzle, recoilPivot.up);
        lateralOffset = Vector3.Dot(toMuzzle, recoilPivot.right) * recoilLateralScale;
        centerOfMassDistance = Mathf.Max(0.05f, Mathf.Abs(Vector3.Dot(toMuzzle, recoilPivot.forward)) * 0.45f);
    }

    void OnDrawGizmosSelected()
    {
        // Magazine miss axes: magenta line from the deepest point out along the travel direction.
        if (magazineMissAxes != null)
        {
            Gizmos.color = Color.magenta;
            foreach (MagazineMissAxis m in magazineMissAxes)
            {
                if (m == null || m.axis == null) continue;
                Vector3 a = m.axis.position;
                Gizmos.DrawWireSphere(a, 0.004f);
                Gizmos.DrawLine(a, a + m.axis.forward * Mathf.Max(0.01f, m.length));
            }
        }

        // Chambered round mount: cyan arrow, pointing the way the nose of the cartridge points (down the barrel).
        if (chamberRoundMount != null)
        {
            Gizmos.color = Color.cyan;
            Vector3 c = chamberRoundMount.position;
            Gizmos.DrawWireSphere(c, 0.003f);
            Gizmos.DrawLine(c, c + chamberRoundMount.forward * 0.03f);
        }

        if (recoilPivot == null) return;
        Vector3 p = recoilPivot.position;
        float len = 0.12f;
        Gizmos.color = Color.red; Gizmos.DrawLine(p, p + recoilPivot.right * len);
        Gizmos.color = Color.green; Gizmos.DrawLine(p, p + recoilPivot.up * len);
        Gizmos.color = Color.blue; Gizmos.DrawLine(p, p + recoilPivot.forward * len);
        if (muzzle == null) return;

        // Pivot to muzzle, split into the parts that make climb (green) and sideways kick (red).
        Vector3 toMuzzle = muzzle.position - p;
        Vector3 up = recoilPivot.up * Vector3.Dot(toMuzzle, recoilPivot.up);
        Vector3 side = recoilPivot.right * Vector3.Dot(toMuzzle, recoilPivot.right);
        Gizmos.color = Color.white; Gizmos.DrawLine(p, muzzle.position);
        Gizmos.color = Color.green; Gizmos.DrawLine(p, p + up);
        Gizmos.color = Color.red; Gizmos.DrawLine(p + up, p + up + side);
    }

    // ================================================================== action

    /// <summary>After a shot in a self-loading gun: eject the case, strip the next round, re-cock.</summary>
    void CycleAction()
    {
        EjectChamber();
        HammerCocked = true;

        if (!FeedFromMagazine() && holdOpenOnEmpty && magazine != null)
            BoltOpen = true; // the magazine follower pushes the bolt catch up: bolt locks back on an empty magazine
    }

    /// <summary>Throws whatever is in the chamber out of the ejection port (a case, or a live round).</summary>
    void EjectChamber()
    {
        if (Chamber == ChamberState.Empty) return;

        AmmoData ammo = ChamberedAmmo;
        bool spent = Chamber == ChamberState.Spent;
        Chamber = ChamberState.Empty;
        ChamberedAmmo = null;

        if (ammo != null)
        {
            SpawnEjected(ammo, spent);
            RoundEjected?.Invoke(ammo, spent);
        }
    }

    // ---------------------------------------------------------------- can another round be chambered?

    /// <summary>Why the magazine can or can't supply the chamber with another round.</summary>
    public MagazineFeedStatus GetMagazineFeedStatus()
    {
        if (magazine == null) return MagazineFeedStatus.NoMagazine;
        if (magazine.IsEmpty) return MagazineFeedStatus.MagazineEmpty;

        AmmoData next = Resolve(magazine.Peek());
        if (next == null || next.caliber != caliber) return MagazineFeedStatus.WrongCaliber;
        return MagazineFeedStatus.Ready;
    }

    /// <summary>
    /// True if working the action right now would put a fresh round from the magazine into the chamber.
    /// (A round or spent case already in the chamber is thrown out first, so this is about the magazine, not the chamber.)
    /// </summary>
    public bool CanChamberFromMagazine => !IsBusy && GetMagazineFeedStatus() == MagazineFeedStatus.Ready;

    /// <summary>The chamber is empty, so a round fed by the bolt (or fired off a belt of one) has somewhere to go.</summary>
    public bool ChamberIsEmpty => Chamber == ChamberState.Empty;

    /// <summary>True if a round can be dropped straight into the chamber by hand: bolt locked open and the chamber empty.</summary>
    public bool CanLoadChamberByHand => !IsBusy && BoltOpen && Chamber == ChamberState.Empty;

    /// <summary>
    /// The one-line answer for the reload system: can this gun take another round into its chamber, by any route?
    /// Either the magazine can feed one (rack the action) or the player can place one by hand (bolt open, chamber empty).
    /// </summary>
    public bool CanLoadAnotherRound => CanChamberFromMagazine || CanLoadChamberByHand;

    /// <summary>The bolt strips the top round off the magazine into the chamber. False if there was nothing to feed.</summary>
    bool FeedFromMagazine()
    {
        if (Chamber != ChamberState.Empty || magazine == null || magazine.IsEmpty) return false;

        // Read before taking: a wrong-caliber or unknown round never feeds, and stays in the magazine.
        AmmoData ammo = Resolve(magazine.Peek());
        if (ammo == null)
        {
            Debug.LogWarning($"[Firearm] Can't find ammo '{magazine.Peek()}'. Is the Ammo Database assigned and does it contain it?", this);
            return false;
        }
        if (ammo.caliber != caliber) return false;
        magazine.Pop();

        Chamber = ChamberState.Live;
        ChamberedAmmo = ammo;
        return true;
    }

    AmmoData Resolve(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        AmmoData found = ammoDatabase != null ? ammoDatabase.Get(id) : null;
        if (found == null && startAmmo != null && startAmmo.ammoId == id) found = startAmmo;
        return found;
    }

    /// <summary>
    /// Work the charging handle (or, if the bolt is locked back, release it).
    /// With the bolt closed: it comes back, whatever is in the chamber (a live round or a case) is thrown out, then it
    /// goes forward, strips the next round from the magazine and cocks the hammer. This is how you fix a misfire,
    /// load the first round after a magazine change, and work a bolt-action rifle.
    /// </summary>
    public bool Rack()
    {
        if (IsBusy) return false;

        busyUntil = Time.time + rackTime;
        rackHoldUntil = Time.time + rackTime * 0.5f;

        // Bolt closed: it travels back and flings the chamber's contents clear.
        // Bolt locked back: releasing it leaves anything the player dropped into the chamber where it is.
        if (!BoltOpen) EjectChamber();

        BoltOpen = false;
        HammerCocked = true;
        if (!FeedFromMagazine() && action == ActionType.SelfLoading && holdOpenOnEmpty && magazine != null)
            BoltOpen = true; // an empty magazine is in: the bolt catches again

        PlayClip(rackSound);
        onRack?.Invoke();
        Racked?.Invoke();
        StateChanged?.Invoke();
        return true;
    }

    // ---------------------------------------------------------------- charging handle (dragged by the player)

    /// <summary>True while the player holds the charging handle.</summary>
    public bool BoltDragging { get; private set; }
    /// <summary>How far back the bolt is right now: 0 = in battery, 1 = fully back.</summary>
    public float BoltFraction => boltTravel > 0.0001f ? Mathf.Clamp01(boltPos / boltTravel) : 0f;
    /// <summary>World position of the bolt carrier when closed.</summary>
    public Vector3 BoltRestWorld => boltCarrier == null ? transform.position : transform.TransformPoint(boltRestRoot);

    // The part the player's hand actually pulls: the separate charging handle if there is one, otherwise the carrier itself.
    bool HandleActive => chargingHandle != null && chargingHandle != boltCarrier;
    float HandleTravel => chargingHandleTravel > 0.0001f ? chargingHandleTravel : boltTravel;
    /// <summary>How far back the part the hand pulls is: 0 = forward, 1 = fully back.</summary>
    public float HandleFraction => HandleActive ? Mathf.Clamp01(handlePos / Mathf.Max(0.0001f, HandleTravel)) : BoltFraction;
    /// <summary>World position of the part the hand pulls, when forward. The drag runs along a line from here.</summary>
    public Vector3 DragRestWorld => HandleActive ? transform.TransformPoint(handleRestRoot) : BoltRestWorld;
    /// <summary>World direction the hand pulls it.</summary>
    public Vector3 DragAxisWorld
    {
        get
        {
            Vector3 a = boltAxis.sqrMagnitude > 0.0001f ? boltAxis.normalized : Vector3.back;
            Transform p = AxisFrame;
            return p != null ? p.TransformDirection(a).normalized : a;
        }
    }
    /// <summary>Full pull length of the part the hand pulls, in world metres.</summary>
    public float DragTravelWorld
    {
        get
        {
            if (!HandleActive) return BoltTravelWorld;
            return WorldOffset(HandleTravel).magnitude;
        }
    }
    /// <summary>World direction the carrier slides when it goes back.</summary>
    public Vector3 BoltAxisWorld
    {
        get
        {
            Vector3 a = boltAxis.sqrMagnitude > 0.0001f ? boltAxis.normalized : Vector3.back;
            return boltCarrier != null && boltCarrier.parent != null ? boltCarrier.parent.TransformDirection(a).normalized : a;
        }
    }
    /// <summary>Full bolt travel in world metres.</summary>
    public float BoltTravelWorld
    {
        get
        {
            return boltCarrier != null ? WorldOffset(boltTravel).magnitude : boltTravel;
        }
    }

    /// <summary>The player grabs the charging handle.</summary>
    public bool BeginBoltDrag()
    {
        if (boltCarrier == null || BoltDragging || IsBusy) return false;
        BoltDragging = true;
        boltDragTarget = HandleFraction;
        boltDragFloor = BoltOpen ? BoltFraction : 0f; // a bolt locked back stays back while the handle is pulled
        boltDragPassed = false;
        boltDragLock = false;
        return true;
    }

    /// <summary>Where the hand has pulled the handle (0 = closed, 1 = fully back). Past Bolt Drag Eject At the chamber is cleared.</summary>
    public void SetBoltDrag(float fraction)
    {
        if (!BoltDragging) return;
        boltDragTarget = Mathf.Clamp01(fraction);
        if (!boltDragPassed && boltDragTarget >= boltDragEjectAt)
        {
            boltDragPassed = true;
            if (!BoltOpen) EjectChamber();   // a live round or a case is thrown out of the ejection port
            HammerCocked = true;             // the carrier cocks the hammer on its way back
            PlayClip(rackSound);
            StateChanged?.Invoke();
        }
        else if (boltDragPassed && !boltDragLock && !BoltOpen && boltDragTarget <= boltDragResetAt)
        {
            // Pushed all the way home again without letting go: the bolt closes and strips the next round into the chamber,
            // and the handle is ready to be pulled back again to eject it (and so on, as many cycles as you like in one hold).
            bool fed = FeedFromMagazine();

            // An empty magazine that would hold the bolt open is left to EndBoltDrag, exactly as when you let go.
            bool wouldHoldOpen = !fed && action == ActionType.SelfLoading && holdOpenOnEmpty && magazine != null;
            if (!wouldHoldOpen)
            {
                boltDragPassed = false;
                onRack?.Invoke();
                Racked?.Invoke();
                StateChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// The player lets go of the handle. If it was pulled all the way back the bolt now goes forward and strips a round from the
    /// magazine (or stays locked open if the bolt catch was pressed, or the magazine is empty). A half pull just springs home.
    /// </summary>
    public void EndBoltDrag()
    {
        if (!BoltDragging) return;
        BoltDragging = false;
        rackHoldUntil = 0f; // from here the spring takes over again

        if (!boltDragPassed) { boltDragLock = false; handleCoupled = !BoltOpen; return; }

        if (boltDragLock || BoltOpen)
        {
            BoltOpen = true;
        }
        else
        {
            BoltOpen = false;
            if (!FeedFromMagazine() && action == ActionType.SelfLoading && holdOpenOnEmpty && magazine != null)
                BoltOpen = true; // empty magazine in: the bolt catches again
        }
        boltDragLock = false;
        handleCoupled = !BoltOpen; // carrier going home: the handle rides it; bolt locked open: the handle springs forward alone
        busyUntil = Mathf.Max(busyUntil, Time.time + 0.1f);
        onRack?.Invoke();
        Racked?.Invoke();
        StateChanged?.Invoke();
    }

    /// <summary>
    /// The bolt catch. Bolt locked open: releases it, it slams forward and chambers the next round. While the charging handle is
    /// held all the way back: locks the bolt open when the handle is let go.
    /// </summary>
    public bool PressBoltCatch()
    {
        if (BoltDragging)
        {
            if (BoltFraction < boltCatchEngageAt) return false;
            boltDragLock = true;
            StateChanged?.Invoke();
            return true;
        }
        if (!BoltOpen || IsBusy) return false;

        busyUntil = Time.time + rackTime * 0.5f;
        BoltOpen = false;
        HammerCocked = true;
        FeedFromMagazine();   // nothing happens if there is already a round in the chamber or the magazine is empty
        PlayClip(rackSound);
        onRack?.Invoke();
        StateChanged?.Invoke();
        return true;
    }

    // ---------------------------------------------------------------- magazine release

    /// <summary>The magazine release is pressed: the magazine is no longer locked in and can slide out (or falls out if nobody holds it).</summary>
    public bool MagazineUnlocked { get; private set; }
    /// <summary>Time.time when the release was pressed.</summary>
    public float MagazineUnlockedAt { get; private set; }

    /// <summary>The slot the magazine installs in (null on weapons without a magazine slot).</summary>
    public AttachmentSlot MagazineSlot
    {
        get { TryGetMagazineSlot(out AttachmentSlot s); return s; }
    }

    float magReleasePressedUntil;

    public bool PressMagazineRelease()
    {
        magReleasePressedUntil = Time.time + magazineReleasePressTime; // the button moves on every press, even if nothing is in the well
        if (magazine == null || MagazineUnlocked) return false;
        MagazineUnlocked = true;
        MagazineUnlockedAt = Time.time;
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>Pull the bolt back and hold it open (chamber contents are thrown out). For loading a round by hand or checking the chamber.</summary>
    public bool OpenBolt()
    {
        if (IsBusy || BoltOpen) return false;

        busyUntil = Time.time + rackTime * 0.6f;
        EjectChamber();
        BoltOpen = true;
        HammerCocked = true; // the carrier cocks the hammer on its way back

        PlayClip(rackSound);
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>Drop a single round straight into the chamber. Only possible with the bolt open and the chamber empty.</summary>
    public bool TryLoadChamber(AmmoData ammo)
    {
        if (ammo == null || ammo.caliber != caliber) return false;
        if (!BoltOpen || Chamber != ChamberState.Empty) return false;

        Chamber = ChamberState.Live;
        ChamberedAmmo = ammo;
        StateChanged?.Invoke();
        return true;
    }

    // ---------------------------------------------------------------- loading rounds into the inserted magazine

    /// <summary>Can this cartridge be pressed into the magazine that is in the gun (right caliber, room left)?</summary>
    public bool CanLoadRoundIntoMagazine(AmmoData ammo)
        => magazine != null && ammo != null && ammo.caliber == caliber && magazine.CanLoad(ammo);

    /// <summary>Press one cartridge into the inserted magazine. Respects the magazine's capacity and caliber.</summary>
    public bool TryLoadRoundIntoMagazine(AmmoData ammo)
    {
        if (!CanLoadRoundIntoMagazine(ammo) || !magazine.TryPush(ammo)) return false;
        StateChanged?.Invoke();
        return true;
    }

    // ================================================================== selector

    bool IsModeAllowed(FireMode mode)
    {
        switch (mode)
        {
            case FireMode.Safe: return true;
            case FireMode.Semi: return allowSemi;
            case FireMode.Burst: return allowBurst;
            case FireMode.Auto: return allowAuto;
            default: return false;
        }
    }

    FireMode ValidMode(FireMode wanted)
    {
        if (IsModeAllowed(wanted)) return wanted;
        if (allowSemi) return FireMode.Semi;
        if (allowBurst) return FireMode.Burst;
        if (allowAuto) return FireMode.Auto;
        return FireMode.Safe;
    }

    /// <summary>Flick the selector to the next available mode: Safe, Semi, Burst, Auto, then back around.</summary>
    public bool CycleFireMode()
    {
        if (IsBusy) return false;

        FireMode next = Mode;
        for (int i = 0; i < 4; i++)
        {
            next = (FireMode)(((int)next + 1) % 4);
            if (IsModeAllowed(next)) break;
        }
        if (next == Mode) return false;

        Mode = next;
        burstRemaining = 0;
        busyUntil = Mathf.Max(busyUntil, Time.time + selectorTime);
        ModeChanged?.Invoke(Mode);
        StateChanged?.Invoke();
        return true;
    }

    // ================================================================== magazine

    public bool CanAcceptMagazine(MagazineContents mag)
        => mag != null && magazine == null && (string.IsNullOrEmpty(mag.caliber) || mag.caliber == caliber);

    /// <summary>Does this magazine model (AttachmentData) take this weapon's caliber? Gives the reason if not.</summary>
    public bool AcceptsMagazineType(AttachmentData type, out string reason)
    {
        reason = null;
        if (type == null) return true;
        if (string.IsNullOrEmpty(type.magazineCaliber))
        {
            reason = "Magazine has no caliber set.";
            Debug.LogWarning($"[Firearm] Magazine '{type.name}' has no Magazine Caliber set, so it can't be installed.", type);
            return false;
        }
        if (type.magazineCaliber != caliber)
        {
            reason = $"Wrong caliber ({type.magazineCaliber}, weapon takes {caliber}).";
            return false;
        }
        return true;
    }

    /// <summary>
    /// Put a magazine in. (The future drag-and-drop reload calls this.) If the weapon has a magazine slot, the
    /// magazine model is installed in it: pass the magazine's AttachmentData, or leave it null and it is looked up
    /// from contents.magazineId in the weapon's AttachmentDatabase.
    /// </summary>
    public bool InsertMagazine(MagazineContents contents, AttachmentData magazineType = null)
    {
        if (!CanAcceptMagazine(contents)) return false;

        if (TryGetMagazineSlot(out AttachmentSlot slot))
        {
            if (magazineType == null && attachments.database != null)
                magazineType = attachments.database.Get(contents.magazineId);

            // The magazine model and its rounds must both match the weapon.
            if (magazineType != null && !AcceptsMagazineType(magazineType, out _)) return false;

            if (magazineType != null)
            {
                syncingAttachments = true;
                bool installed = attachments.Install(slot, magazineType, -1, true, contents);
                syncingAttachments = false;
                if (!installed) return false;
            }
        }

        magazine = contents;
        MagazineUnlocked = false; // seated and locked again
        MagazineInserted?.Invoke(contents);
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Take the magazine out, rounds and all (null if there is none). The chamber is untouched: a chambered round
    /// stays chambered and the gun can still fire it.
    /// </summary>
    public MagazineContents EjectMagazine(out AttachmentData magazineType, bool animate = true)
    {
        magazineType = null;
        if (magazine == null) return null;
        MagazineUnlocked = false;

        if (TryGetMagazineSlot(out AttachmentSlot slot) && slot.Installed != null)
        {
            syncingAttachments = true;
            magazineType = attachments.Remove(slot, animate);
            syncingAttachments = false;
        }

        MagazineContents gone = magazine;
        magazine = null;
        MagazineEjected?.Invoke(gone);
        StateChanged?.Invoke();
        return gone;
    }

    bool TryGetMagazineSlot(out AttachmentSlot slot)
    {
        slot = null;
        if (attachments == null || string.IsNullOrEmpty(magazineSlotId)) return false;
        slot = attachments.GetSlot(magazineSlotId);
        return slot != null && slot.category == AttachmentCategory.Magazine;
    }

    /// <summary>The magazine attachment was installed or removed by something else (the inspect picker, a loadout restore).</summary>
    void OnAttachmentChanged(AttachmentSlot slot, AttachmentData previous, AttachmentData now)
    {
        if (syncingAttachments || slot == null) return;
        if (slot.slotID != magazineSlotId || slot.category != AttachmentCategory.Magazine) return;
        MagazineUnlocked = false;

        if (now != null)
        {
            // The magazine model carries its own rounds (MagazineItem): the gun just reads that same object.
            MagazineItem item = slot.Instance != null ? slot.Instance.GetComponent<MagazineItem>() : null;
            if (item != null && item.contents != null)
            {
                MagazineContents incoming = item.contents;
                if (magazine != null && magazine != incoming) MagazineEjected?.Invoke(magazine); // replaced by another magazine
                magazine = incoming;
                magazine.magazineId = now.attachmentID;
                if (!string.IsNullOrEmpty(now.magazineCaliber)) magazine.caliber = now.magazineCaliber;
                if (magazine.capacity <= 0) magazine.capacity = Mathf.Max(1, fallbackMagazineCapacity);

                // The weapon's own Start State beats the magazine's default fill for the magazine it starts with.
                if (startFillPending && startLoaded && startAmmo != null)
                {
                    magazine.rounds.Clear();
                    magazine.Fill(startAmmo, startMagazineRounds);
                }
                startFillPending = false;
                StateChanged?.Invoke();
                return;
            }

            int capacity = now.stats.magazineCapacity > 0 ? now.stats.magazineCapacity : Mathf.Max(1, fallbackMagazineCapacity);
            if (magazine == null)
            {
                magazine = new MagazineContents { magazineId = now.attachmentID, caliber = now.magazineCaliber, capacity = capacity };

                // A new magazine arrives with rounds: the weapon's Start State first, otherwise what the magazine asset says.
                if (startFillPending && startLoaded && startAmmo != null)
                    magazine.Fill(startAmmo, startMagazineRounds);
                else if (now.loadedAmmo != null)
                    magazine.Fill(now.loadedAmmo, now.loadedRounds);
                startFillPending = false;
            }
            else
            {
                // Rounds are already here (a save restored them first): keep them, just learn which magazine this is.
                magazine.magazineId = now.attachmentID;
                if (!string.IsNullOrEmpty(now.magazineCaliber)) magazine.caliber = now.magazineCaliber;
                magazine.capacity = capacity;
            }
        }
        else if (magazine != null)
        {
            MagazineContents gone = magazine;
            magazine = null;
            MagazineEjected?.Invoke(gone);
        }
        StateChanged?.Invoke();
    }

    // ================================================================== visuals & sound

    void UpdateBolt(float dt)
    {
        if (boltCarrier == null) return;

        dt = Mathf.Min(dt, 0.033f);

        // The player is dragging the charging handle: the carrier follows the hand (smoothed), no spring.
        if (BoltDragging)
        {
            if (HandleActive)
            {
                // ONE smoothed value for the hand; the carrier is locked to it, so the two can never drift apart however fast you pull.
                handlePos = Mathf.SmoothDamp(handlePos, boltDragTarget * HandleTravel, ref handleVel, 0.03f, Mathf.Infinity, dt);
                boltPos = Mathf.Max(HandleFraction, boltDragFloor) * boltTravel;
                boltVel = 0f;
            }
            else
            {
                float carrierTarget = Mathf.Max(boltDragTarget, boltDragFloor);
                boltPos = Mathf.SmoothDamp(boltPos, carrierTarget * boltTravel, ref boltVel, 0.03f, Mathf.Infinity, dt);
            }
            ApplyBoltPose(dt);
            return;
        }

        bool holdBack = BoltOpen || Time.time < rackHoldUntil;
        float target = holdBack ? boltTravel : 0f;

        int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.002f));
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            float accel = -boltStiffness * (boltPos - target) - boltDamping * boltVel;
            boltVel += accel * h;
            boltPos += boltVel * h;

            // Hard stops: the carrier hits the buffer / slams into battery and bounces a little.
            if (boltPos < 0f) { boltPos = 0f; boltVel = Mathf.Max(0f, -boltVel * 0.2f); }
            else if (boltPos > boltTravel) { boltPos = boltTravel; boltVel = Mathf.Min(0f, -boltVel * 0.2f); }
        }

        ApplyBoltPose(dt);
    }

    /// <summary>Puts the bolt carrier and the charging handle where the simulation says they are.</summary>
    void ApplyBoltPose(float dt)
    {
        Vector3 axis = boltAxis.sqrMagnitude > 0.0001f ? boltAxis.normalized : Vector3.back;

        // Handle first: the carrier may be its child, and the carrier is then placed in world space so it never double-moves.
        if (HandleActive)
        {
            float travel = HandleTravel;
            if (!BoltDragging)   // (while dragging, UpdateBolt has already placed the handle)
            {
                if (chargingHandleReciprocates || handleCoupled)
                {
                    // Fixed to the carrier (AK), or just let go and riding the carrier home: exactly the same position, no lag.
                    handlePos = BoltFraction * travel;
                    handleVel = 0f;
                    if (!chargingHandleReciprocates && BoltFraction < 0.02f) handleCoupled = false;
                }
                else
                {
                    // The bolt stayed locked open: the handle returns forward on its own.
                    handlePos = Mathf.SmoothDamp(handlePos, 0f, ref handleVel, Mathf.Max(0.01f, chargingHandleReturnTime), Mathf.Infinity, dt);
                }
            }

            chargingHandle.position = transform.TransformPoint(handleRestRoot) + WorldOffset(handlePos);
        }

        boltCarrier.position = transform.TransformPoint(boltRestRoot) + WorldOffset(boltPos);
    }

    /// <summary>
    /// The space Bolt Axis is written in: the charging handle's PARENT when there is a separate handle, otherwise the bolt carrier's
    /// parent. The handle and the carrier both use this one frame, so they always slide the same way.
    /// </summary>
    Transform AxisFrame => HandleActive ? chargingHandle.parent : (boltCarrier != null ? boltCarrier.parent : null);

    /// <summary>
    /// A slide of `amount` along Bolt Axis as a world-space displacement. The DISTANCE is measured in the weapon's own units, so
    /// import scales on the parts (a handle at scale 100 that holds the carrier, say) can't make one part travel further than another.
    /// </summary>
    Vector3 WorldOffset(float amount)
    {
        Transform parent = AxisFrame;
        Vector3 a = boltAxis.sqrMagnitude > 0.0001f ? boltAxis.normalized : Vector3.back;
        Vector3 dirWorld = parent != null ? parent.TransformDirection(a) : transform.TransformDirection(a);
        Vector3 dirRoot = transform.InverseTransformDirection(dirWorld.normalized);
        return transform.TransformVector(dirRoot * amount);
    }

    /// <summary>True while the bolt catch is holding the bolt open (the lever is up).</summary>
    public bool BoltCatchEngaged
    {
        get
        {
            if (!BoltOpen && !boltDragLock) return false;
            if (boltCarrier == null || boltTravel <= 0.0001f) return true;
            return boltPos >= boltTravel * boltCatchEngageAt; // it can only catch a bolt that has come all the way back
        }
    }

    /// <summary>Moves the bolt catch lever to engaged while the bolt is locked open, and back when it is released or closed.</summary>
    void UpdateBoltCatch(float dt) => boltCatchLever.Tick(transform, BoltCatchEngaged, dt);

    /// <summary>The position (degrees or metres) the selector switch sits at for a fire mode.</summary>
    public float SelectorAngleFor(FireMode mode)
    {
        switch (mode)
        {
            case FireMode.Semi: return selectorAngleSemi;
            case FireMode.Burst: return selectorAngleBurst;
            case FireMode.Auto: return selectorAngleAuto;
            default: return selectorAngleSafe;
        }
    }

    /// <summary>Moves the selector switch model to the position of the current mode.</summary>
    void UpdateSelector(float dt) => selectorLever.TickTo(transform, SelectorAngleFor(Mode), dt);

    /// <summary>Carries old weapons' single-transform bolt catch / selector settings over into the new parts (runs once per weapon).</summary>
    void MigrateLegacyParts()
    {
        if (boltCatch != null && boltCatchLever.part == null)
        {
            boltCatchLever.Set(boltCatch, PartMotion.Kind.Rotate, boltCatchAxis, boltCatchOpenAngle, boltCatchStiffness, boltCatchDampingRatio);
            boltCatch = null;
        }
        if (selectorSwitch != null && selectorLever.part == null)
        {
            selectorLever.Set(selectorSwitch, PartMotion.Kind.Rotate, selectorAxis, 0f, selectorLever.stiffness, selectorLever.dampingRatio);
            selectorSwitch = null;
        }
    }

    void SpawnEjected(AmmoData ammo, bool spent)
    {
        GameObject prefab = spent ? ammo.casingPrefab : ammo.cartridgePrefab;
        if (prefab == null) return;

        Transform port = ejectionPort != null ? ejectionPort : transform;
        GameObject go = Instantiate(prefab, port.position,
            port.rotation * Quaternion.Euler(UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(0f, 360f), 0f));

        if (OwnerColliders != null)
        {
            foreach (Collider mine in go.GetComponentsInChildren<Collider>())
                foreach (Collider other in OwnerColliders)
                    if (other != null) Physics.IgnoreCollision(mine, other, true);
        }

        Rigidbody body = go.GetComponent<Rigidbody>();
        if (body != null)
        {
            Vector3 push = port.TransformDirection(ejectionDirection.normalized)
                           * (ejectionSpeed * UnityEngine.Random.Range(0.8f, 1.2f));
            Pickupable.SetVelocity(body, OwnerVelocity + push);
            body.angularVelocity = UnityEngine.Random.onUnitSphere * ejectionSpin;
        }
        Destroy(go, casingLifetime);
    }

    void PlayClip(AudioClip clip)
    {
        if (clip != null) AudioSource.PlayClipAtPoint(clip, MuzzleTransform.position, soundVolume);
    }

    // ================================================================== readouts

    /// <summary>Short text for a HUD or inspect screen: "Live 5.56x45", "Spent case", "Empty".</summary>
    public string ChamberDescription
    {
        get
        {
            if (Chamber == ChamberState.Empty) return "Empty";
            string name = ChamberedAmmo != null ? ChamberedAmmo.displayName : "?";
            return Chamber == ChamberState.Live ? "Live (" + name + ")" : "Spent case (" + name + ")";
        }
    }

    // ================================================================== saving

    public string CaptureState()
    {
        var state = new State
        {
            version = 1,
            chamber = (int)Chamber,
            chamberAmmoId = ChamberedAmmo != null ? ChamberedAmmo.ammoId : "",
            boltOpen = BoltOpen,
            hammerCocked = HammerCocked,
            dustCoverOpen = DustCoverOpen,
            mode = (int)Mode,
            hasMagazine = magazine != null,
            magazine = magazine ?? new MagazineContents()
        };
        return JsonUtility.ToJson(state);
    }

    public void RestoreState(string json)
    {
        if (string.IsNullOrEmpty(json)) return;

        State state = null;
        try { state = JsonUtility.FromJson<State>(json); }
        catch (ArgumentException) { }
        if (state == null || state.version < 1) return;

        initialized = true; // a restored state replaces the Start State

        ChamberedAmmo = Resolve(state.chamberAmmoId);
        Chamber = ChamberedAmmo != null ? (ChamberState)Mathf.Clamp(state.chamber, 0, 2) : ChamberState.Empty;
        BoltOpen = state.boltOpen;
        HammerCocked = state.hammerCocked;
        DustCoverOpen = state.dustCoverOpen;
        Mode = ValidMode((FireMode)Mathf.Clamp(state.mode, 0, 3));
        magazine = state.hasMagazine ? state.magazine : null;
        // The magazine model on the gun carries the restored rounds too.
        if (magazine != null && TryGetMagazineSlot(out AttachmentSlot restoredSlot) && restoredSlot.Instance != null)
        {
            MagazineItem item = restoredSlot.Instance.GetComponent<MagazineItem>();
            if (item != null) item.contents = magazine;
        }

        boltPos = BoltOpen ? boltTravel : 0f;
        boltVel = 0f;
        selectorLever.Snap();   // a loaded save shows these parts in the right place immediately
        boltCatchLever.Snap();
        dustCover.Snap();
        prevBoltFraction = BoltFraction;
        StateChanged?.Invoke();
    }
}