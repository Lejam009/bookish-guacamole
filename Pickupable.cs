using System.Collections;
using UnityEngine;

/// <summary>
/// Put this on the WORLD prefab of any item (needs a Rigidbody and at least one Collider).
/// Loose, it's a normal physics object you can kick around. Held, it's switched to kinematic with its
/// colliders off, and HeldItemSlot drives its transform.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Pickupable : MonoBehaviour
{
    public ItemData data;

    public bool IsHeld { get; private set; }
    public Rigidbody Body => rb;
    public float MassKg => data != null ? data.massKg : rb.mass;
    public string DisplayName => data != null ? data.displayName : name;

    Rigidbody rb;
    Collider[] colliders;
    bool[] colliderWasEnabled;
    RigidbodyInterpolation originalInterpolation;
    CollisionDetectionMode originalDetection;
    IItemStateful stateful;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        colliders = GetComponentsInChildren<Collider>(true);
        colliderWasEnabled = new bool[colliders.Length];
        stateful = GetComponent<IItemStateful>();
        originalInterpolation = rb.interpolation;
        originalDetection = rb.collisionDetectionMode;
        if (data != null) rb.mass = data.massKg;
    }

    /// <summary>Called by HeldItemSlot. Held = kinematic and non-colliding.</summary>
    public void SetHeld(bool held)
    {
        if (held == IsHeld) return;
        IsHeld = held;

        if (held)
        {
            SetVelocity(rb, Vector3.zero);
            rb.angularVelocity = Vector3.zero;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.isKinematic = true;

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null) continue;
                colliderWasEnabled[i] = colliders[i].enabled;
                colliders[i].enabled = false;
            }
        }
        else
        {
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null) colliders[i].enabled = colliderWasEnabled[i];

            rb.isKinematic = false;
            rb.interpolation = originalInterpolation;
            rb.collisionDetectionMode = originalDetection;
            rb.WakeUp();
        }
    }

    /// <summary>Stops a just-dropped item from slamming into the person who dropped it.</summary>
    public void IgnoreCollisionsTemporarily(Collider[] others, float seconds)
    {
        if (others == null || others.Length == 0) return;
        StartCoroutine(IgnoreRoutine(others, seconds));
    }

    IEnumerator IgnoreRoutine(Collider[] others, float seconds)
    {
        SetIgnore(others, true);
        yield return new WaitForSeconds(seconds);
        SetIgnore(others, false);
    }

    void SetIgnore(Collider[] others, bool ignore)
    {
        foreach (Collider mine in colliders)
        {
            if (mine == null) continue;
            foreach (Collider other in others)
                if (other != null) Physics.IgnoreCollision(mine, other, ignore);
        }
    }

    public string CaptureState() => stateful != null ? stateful.CaptureState() : null;

    public void RestoreState(string state)
    {
        if (stateful != null && !string.IsNullOrEmpty(state)) stateful.RestoreState(state);
    }

    // ---- Unity 6 renamed Rigidbody.velocity; these keep every script version-safe ----
    public static Vector3 GetVelocity(Rigidbody body)
    {
#if UNITY_6000_0_OR_NEWER
        return body.linearVelocity;
#else
        return body.velocity;
#endif
    }

    public static void SetVelocity(Rigidbody body, Vector3 v)
    {
#if UNITY_6000_0_OR_NEWER
        body.linearVelocity = v;
#else
        body.velocity = v;
#endif
    }
}
