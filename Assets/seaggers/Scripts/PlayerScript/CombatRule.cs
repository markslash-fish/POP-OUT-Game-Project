using UnityEngine;

/// <summary>
/// Global combat switches. The future ArenaEventManager will change these for the Global events:
///  - "Oh Noir!"          -> StacksEnabled = false
///  - "Wash Out!"         -> DecayIntervalScale = 0.5   (see Open Question 6: the docx wording is unclear)
///  - "P-aint No Chill"   -> InstantProc = true
/// </summary>
public static class CombatRules
{
    public static bool StacksEnabled = true;       // false = hits give no stacks and no color effects
    public static bool InstantProc = false;        // true  = every balloon hit triggers the color effect right away
    public static float DecayIntervalScale = 1f;   // 0.5 = stacks decay twice as fast

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetAll()
    {
        StacksEnabled = true;
        InstantProc = false;
        DecayIntervalScale = 1f;
    }
}