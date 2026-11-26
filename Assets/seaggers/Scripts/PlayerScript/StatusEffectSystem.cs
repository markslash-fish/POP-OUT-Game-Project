using System;
using System.Text;
using UnityEngine;

public enum StatusType { Stun, Slow, Burn, Disarm, Lifesteal }   // Lifesteal is only used for immunity checks

/// <summary>
/// Put on EVERY player (same object as Health). Keeps track of the status effects on this player:
/// Stun, Slow, Burn, Disarm, and the Orange immunity.
/// Other scripts ask it questions (IsStunned, CanAttack, SpeedMultiplier...) - they never store status themselves.
/// </summary>
public class StatusEffectSystem : MonoBehaviour
{
    public Health health;                    // auto-found on the same object
    public bool logEvents = true;            // prints to the Console (handy while testing)

    const int TypeCount = 5;
    readonly float[] activeUntil = new float[TypeCount];
    readonly float[] immuneUntil = new float[TypeCount];

    float slowMultiplier = 1f;

    // Burn works with ticks (1 tick per second)
    Health burnSource;
    float burnPercentOfMax;
    float burnInterval = 1f;
    int burnTicksLeft;
    float nextBurnTick;

    int lastMask = -1;
    readonly StringBuilder sb = new StringBuilder();

    public event Action StatusChanged;       // HUD listens to this

    // ---------------------------------------------------------------- questions other scripts ask
    public bool IsStunned => IsActive(StatusType.Stun);
    public bool IsDisarmed => IsActive(StatusType.Disarm);
    public bool IsSlowed => IsActive(StatusType.Slow);
    public bool IsBurning => burnTicksLeft > 0;
    public bool CanAttack => !IsStunned && !IsDisarmed;                         // throw + sword
    public float SpeedMultiplier => IsSlowed ? slowMultiplier : 1f;

    public bool IsImmuneTo(StatusType type) { return Time.time < immuneUntil[(int)type]; }

    bool IsActive(StatusType type)
    {
        if (type == StatusType.Burn) return burnTicksLeft > 0;
        return Time.time < activeUntil[(int)type];
    }

    // ---------------------------------------------------------------- lifecycle
    void Awake()
    {
        if (health == null) health = GetComponent<Health>();
    }

    void OnEnable()
    {
        if (health != null) health.Died += OnDied;
    }

    void OnDisable()
    {
        if (health != null) health.Died -= OnDied;
    }

    void OnDied(Health victim, Health killer)
    {
        ClearAll();
    }

    void Update()
    {
        if (burnTicksLeft > 0 && Time.time >= nextBurnTick)
        {
            nextBurnTick += burnInterval;
            burnTicksLeft--;
            if (health != null && !health.IsDead)
                health.TakeDamage(health.maxHealth * burnPercentOfMax, burnSource, HitType.Other);   // Other = burn never creates stacks
        }

        CheckChanged();
    }

    // ---------------------------------------------------------------- applying effects
    /// <param name="magnitude">Slow: speed multiplier (0.5 = half speed). Burn: % of max HP per tick (0.05).</param>
    /// <returns>false if the effect was blocked (dead or immune).</returns>
    public bool Apply(StatusType type, float duration, Health source = null, float magnitude = 1f, float tickInterval = 1f)
    {
        if (health != null && health.IsDead) return false;
        if (IsImmuneTo(type)) return false;

        int i = (int)type;
        switch (type)
        {
            case StatusType.Burn:
                // PLACEHOLDER rule (not in docx): re-applying Burn restarts it, it does not stack
                burnSource = source;
                burnPercentOfMax = magnitude;
                burnInterval = Mathf.Max(0.1f, tickInterval);
                burnTicksLeft = Mathf.Max(1, Mathf.RoundToInt(duration / burnInterval));
                nextBurnTick = Time.time + burnInterval;
                break;

            case StatusType.Slow:
                slowMultiplier = IsSlowed ? Mathf.Min(slowMultiplier, magnitude) : magnitude;   // the strongest slow wins
                activeUntil[i] = Mathf.Max(activeUntil[i], Time.time + duration);
                break;

            default:
                // PLACEHOLDER rule (not in docx): re-applying refreshes to the longer remaining time
                activeUntil[i] = Mathf.Max(activeUntil[i], Time.time + duration);
                break;
        }

        if (logEvents && health != null) Debug.Log(health.playerName + " gets " + type + " (" + duration + "s)");
        CheckChanged();
        return true;
    }

    // ---------------------------------------------------------------- immunity (Orange)
    public void AddImmunity(StatusType type, float duration)
    {
        int i = (int)type;
        immuneUntil[i] = Mathf.Max(immuneUntil[i], Time.time + duration);
        CheckChanged();
    }

    /// <summary>Orange ability: immune to Stun, Slow, Burn, Disarm and Lifesteal. Does NOT remove effects already on us.</summary>
    public void AddNegativeImmunity(float duration)
    {
        for (int i = 0; i < TypeCount; i++)
            immuneUntil[i] = Mathf.Max(immuneUntil[i], Time.time + duration);

        if (logEvents && health != null) Debug.Log(health.playerName + " is immune to status effects (" + duration + "s)");
        CheckChanged();
    }

    // ---------------------------------------------------------------- removing effects
    /// <summary>Used later by "Wash Out!" (cleanses Burn, Slow, Disarm).</summary>
    public void Cleanse(params StatusType[] types)
    {
        foreach (var t in types)
        {
            if (t == StatusType.Burn) burnTicksLeft = 0;
            else activeUntil[(int)t] = 0f;
        }
        if (!IsSlowed) slowMultiplier = 1f;
        CheckChanged();
    }

    public void ClearAll()
    {
        for (int i = 0; i < TypeCount; i++) { activeUntil[i] = 0f; immuneUntil[i] = 0f; }
        burnTicksLeft = 0;
        slowMultiplier = 1f;
        CheckChanged();
    }

    // ---------------------------------------------------------------- HUD text
    public string DescribeActive()
    {
        sb.Clear();
        if (IsStunned) sb.Append("STUN  ");
        if (IsSlowed) sb.Append("SLOW  ");
        if (IsBurning) sb.Append("BURN  ");
        if (IsDisarmed) sb.Append("DISARM  ");
        if (HasAnyImmunity()) sb.Append("IMMUNE  ");
        return sb.ToString();
    }

    bool HasAnyImmunity()
    {
        for (int i = 0; i < TypeCount; i++)
            if (Time.time < immuneUntil[i]) return true;
        return false;
    }

    // Tells listeners ONLY when something actually started or ended
    void CheckChanged()
    {
        int mask = 0;
        if (IsStunned) mask |= 1;
        if (IsSlowed) mask |= 2;
        if (IsBurning) mask |= 4;
        if (IsDisarmed) mask |= 8;
        if (HasAnyImmunity()) mask |= 16;

        if (mask == lastMask) return;
        lastMask = mask;
        StatusChanged?.Invoke();
    }
}