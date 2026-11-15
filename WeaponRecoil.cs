using UnityEngine;

/// <summary>
/// Recoil as physics, not as a recoil pattern.
///
/// When a round fires, the gun gets the momentum impulse J (N*s) that Firearm computed from the bullet and the powder
/// gas. The gun and the part of your body that moves with it behave as one rigid body hanging on springs at the shoulder:
///
///   Straight back:  v0 = J / (gun mass + arm mass).  A spring-damper (your shoulder and arms) pushes it forward again.
///   Muzzle climb:   the bore sits a height h above the shoulder contact, so the impulse is also a torque, J * h.
///                   Angular velocity w0 = J * h / I, where I = mass * (distance to centre of mass)^2 + a little.
///                   A spring-damper (your wrists and arms) pulls the muzzle back down.
///
/// Because it is a real simulation, everything follows without hand-tuning:
///   - a heavier gun (or a heavy optic or suppressor) kicks less and climbs slower
///   - a more powerful cartridge, a shorter barrel or a lighter bullet changes the impulse
///   - a stock lower in line with the bore (small h) climbs less
///   - full auto climbs because the next round fires before the muzzle has come back down
///   - crouching and aiming brace you (stiffer springs); being tired or moving loosens your grip
///
/// Where it shows up:
///   - the gun model rotates and slides about the shoulder (through HeldItemSlot.SetRecoilPose)
///   - the camera follows part of the gun's angle (MouseLook.ViewKickEuler): more while aiming, less from the hip
///   - the bullet leaves along the muzzle's REAL direction, so climb during a burst really moves the shots
///
/// Put this on the Player root next to HeldItemSlot. Nothing to call: FirearmController hands it each shot.
/// </summary>
[DefaultExecutionOrder(-10)] // before MouseLook (so the kick is in this frame's camera) and HeldItemSlot (50)
public class WeaponRecoil : MonoBehaviour
{
    [Header("References (found automatically if empty)")]
    public MouseLook look;
    public PlayerMotor motor;
    public PlayerStamina stamina;
    public HeldItemSlot slot;
    public WeaponADS ads;

    [Header("Shooter Body")]
    [Tooltip("How hard your shoulders and wrists resist the muzzle rising, N*m per radian. Higher = less climb, faster recovery.")]
    public float rotationalStiffness = 260f;
    [Tooltip("N*m*s per radian. Around 2 * sqrt(stiffness * inertia) is critically damped (about 22 for a rifle).")]
    public float rotationalDamping = 15f;
    [Tooltip("How hard your shoulder resists being pushed back, N/m.")]
    public float linearStiffness = 3500f;
    [Tooltip("N*s/m.")]
    public float linearDamping = 260f;
    [Tooltip("Mass (kg) of the arms and shoulder that recoil together with the gun. More = softer kick.")]
    public float bodyCouplingKg = 4f;

    [Header("Stance (multiplies the spring strength)")]
    [Tooltip("Crouching braces you.")]
    [Range(0.7f, 1.6f)] public float crouchGain = 1.2f;
    [Tooltip("Shouldering the gun tight to aim.")]
    [Range(0.7f, 1.6f)] public float aimGain = 1.15f;
    [Tooltip("Strength lost when completely out of stamina.")]
    [Range(0f, 0.8f)] public float fatigueLoss = 0.4f;
    [Tooltip("Strength lost when moving at walking speed.")]
    [Range(0f, 0.6f)] public float movingLoss = 0.2f;

    [Header("Shot-to-Shot Variation")]
    [Tooltip("Random variation of the climb, yaw and roll, as a fraction. Real shooters never repeat exactly.")]
    [Range(0f, 1f)] public float variation = 1f;

    [Header("View")]
    [Tooltip("How much of the gun's muzzle angle the CAMERA follows when firing from the hip (the rest moves the gun on screen).")]
    [Range(0f, 1f)] public float cameraShareHip = 0.35f;
    [Tooltip("Same while aiming down sights. 1 = the sights stay on screen and the whole view climbs.")]
    [Range(0f, 1f)] public float cameraShareAim = 0.7f;
    [Tooltip("Scales the whole recoil impulse. 1 = the physically computed value, <1 = gentler, >1 = harder. Affects bullets and visuals alike.")]
    [Min(0f)] public float recoilScale = 1f;
    [Tooltip("Safety limit on how far the muzzle can be thrown (degrees).")]
    public float maxAngleDegrees = 20f;

    // ---- Public state ----
    /// <summary>How far the muzzle is currently raised above where you pointed it (degrees).</summary>
    public float MuzzleRiseDegrees => ang.x * Mathf.Rad2Deg;
    /// <summary>How far the gun is currently pushed back toward you (metres).</summary>
    public float KickbackMeters => lin;

    // x = pitch (muzzle up is +), y = yaw (muzzle right is +), z = roll. Radians.
    Vector3 ang, angVel;
    float lin, linVel;

    Pickupable cachedItem;
    Firearm weapon;
    Vector3 pivotInItem;

    void Awake()
    {
        if (motor == null) motor = GetComponent<PlayerMotor>();
        if (stamina == null) stamina = GetComponent<PlayerStamina>();
        if (slot == null) slot = GetComponent<HeldItemSlot>();
        if (ads == null) ads = GetComponent<WeaponADS>();
        if (look == null && slot != null) look = slot.look;
        if (look == null) look = GetComponentInChildren<MouseLook>();
    }

    void OnDisable()
    {
        ang = angVel = Vector3.zero;
        lin = linVel = 0f;
        if (look != null) look.ViewKickEuler = Vector3.zero;
        if (slot != null) slot.SetRecoilPose(Vector3.zero, Vector3.zero, Quaternion.identity);
    }

    /// <summary>Gives the gun the recoil impulse of one shot (N*s). Called by FirearmController.</summary>
    public void ApplyShot(float impulse)
    {
        if (weapon == null) return;

        weapon.GetRecoilGeometry(out float boreHeight, out float comDistance, out float lateral);
        float effectiveMass = weapon.TotalMassKg + bodyCouplingKg;
        float inertia = Inertia(effectiveMass, comDistance);
        float j = impulse * recoilScale;

        // Straight back: free-recoil velocity of the gun plus the arms that move with it.
        linVel += j / effectiveMass;

        // Twist: the push acts along the bore, a height above the shoulder, so it is also a torque.
        float v = variation;
        float climb = j * boreHeight * (1f + UnityEngine.Random.Range(-0.15f, 0.15f) * v);
        float side = j * (lateral + UnityEngine.Random.Range(-0.3f, 0.3f) * v * Mathf.Abs(boreHeight));
        float roll = j * Mathf.Abs(boreHeight) * UnityEngine.Random.Range(-0.12f, 0.12f) * v;
        angVel += new Vector3(climb, side, roll) / inertia;
    }

    void RefreshWeapon()
    {
        Pickupable current = slot != null ? slot.Current : null;
        if (current == cachedItem) return;

        cachedItem = current;
        weapon = current != null ? current.GetComponent<Firearm>() : null;
        pivotInItem = weapon != null ? weapon.RecoilPivotLocal : Vector3.zero;

        // A different gun starts calm.
        ang = angVel = Vector3.zero;
        lin = linVel = 0f;
    }

    static float Inertia(float effectiveMass, float comDistance)
        => effectiveMass * (comDistance * comDistance + 0.02f);

    float StanceStrength()
    {
        float g = 1f;
        if (motor != null)
        {
            if (motor.IsCrouching) g *= crouchGain;
            g *= 1f - movingLoss * Mathf.Clamp01(motor.PlanarSpeed / Mathf.Max(0.1f, motor.walkSpeed));
        }
        if (ads != null) g *= Mathf.Lerp(1f, aimGain, ads.AimBlend);
        if (stamina != null) g *= 1f - fatigueLoss * stamina.Fatigue;
        return Mathf.Max(0.3f, g);
    }

    void LateUpdate()
    {
        if (look == null || slot == null) return;
        float rawDt = Time.deltaTime;
        if (rawDt <= 0f) return;

        RefreshWeapon();

        float comDistance = 0.3f;
        float mass = 3.5f;
        if (weapon != null)
        {
            weapon.GetRecoilGeometry(out _, out comDistance, out _);
            mass = weapon.TotalMassKg;
        }
        float effectiveMass = mass + bodyCouplingKg;
        float inertia = Inertia(effectiveMass, comDistance);
        float strength = StanceStrength();

        float kRot = rotationalStiffness * strength, cRot = rotationalDamping * strength;
        float kLin = linearStiffness * strength, cLin = linearDamping * strength;

        // Small fixed substeps keep the stiff springs stable at any frame rate.
        float dt = Mathf.Min(rawDt, 0.05f);
        int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.004f));
        float h = dt / steps;
        float limit = maxAngleDegrees * Mathf.Deg2Rad;

        for (int i = 0; i < steps; i++)
        {
            angVel += (-kRot * ang - cRot * angVel) / inertia * h;
            ang += angVel * h;
            ang.x = Mathf.Clamp(ang.x, -limit, limit);
            ang.y = Mathf.Clamp(ang.y, -limit, limit);
            ang.z = Mathf.Clamp(ang.z, -limit, limit);

            linVel += (-kLin * lin - cLin * linVel) / effectiveMass * h;
            lin += linVel * h;
            lin = Mathf.Clamp(lin, -0.02f, 0.15f);
        }

        // The camera follows a share of the muzzle's angle; the gun on screen keeps the rest.
        // (Together they add up to the physical angle, which is where the bullet really goes.)
        float aim = ads != null ? ads.AimBlend : 0f;
        float share = Mathf.Lerp(cameraShareHip, cameraShareAim, aim);

        // Unity camera pitch is positive downward, so a rising muzzle is a negative pitch.
        look.ViewKickEuler = new Vector3(-ang.x * share, ang.y * share, ang.z * share * 0.5f) * Mathf.Rad2Deg;

        float gunShare = 1f - share;
        Quaternion gunRot = Quaternion.Euler(
            -ang.x * gunShare * Mathf.Rad2Deg,
            ang.y * gunShare * Mathf.Rad2Deg,
            -ang.z * gunShare * Mathf.Rad2Deg);

        slot.SetRecoilPose(pivotInItem, new Vector3(0f, 0f, -lin), gunRot);
    }
}
