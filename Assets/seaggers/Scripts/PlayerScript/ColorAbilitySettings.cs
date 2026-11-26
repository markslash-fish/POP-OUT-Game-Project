using UnityEngine;

/// <summary>
/// All color-effect numbers from the Game Design docx in ONE place.
/// Optional: Right-click in Project > Create > Pop Out > Color Ability Settings, then assign it to ColorStackSystem.
/// If you assign nothing, the defaults below are used.
/// </summary>
[CreateAssetMenu(menuName = "Pop Out/Color Ability Settings", fileName = "ColorAbilitySettings")]
public class ColorAbilitySettings : ScriptableObject
{
    [Header("Yellow - Stun (immobilize + silence)")]
    public float stunDuration = 1f;

    [Header("Blue - Slow")]
    [Range(0f, 1f)] public float slowSpeedMultiplier = 0.5f;   // -50% movement speed
    public float slowDuration = 3f;

    [Header("Green - Lifesteal")]
    [Range(0f, 1f)] public float lifestealPercentOfCurrentHealth = 0.15f;   // docx says the TARGET'S CURRENT health (Open Question 7)

    [Header("Red - Burn (damage over time)")]
    [Range(0f, 1f)] public float burnPercentOfMaxPerTick = 0.05f;   // 5% per tick
    public float burnTickInterval = 1f;
    public float burnDuration = 3f;                                  // 3 ticks = 15% total

    [Header("Pink - Disarm")]
    public float disarmDuration = 2f;

    [Header("Purple - Bonus burst")]
    [Range(0f, 1f)] public float purpleBurstPercentOfMax = 0.15f;

    [Header("Orange - Status immunity for the user")]
    public float orangeImmunityDuration = 3f;
}