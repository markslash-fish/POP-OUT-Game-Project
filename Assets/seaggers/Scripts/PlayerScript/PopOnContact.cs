using System.Collections.Generic;
using UnityEngine;

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

    bool popped;

    void OnCollisionEnter(Collision c)
    {
        if (popped) return;
        if ((ignoreLayers.value & (1 << c.gameObject.layer)) != 0) return;

        popped = true;
        Vector3 point = c.contactCount > 0 ? c.GetContact(0).point : transform.position;

        // did we land DIRECTLY on a player? (that is the "center / bullseye" hit)
        Health directHit = c.collider.GetComponentInParent<Health>();

        Pop(point, directHit);
        Destroy(gameObject);
    }

    void Pop(Vector3 pos, Health directHit)
    {
        var go = new GameObject("PopFX");
        go.transform.position = pos;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 1f;
        main.loop = false;
        main.startLifetime = lifetime;
        main.startSpeed = new ParticleSystem.MinMaxCurve(burstSpeed * 0.5f, burstSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
        main.startColor = color;
        main.gravityModifier = 0.5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)particleCount) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.1f;

        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        fade.color = grad;

        var shrink = ps.sizeOverLifetime;
        shrink.enabled = true;
        shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = new Material(Shader.Find("Sprites/Default"));

        ps.Play();
        Destroy(go, 2f);

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

    // Finds every player in the splash radius. The one we hit directly = CENTER hit, everyone else = OUTER hit.
    void ApplySplash(Vector3 pos, Health directHit)
    {
        // a ragdoll has many colliders, so make sure each player is processed only ONCE
        var found = new HashSet<Health>();

        foreach (var col in Physics.OverlapSphere(pos, pushRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (h != null) found.Add(h);
        }
        if (directHit != null) found.Add(directHit);

        foreach (Health h in found)
        {
            if (h.IsDead || h.IsInvulnerable) continue;   // respawn shield blocks damage AND knockback

            bool isOwner = h == owner;
            bool isCenter = h == directHit;
            HitType type = isCenter ? HitType.BalloonCenter : HitType.BalloonOuter;
            float percent = isCenter ? centerDamagePercent : outerDamagePercent;

            if (!isOwner || damageOwner)
                h.TakeDamage(h.maxHealth * percent, owner, type);

            var kb = h.GetComponent<KnockbackReceiver>();
            if (kb != null && kb.body != null)
            {
                Vector3 dir = kb.body.position - pos;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f)
                {
                    Vector2 rnd = Random.insideUnitCircle.normalized;
                    dir = new Vector3(rnd.x, 0f, rnd.y);
                }
                dir = dir.normalized + Vector3.up * knockbackUpward;

                float speed = knockbackSpeed * (isCenter ? 1f : outerKnockbackScale) * (isOwner ? ownerKnockbackScale : 1f);
                kb.ApplyKnockback(dir, speed);
            }
        }
    }
}