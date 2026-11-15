using UnityEngine;

/// <summary>
/// One asset per cartridge type (5.56x45 M855, 9x19 FMJ, 7.62x39...). Create via
/// Assets > Create > Weapons > Ammo Data.
///
/// The numbers are real-world numbers on purpose: recoil comes from momentum (bullet + powder gas),
/// the bullet flies with real drag and gravity, and barrel length changes the muzzle velocity.
/// </summary>
[CreateAssetMenu(fileName = "NewAmmo", menuName = "Weapons/Ammo Data")]
public class AmmoData : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Unique + stable, written into saves (magazine contents, chambered round). Never rename it after shipping.")]
    public string ammoId;
    public string displayName;
    [Tooltip("A cartridge only chambers in a weapon / magazine with the same caliber text, e.g. 5.56x45, 9x19, 7.62x39.")]
    public string caliber = "5.56x45";

    [Header("Bullet")]
    [Tooltip("Bullet mass in grams. 5.56 M855 = 4.0, 9x19 FMJ = 7.45, 7.62x39 = 7.9, 12.7x99 = 42.")]
    public float bulletMassGrams = 4f;
    [Tooltip("Bullet diameter in millimetres (sets the frontal area for drag).")]
    public float bulletDiameterMm = 5.7f;
    [Tooltip("Drag coefficient. Pointed rifle bullets ~0.28-0.33, round-nose pistol/rifle bullets ~0.35-0.45.")]
    public float dragCoefficient = 0.3f;

    [Tooltip("Mass of the empty case + primer in grams (a 5.56 brass case is ~6 g, 9x19 ~3 g). Counts toward the weight of a loaded magazine.")]
    public float casingMassGrams = 6f;

    [Header("Powder & Velocity")]
    [Tooltip("Propellant mass in grams. The hot gas leaves the muzzle too and adds to recoil. 5.56 ~1.6, 7.62x39 ~1.6, 9x19 ~0.35.")]
    public float powderChargeGrams = 1.6f;
    [Tooltip("Muzzle velocity in m/s when fired from the reference barrel length below.")]
    public float muzzleVelocity = 920f;
    [Tooltip("Barrel length (m) the muzzle velocity above was measured from. 5.56 is usually quoted at 20 in = 0.508 m.")]
    public float referenceBarrelMeters = 0.508f;
    [Tooltip("Velocity gained (or lost) in m/s per metre of barrel length. Rifle powders ~250-300, pistol ~120-180.")]
    public float velocityPerBarrelMeter = 250f;

    [Header("Behaviour")]
    [Tooltip("Chance the primer fails to ignite (0.001 = 1 in 1000). The round stays in the chamber and the action must be cycled.")]
    [Range(0f, 0.05f)] public float misfireChance = 0.0005f;
    [Tooltip("Inherent accuracy of the load in minutes of angle (1 MOA = about 2.9 cm at 100 m). Combined with the weapon's own.")]
    public float dispersionMoa = 2f;
    [Tooltip("Fraction of the bullet's momentum pushed into a rigidbody it hits (1 = the bullet stops in it, which is also what keeps it from tunnelling through).")]
    [Range(0f, 2f)] public float impactMomentumTransfer = 1f;

    [Header("Ejected Visuals (optional)")]
    [Tooltip("Spent casing thrown out of the ejection port. Needs a Rigidbody + Collider to tumble and bounce.")]
    public GameObject casingPrefab;
    [Tooltip("Live round thrown out when you rack the action with a round in the chamber. Same idea as the casing.")]
    public GameObject cartridgePrefab;

    public float BulletMassKg => bulletMassGrams * 0.001f;
    public float PowderMassKg => powderChargeGrams * 0.001f;
    /// <summary>Whole live cartridge: bullet + powder + case.</summary>
    public float CartridgeMassKg => (bulletMassGrams + powderChargeGrams + casingMassGrams) * 0.001f;
    /// <summary>Spent case only (what is left in the chamber after firing).</summary>
    public float CasingMassKg => casingMassGrams * 0.001f;
    public float CrossSectionM2
    {
        get
        {
            float radius = bulletDiameterMm * 0.0005f;
            return Mathf.PI * radius * radius;
        }
    }

    /// <summary>
    /// Quadratic drag constant k in dv/dt = -k v^2, in 1/m: k = air density * Cd * area / (2 * mass).
    /// The speed after travelling x metres is v0 * exp(-k x).
    /// </summary>
    public float DragPerMeter(float airDensity = 1.225f)
        => 0.5f * airDensity * dragCoefficient * CrossSectionM2 / Mathf.Max(0.0001f, BulletMassKg);

    void OnValidate()
    {
        bulletMassGrams = Mathf.Max(0.1f, bulletMassGrams);
        bulletDiameterMm = Mathf.Max(1f, bulletDiameterMm);
        muzzleVelocity = Mathf.Max(50f, muzzleVelocity);
        if (string.IsNullOrEmpty(ammoId)) ammoId = name.ToLowerInvariant().Replace(' ', '_');
        if (string.IsNullOrEmpty(displayName)) displayName = name;
    }
}