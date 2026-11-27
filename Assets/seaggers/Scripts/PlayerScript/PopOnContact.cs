using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

public class PopOnContact : MonoBehaviour
{
    [Header("Look")]
    public Color color = new Color(0.2f, 0.5f, 1f, 1f);
    public int particleCount = 40;
    public float burstSpeed = 5f;
    public float size = 0.15f;
    public float lifetime = 0.6f;

    [Header("Contact")]
    public LayerMask ignoreLayers;   // layers that don't trigger the pop
    public float pushRadius = 2.5f;  // also the SPLASH radius for damage/knockback
    public float pushForce = 0f;     // 0 = no push for props (non-player rigidbodies)

    [Header("Damage (from the Game Design docx, as % of the victim's max HP)")]
    [Range(0f, 1f)] public float centerDamagePercent = 0.20f;   // DIRECT hit: 20% (5 hits to KO), grants 2 stacks
    [Range(0f, 1f)] public float outerDamagePercent = 0.10f;    // SPLASH hit: 10% (10 hits to KO), grants 1 stack

    [Header("Knockback (PROPOSAL - the docx only says 'physics-based knockback')")]
    public float knockbackSpeed = 8f;                           // m/s on a direct hit
    [Range(0f, 1f)] public float outerKnockbackScale = 0.6f;    // splash pushes this much of a direct hit
    public float knockbackUpward = 0.3f;
    public bool damageOwner = false;                            // does the thrower hurt themselves?
    [Range(0f, 1f)] public float ownerKnockbackScale = 0.5f;    // self-knockback strength (like a rocket jump)

    [HideInInspector] public Health owner;   // set by Thrower
    internal Action<Vector3, Health, IReadOnlyCollection<Health>> ImpactReported;

    const int ParticlesPerBurstLimit = 1000;
    static readonly Collider[] SplashContacts = new Collider[64];
    static ParticleSystem popParticles;
    static Material popMaterial;
    bool popped;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPopResources()
    {
        if (popMaterial != null) Destroy(popMaterial);
        popMaterial = null;
        popParticles = null;
    }

    void OnCollisionEnter(Collision c)
    {
        if (popped) return;
        if ((ignoreLayers.value & (1 << c.gameObject.layer)) != 0) return;

        Health directHit = c.collider.GetComponentInParent<Health>();
        if (owner != null && directHit == owner) return;

        popped = true;
        Vector3 point = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
        Pop(point, directHit);
        Destroy(gameObject);
    }

    void Pop(Vector3 pos, Health directHit)
    {
        ShowImpact(pos, owner);
        ApplySplash(pos, directHit);

        if (pushForce > 0f)
        {
            foreach (var col in Physics.OverlapSphere(pos, pushRadius))
            {
                var rb = col.attachedRigidbody;
                // players are pushed by KnockbackReceiver instead, so skip them here
                if (rb != null && rb.GetComponentInParent<Health>() == null)
                    rb.AddExplosionForce(pushForce, pos, pushRadius, 0.5f, ForceMode.Impulse);
            }
        }
    }

    internal void ShowImpact(Vector3 position, Health attacker)
    {
        int count = Mathf.Clamp(particleCount, 0, ParticlesPerBurstLimit);
        if (count == 0) return;
        if (popParticles == null) popParticles = CreatePopParticles();

        var settings = popParticles.main;
        int requiredCapacity = popParticles.particleCount + count;
        if (settings.maxParticles < requiredCapacity) settings.maxParticles = requiredCapacity;

        settings.startLifetime = lifetime;
        settings.startSpeed = new ParticleSystem.MinMaxCurve(burstSpeed * 0.5f, burstSpeed);
        settings.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
        settings.startColor = attacker != null && attacker.TryGetComponent(out PlayerColorIdentity identity)
            ? PlayerColorInfo.ToColor(identity.CurrentColor)
            : color;
        popParticles.transform.position = position;
        popParticles.Emit(count);
    }

    static ParticleSystem CreatePopParticles()
    {
        var particles = new GameObject("PopFX").AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = particles.main;
        main.playOnAwake = false;
        main.loop = true;
        main.gravityModifier = 0.5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = particles.emission;
        emission.rateOverTime = 0f;

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.1f;

        var fade = particles.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;

        var shrink = particles.sizeOverLifetime;
        shrink.enabled = true;
        shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        if (popMaterial == null) popMaterial = new Material(Shader.Find("Sprites/Default"));
        particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = popMaterial;
        particles.Play();
        return particles;
    }

    // Finds every player in the splash radius. The one we hit directly = CENTER hit, everyone else = OUTER hit.
    void ApplySplash(Vector3 pos, Health directHit)
    {
        // a ragdoll has many colliders, so make sure each player is processed only ONCE
        using var pooled = HashSetPool<Health>.Get(out var found);
        int count = Physics.OverlapSphereNonAlloc(pos, pushRadius, SplashContacts, ~0, QueryTriggerInteraction.Ignore);
        var contacts = SplashContacts;
        if (count == contacts.Length)
        {
            contacts = Physics.OverlapSphere(pos, pushRadius, ~0, QueryTriggerInteraction.Ignore);
            count = contacts.Length;
        }
        for (int i = 0; i < count; i++)
        {
            var h = contacts[i].GetComponentInParent<Health>();
            if (h != null) found.Add(h);
        }
        System.Array.Clear(SplashContacts, 0, SplashContacts.Length);
        if (directHit != null) found.Add(directHit);

        ImpactReported?.Invoke(pos, directHit, found);
        foreach (Health h in found)
            ApplyHit(h, pos, h == directHit, owner);
    }

    internal bool ApplyHit(Health target, Vector3 pos, bool isCenter, Health attacker)
    {
        if (target.IsRemoteReplica || target.IsDead || target.IsInvulnerable) return false;

        bool isOwner = target == attacker;
        HitType type = isCenter ? HitType.BalloonCenter : HitType.BalloonOuter;
        float percent = isCenter ? centerDamagePercent : outerDamagePercent;

        bool damaged = (!isOwner || damageOwner) && target.TakeDamage(target.maxHealth * percent, attacker, type);

        var kb = target.GetComponent<KnockbackReceiver>();
        if (kb == null || kb.body == null) return damaged;

        Vector3 dir = kb.body.position - pos;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
        {
            Vector2 rnd = UnityEngine.Random.insideUnitCircle.normalized;
            dir = new Vector3(rnd.x, 0f, rnd.y);
        }
        dir = dir.normalized + Vector3.up * knockbackUpward;

        float speed = knockbackSpeed * (isCenter ? 1f : outerKnockbackScale) * (isOwner ? ownerKnockbackScale : 1f);
        kb.ApplyKnockback(dir, speed);
        return damaged;
    }
}
