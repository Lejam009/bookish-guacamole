using UnityEngine;

/// <summary>
/// Stamina pool for the player. Sprinting drains it, jumping costs a burst,
/// and it recovers after a short delay (faster when standing still than when walking).
/// Run it dry and you are "exhausted": no sprinting until you've recovered a chunk,
/// and recovery is slower. Carried weight makes everything cost more.
///
/// Put this on the same object as PlayerMotor.
/// </summary>
public class PlayerStamina : MonoBehaviour
{
    [Header("References")]
    public PlayerMotor motor;

    [Header("Capacity")]
    public float maxStamina = 100f;

    [Header("Drain (per second / per action)")]
    [Tooltip("6 = about 16 seconds of sprinting from full.")]
    public float sprintDrainPerSecond = 6f;
    [Tooltip("Extra drain per kg of carried load (0.015 = +45% at 30 kg).")]
    public float loadDrainPerKg = 0.015f;
    public float jumpCost = 6f;

    [Header("Recovery")]
    [Tooltip("Seconds after you stop exerting before stamina starts coming back.")]
    public float regenDelay = 1f;
    public float exhaustedRegenDelay = 2f;
    [Tooltip("Per second while standing still / crouching.")]
    public float restingRegen = 14f;
    [Tooltip("Per second while walking.")]
    public float walkingRegen = 7f;
    [Tooltip("Recovery speed multiplier while exhausted.")]
    [Range(0.2f, 1f)] public float exhaustedRegenMultiplier = 0.7f;
    [Tooltip("After running dry you need this much stamina before you can sprint again.")]
    public float exhaustedRecoverPoint = 25f;

    public float Current { get; private set; }
    /// <summary>0..1</summary>
    public float Value01 => Current / maxStamina;
    public bool IsExhausted { get; private set; }
    public bool CanSprint => !IsExhausted;
    /// <summary>0 = fresh, 1 = gassed. Drives breathing and other tiredness effects.</summary>
    public float Fatigue => Mathf.Pow(1f - Value01, 1.2f);

    float regenTimer;

    void Awake()
    {
        if (motor == null) motor = GetComponent<PlayerMotor>();
        Current = maxStamina;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        bool sprinting = motor.IsSprinting && motor.PlanarSpeed > motor.walkSpeed * 1.1f;

        if (sprinting)
        {
            Current -= sprintDrainPerSecond * LoadMultiplier() * dt;
            regenTimer = IsExhausted ? exhaustedRegenDelay : regenDelay;
        }
        else if (regenTimer > 0f)
        {
            regenTimer -= dt;
        }
        else
        {
            bool restingPose = motor.PlanarSpeed < 0.3f || motor.IsCrouching;
            float rate = restingPose ? restingRegen : walkingRegen;
            if (IsExhausted) rate *= exhaustedRegenMultiplier;
            Current += rate * dt;
        }

        Current = Mathf.Clamp(Current, 0f, maxStamina);
        UpdateExhaustion();
    }

    /// <summary>Called by the motor on a jump. Also usable for melee, vaulting, etc.</summary>
    public void Spend(float amount)
    {
        Current = Mathf.Max(0f, Current - amount * LoadMultiplier());
        regenTimer = IsExhausted ? exhaustedRegenDelay : regenDelay;
        UpdateExhaustion();
    }

    public void OnJump() => Spend(jumpCost);

    float LoadMultiplier() => 1f + motor.carriedLoadKg * loadDrainPerKg;

    void UpdateExhaustion()
    {
        if (!IsExhausted && Current <= 0f)
        {
            IsExhausted = true;
            regenTimer = exhaustedRegenDelay;
        }
        else if (IsExhausted && Current >= exhaustedRecoverPoint)
        {
            IsExhausted = false;
        }
    }
}