using System;
using UnityEngine;

/// <summary>
/// Trigger volume that marks an area as safe (base, hideout, workbench room...).
/// While the player stands in ANY SafeZone, SafeZone.PlayerInSafeZone is true.
/// Used to gate weapon rotation and attachment changes in inspect mode.
/// </summary>
[RequireComponent(typeof(Collider))]
public class SafeZone : MonoBehaviour
{
    static int occupiedZones;

    public static bool PlayerInSafeZone => occupiedZones > 0;
    public static event Action<bool> StateChanged;

    bool playerInside;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        occupiedZones = 0;
        StateChanged = null;
    }

    void Reset() => GetComponent<Collider>().isTrigger = true;

    void OnTriggerEnter(Collider other)
    {
        if (playerInside || other.GetComponentInParent<PlayerMotor>() == null) return;
        SetInside(true);
    }

    void OnTriggerExit(Collider other)
    {
        if (!playerInside || other.GetComponentInParent<PlayerMotor>() == null) return;
        SetInside(false);
    }

    void OnDisable()
    {
        if (playerInside) SetInside(false);
    }

    void SetInside(bool inside)
    {
        bool before = PlayerInSafeZone;
        playerInside = inside;
        occupiedZones = Mathf.Max(0, occupiedZones + (inside ? 1 : -1));
        if (before != PlayerInSafeZone) StateChanged?.Invoke(PlayerInSafeZone);
    }
}
