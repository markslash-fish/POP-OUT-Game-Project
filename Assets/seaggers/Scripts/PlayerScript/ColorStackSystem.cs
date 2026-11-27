using System;
using UnityEngine;

/// <summary>
/// Put on every player who ATTACKS (same object as Health).
/// Counts the stacks this player builds with balloon hits and fires the color effect at 3 stacks.
///   Center hit = 2 stacks, Outer (splash) hit = 1 stack, Sword = 0. Overflow carries over. Stacks decay after 4s.
/// </summary>
public class ColorStackSystem : MonoBehaviour
{
    [Header("References")]
    public Health health;                       // auto-found
    public PlayerColorIdentity identity;        // auto-found
    public ColorAbilitySettings settings;       // optional asset; empty = docx defaults

    [Header("Stacks (from the Game Design docx)")]
    public int centerHitStacks = 2;
    public int outerHitStacks = 1;
    public int threshold = 3;
    public float decayDelay = 4f;               // seconds without landing a hit before decay starts
    public float decayInterval = 1f;            // then 1 stack is lost every second

    public int Stacks { get; private set; }
    internal bool UseNetworkHits { get; set; }
    public event Action<int> StacksChanged;                     // HUD
    public event Action<PlayerColor, Health> AbilityTriggered;  // for future VFX / sound

    float lastHitTime = -999f;
    float nextDecayTime;

    void Awake()
    {
        if (health == null) health = GetComponent<Health>();
        if (identity == null) identity = GetComponent<PlayerColorIdentity>();
        if (settings == null) settings = ScriptableObject.CreateInstance<ColorAbilitySettings>();   // docx defaults
    }

    void OnEnable()
    {
        Health.AnyDamaged += OnAnyDamaged;
        if (health != null) health.Died += OnOwnerDied;
    }

    void OnDisable()
    {
        Health.AnyDamaged -= OnAnyDamaged;
        if (health != null) health.Died -= OnOwnerDied;
    }

    // PLACEHOLDER (Open Question 2): stacks are cleared when this player dies
    void OnOwnerDied(Health victim, Health killer)
    {
        SetStacks(0);
    }

    void OnAnyDamaged(Health victim, Health attacker, float amount, HitType type)
    {
        if (UseNetworkHits) return;
        if (attacker != health || victim == health) return;                     // only OUR hits on OTHER players
        if (type != HitType.BalloonCenter && type != HitType.BalloonOuter) return;   // sword / burn / hazards give no stacks
        if (!CombatRules.StacksEnabled) return;                                  // "Oh Noir!"

        int next = StacksAfterHit(Stacks, type == HitType.BalloonCenter, out bool triggersAbility);
        CommitHitStacks(next);
        if (triggersAbility) ApplyAbility(victim);
    }

    internal int StacksAfterHit(int current, bool directHit, out bool triggersAbility)
    {
        if (CombatRules.InstantProc)
        {
            triggersAbility = true;
            return current;
        }

        int total = current + (directHit ? centerHitStacks : outerHitStacks);
        triggersAbility = total >= threshold;
        return triggersAbility ? total - threshold : total;
    }

    internal void CommitHitStacks(int value)
    {
        lastHitTime = Time.time;
        nextDecayTime = 0f;
        SetStacks(value);
    }

    void Update()
    {
        if (Stacks <= 0) return;
        if (Time.time - lastHitTime < decayDelay) return;
        if (Time.time < nextDecayTime) return;

        SetStacks(Stacks - 1);
        nextDecayTime = Time.time + decayInterval * CombatRules.DecayIntervalScale;
    }

    void SetStacks(int value)
    {
        value = Mathf.Max(0, value);
        if (value == Stacks) return;
        Stacks = value;
        StacksChanged?.Invoke(Stacks);
    }

    // ---------------------------------------------------------------- the 7 color effects
    internal float ApplyAbility(Health victim)
    {
        PlayerColor color = identity != null ? identity.CurrentColor : PlayerColor.Red;
        AbilityTriggered?.Invoke(color, victim);
        Debug.Log(victim != null ? $"{health.playerName} triggers {color} on {victim.playerName}" :
            $"{health.playerName} triggers {color}");

        bool victimAlive = victim != null && !victim.IsDead && victim.Current > 0f;
        StatusEffectSystem vs = victim != null ? victim.Status : null;
        var s = settings;

        switch (color)
        {
            case PlayerColor.Yellow:   // Stun: no move, no melee, no throwing
                if (victimAlive && vs != null) vs.Apply(StatusType.Stun, s.stunDuration, health);
                break;

            case PlayerColor.Blue:     // Slow
                if (victimAlive && vs != null) vs.Apply(StatusType.Slow, s.slowDuration, health, s.slowSpeedMultiplier);
                break;

            case PlayerColor.Green:    // Lifesteal: take 15% of the target's CURRENT health and heal the same amount
                if (!victimAlive) break;
                if (vs != null && vs.IsImmuneTo(StatusType.Lifesteal)) break;
                float drain = victim.Current * s.lifestealPercentOfCurrentHealth;
                if (victim.TakeDamage(drain, health, HitType.Other))
                {
                    if (!health.IsRemoteReplica) health.Heal(drain);
                    return drain;
                }
                break;

            case PlayerColor.Red:      // Burn: 5% max HP per second for 3 seconds
                if (victimAlive && vs != null)
                    vs.Apply(StatusType.Burn, s.burnDuration, health, s.burnPercentOfMaxPerTick, s.burnTickInterval);
                break;

            case PlayerColor.Pink:     // Disarm: no throwing / sword, can still move
                if (victimAlive && vs != null) vs.Apply(StatusType.Disarm, s.disarmDuration, health);
                break;

            case PlayerColor.Purple:   // Bonus burst on top of the balloon's own damage
                if (victimAlive) victim.TakeDamage(victim.maxHealth * s.purpleBurstPercentOfMax, health, HitType.Other);
                break;

            case PlayerColor.Orange:   // Buff on the USER: immune to Stun, Slow, Burn, Disarm, Lifesteal
                if (!health.IsRemoteReplica && !health.IsDead) health.Status?.AddNegativeImmunity(s.orangeImmunityDuration);
                break;
        }
        return 0f;
    }
}
