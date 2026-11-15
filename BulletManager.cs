using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>What a bullet tells whatever it hit.</summary>
public struct BulletHitInfo
{
    public RaycastHit hit;
    /// <summary>Bullet velocity at the moment of impact (m/s).</summary>
    public Vector3 velocity;
    public float massKg;
    public AmmoData ammo;

    public float Speed => velocity.magnitude;
    /// <summary>Kinetic energy in joules. A 5.56 M855 leaves a rifle with about 1700 J.</summary>
    public float EnergyJoules => 0.5f * massKg * velocity.sqrMagnitude;
    public Vector3 Momentum => velocity * massKg;
}

/// <summary>
/// Put on anything a bullet can hit that should react (a health component, a target, a window...).
/// BulletManager finds it with GetComponentInParent on the collider that was hit.
/// </summary>
public interface IShootable
{
    void OnShot(BulletHitInfo info);
}

/// <summary>
/// Flies every bullet in the game. Nothing to set up: Firearm creates the manager the first time it fires.
///
/// Bullets are real projectiles, not instant rays: they lose speed to quadratic air drag, fall under gravity,
/// and take time to arrive. Each step is a raycast along the path, so even a 900 m/s bullet cannot skip a thin wall.
/// On impact the bullet's momentum is pushed into the rigidbody it hits (a crate slides, a barrel tips),
/// IShootable.OnShot is called on the target, and the static BulletHit event fires (hook damage, decals, sounds).
///
/// Not simulated yet: penetration, ricochet, supersonic drag changes, wind.
/// </summary>
[DefaultExecutionOrder(100)]
public class BulletManager : MonoBehaviour
{
    public static event Action<BulletHitInfo> BulletHit;

    [Header("World")]
    [Tooltip("kg/m^3. Sea level 1.225; thinner at altitude.")]
    public float airDensity = 1.225f;
    public float maxFlightSeconds = 5f;
    public float maxRangeMeters = 2000f;
    [Tooltip("Longest time slice per raycast. Smaller = more accurate drag and drop, slightly more cost.")]
    public float maxStepSeconds = 1f / 120f;
    [Tooltip("Draw each bullet's path in the Scene view (Gizmos must be on in the Game view).")]
    public bool drawDebugTracers = false;

    struct Bullet
    {
        public Vector3 position, velocity, origin;
        public AmmoData ammo;
        public float massKg, dragPerMeter, age;
        public int hitMask, spawnFrame;
    }

    static BulletManager instance;
    readonly List<Bullet> bullets = new List<Bullet>(64);

    public static BulletManager Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("BulletManager");
                instance = go.AddComponent<BulletManager>();
            }
            return instance;
        }
    }

    public const float DefaultAirDensity = 1.225f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        BulletHit = null;
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    public int ActiveBullets => bullets.Count;

    /// <summary>Launch a bullet. It starts moving next frame so the shot never travels before the gun has finished posing.</summary>
    public void Fire(Vector3 position, Vector3 velocity, AmmoData ammo, int hitMask)
    {
        if (ammo == null) return;
        bullets.Add(new Bullet
        {
            position = position,
            velocity = velocity,
            origin = position,
            ammo = ammo,
            massKg = ammo.BulletMassKg,
            dragPerMeter = ammo.DragPerMeter(airDensity),
            age = 0f,
            hitMask = hitMask,
            spawnFrame = Time.frameCount
        });
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Walk backwards so a finished bullet can be swapped out with the last one (already processed).
        for (int i = bullets.Count - 1; i >= 0; i--)
        {
            Bullet b = bullets[i];
            if (b.spawnFrame == Time.frameCount) continue;

            if (Advance(ref b, dt))
            {
                bullets[i] = b;
            }
            else
            {
                bullets[i] = bullets[bullets.Count - 1];
                bullets.RemoveAt(bullets.Count - 1);
            }
        }
    }

    /// <summary>Moves a bullet forward by dt. Returns false when it has hit something or flown out of range.</summary>
    bool Advance(ref Bullet b, float dt)
    {
        float remaining = dt;
        float stepMax = Mathf.Max(0.0005f, maxStepSeconds);

        while (remaining > 1e-6f)
        {
            float step = Mathf.Min(remaining, stepMax);
            remaining -= step;

            // Quadratic drag has an exact solution along a straight line: v(t) = v0 / (1 + k v0 t)
            float speed = b.velocity.magnitude;
            Vector3 next = speed > 1e-4f
                ? b.velocity * (1f / (1f + b.dragPerMeter * speed * step))
                : b.velocity;
            next += Physics.gravity * step;

            Vector3 move = (b.velocity + next) * (0.5f * step);
            float distance = move.magnitude;

            if (distance > 1e-5f &&
                Physics.Raycast(b.position, move / distance, out RaycastHit hit, distance, b.hitMask,
                    QueryTriggerInteraction.Ignore))
            {
                Vector3 impactVelocity = Vector3.Lerp(b.velocity, next, hit.distance / distance);
                Impact(hit, impactVelocity, b);
                return false;
            }

            if (drawDebugTracers) Debug.DrawLine(b.position, b.position + move, Color.yellow, 1f);

            b.position += move;
            b.velocity = next;
            b.age += step;

            if (b.age > maxFlightSeconds) return false;
            if ((b.position - b.origin).sqrMagnitude > maxRangeMeters * maxRangeMeters) return false;
        }
        return true;
    }

    void Impact(RaycastHit hit, Vector3 velocity, Bullet b)
    {
        var info = new BulletHitInfo { hit = hit, velocity = velocity, massKg = b.massKg, ammo = b.ammo };

        // Momentum conservation: the bullet's momentum goes into whatever it hit.
        Rigidbody body = hit.rigidbody;
        if (body != null && !body.isKinematic)
            body.AddForceAtPosition(info.Momentum * b.ammo.impactMomentumTransfer, hit.point, ForceMode.Impulse);

        IShootable target = hit.collider.GetComponentInParent<IShootable>();
        if (target != null) target.OnShot(info);

        BulletHit?.Invoke(info);
    }

    // ================================================================== zeroing

    /// <summary>
    /// How far (radians) to raise the barrel so the bullet's path crosses the line of sight at zeroDistance.
    /// sightHeight = how far the sights sit above the bore. Uses the same drag and gravity as the live bullets,
    /// so changing ammo, barrel length or muzzle velocity changes the elevation (and therefore the drop) correctly.
    /// </summary>
    public static float SolveZeroElevation(AmmoData ammo, float muzzleVelocity, float sightHeight, float zeroDistance)
    {
        if (ammo == null || sightHeight <= 0.0005f || zeroDistance <= 1f) return 0f;

        float lo = 0f, hi = 0.12f;
        if (HeightAtRange(ammo, muzzleVelocity, hi, zeroDistance) < sightHeight) return hi;

        for (int i = 0; i < 18; i++)
        {
            float mid = 0.5f * (lo + hi);
            if (HeightAtRange(ammo, muzzleVelocity, mid, zeroDistance) < sightHeight) lo = mid; else hi = mid;
        }
        return 0.5f * (lo + hi);
    }

    /// <summary>Height above the bore (m) of a bullet fired at the given angle when it passes 'range' metres downrange.</summary>
    static float HeightAtRange(AmmoData ammo, float speed, float angle, float range)
    {
        float k = ammo.DragPerMeter(DefaultAirDensity);
        float g = Mathf.Abs(Physics.gravity.y);
        float x = 0f, y = 0f;
        float vx = speed * Mathf.Cos(angle), vy = speed * Mathf.Sin(angle);
        const float dt = 0.001f;

        for (int i = 0; i < 6000; i++)
        {
            float v = Mathf.Sqrt(vx * vx + vy * vy);
            float scale = 1f / (1f + k * v * dt);
            float nvx = vx * scale;
            float nvy = vy * scale - g * dt;
            float nx = x + 0.5f * (vx + nvx) * dt;
            float ny = y + 0.5f * (vy + nvy) * dt;

            if (nx >= range)
            {
                float t = (range - x) / Mathf.Max(1e-6f, nx - x);
                return Mathf.Lerp(y, ny, t);
            }
            x = nx; y = ny; vx = nvx; vy = nvy;
            if (vx < 1f) break;
        }
        return -1000f; // never got that far
    }
}
