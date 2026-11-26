using System;
using System.Collections.Generic;
using UnityEngine;

public enum DeathCause { Damage, RingOut }
public enum HitType { Other, BalloonCenter, BalloonOuter, Sword, Hazard }   // the stack system needs to know what hit us

/// <summary>
/// Health of one player. Put this on the parent object of ALL ragdoll colliders (e.g. Physical_Body).
/// All damage/death must go through TakeDamage() / Kill() so the server can own it later (multiplayer).
/// </summary>
public class Health : MonoBehaviour
{
    // ---- Registry of all players (used by respawn and score) ----
    public static readonly List<Health> All = new List<Health>();
    public static event Action<Health, Health, DeathCause> AnyDied;   // (victim, killer or null, cause)
    public static event Action<Health, Health, float, HitType> AnyDamaged;   // (victim, attacker or null, amount, type)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); AnyDied = null; AnyDamaged = null; }

    [Header("Identity")]
    public string playerName = "Player";     // shown in logs / score (give each player a different name)
    public Transform anchor;                 // body used for position (the Spine). Auto-found from KnockbackReceiver.

    [Header("Health")]
    public float maxHealth = 100f;           // PROPOSAL value (not defined in design.md)

    [Header("Kill credit")]
    public float creditWindow = 5f;          // PROPOSAL: last attacker within this many seconds gets the kill

    public float Current { get; private set; }
    public bool IsDead { get; private set; }
    public Health LastAttacker { get; private set; }
    public float LastHitTime { get; private set; }
    public DeathCause LastDeathCause { get; private set; }
    public Vector3 Position => anchor != null ? anchor.position : transform.position;
    public StatusEffectSystem Status { get; private set; }   // found automatically (can be null)

    float invulnerableUntil;
    public bool IsInvulnerable => Time.time < invulnerableUntil;

    public event Action<Health> HealthChanged;           // (victim)
    public event Action<Health, float> Damaged;          // (victim, amount)
    public event Action<Health, Health> Died;            // (victim, killer - can be null)

    void Awake()
    {
        Current = maxHealth;
        Status = GetComponent<StatusEffectSystem>();

        if (anchor == null)
        {
            var kb = GetComponent<KnockbackReceiver>();
            if (kb != null && kb.body != null) anchor = kb.body.transform;
        }
    }

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
    }

    /// <summary>Returns true if the damage was actually applied.</summary>
    public bool TakeDamage(float amount, Health attacker = null, HitType hitType = HitType.Other)
    {
        if (IsDead || IsInvulnerable || amount <= 0f) return false;

        Current = Mathf.Max(0f, Current - amount);

        if (attacker != null && attacker != this)
        {
            LastAttacker = attacker;
            LastHitTime = Time.time;
        }

        Damaged?.Invoke(this, amount);
        AnyDamaged?.Invoke(this, attacker, amount, hitType);
        HealthChanged?.Invoke(this);

        if (Current <= 0f) Die(DeathCause.Damage);
        return true;
    }

    /// <summary>Instant death (ring-out). Ignores the respawn shield.</summary>
    public void Kill(DeathCause cause)
    {
        if (IsDead) return;
        Current = 0f;
        HealthChanged?.Invoke(this);
        Die(cause);
    }

    /// <summary>Remember who pushed us WITHOUT dealing damage (the sword). A ring-out must still credit the pusher.</summary>
    public void RegisterAttacker(Health attacker)
    {
        if (attacker == null || attacker == this || IsDead) return;
        LastAttacker = attacker;
        LastHitTime = Time.time;
    }

    public void Heal(float amount)   // for the Green "Lifesteal" passive later
    {
        if (IsDead || amount <= 0f) return;
        Current = Mathf.Min(maxHealth, Current + amount);
        HealthChanged?.Invoke(this);
    }

    public void GrantInvulnerability(float seconds)   // respawn shield
    {
        invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + seconds);
    }

    public void ResetHealth()
    {
        Current = maxHealth;
        IsDead = false;
        LastAttacker = null;   // old credit must not carry into the next life
        HealthChanged?.Invoke(this);
    }

    void Die(DeathCause cause)
    {
        if (IsDead) return;   // never die twice (nested damage from color effects)
        IsDead = true;
        LastDeathCause = cause;

        Health killer = (LastAttacker != null && Time.time - LastHitTime <= creditWindow) ? LastAttacker : null;

        Died?.Invoke(this, killer);
        AnyDied?.Invoke(this, killer, cause);
    }
}