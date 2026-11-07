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
    public float pushRadius = 2.5f;
    public float pushForce = 0f;     // 0 = no knockback for now

    bool popped;

    void OnCollisionEnter(Collision c)
    {
        if (popped) return;
        if ((ignoreLayers.value & (1 << c.gameObject.layer)) != 0) return;

        popped = true;
        Vector3 point = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
        Pop(point);
        Destroy(gameObject);
    }

    void Pop(Vector3 pos)
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

        if (pushForce > 0f)
        {
            foreach (var col in Physics.OverlapSphere(pos, pushRadius))
            {
                var rb = col.attachedRigidbody;
                if (rb != null) rb.AddExplosionForce(pushForce, pos, pushRadius, 0.5f, ForceMode.Impulse);
            }
        }
    }
}