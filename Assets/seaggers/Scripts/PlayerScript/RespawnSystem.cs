using System.Collections;
using UnityEngine;

/// <summary>
/// Put on the SAME object as Health. When the player dies it teleports them to the safest SpawnPoint,
/// refills health and gives the respawn shield.
/// </summary>
public class RespawnSystem : MonoBehaviour
{
    [Header("References")]
    public Health health;                    // auto-found on the same object
    public KnockbackReceiver knockback;      // optional, auto-found on the same object
    public Transform feetReference;          // Animated_Body (its root position = the player's feet)
    public Transform[] rootsToMove;          // the top-level objects to teleport (see setup notes)

    [Header("Respawn")]
    public float respawnDelay = 0f;          // design says "instantly": 0 = next frame
    public float shieldSeconds = 1.5f;       // from the design document
    public float blinkInterval = 0.1f;       // shield visual feedback

    Rigidbody[] bodies;
    Renderer[] renderers;
    Coroutine blinkRoutine;

    void Awake()
    {
        if (health == null) health = GetComponent<Health>();
        if (knockback == null) knockback = GetComponent<KnockbackReceiver>();

        if (feetReference == null || rootsToMove == null || rootsToMove.Length == 0)
        {
            Debug.LogError("RespawnSystem: assign Feet Reference and Roots To Move.", this);
            enabled = false;
            return;
        }

        var rbList = new System.Collections.Generic.List<Rigidbody>();
        var rendList = new System.Collections.Generic.List<Renderer>();
        foreach (var root in rootsToMove)
        {
            rbList.AddRange(root.GetComponentsInChildren<Rigidbody>(true));
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                // blink only the character's meshes, not effects/lines
                if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;

                // IMPORTANT: skip renderers that are hidden on purpose (e.g. the Animated_Body "ghost" mesh).
                // We must never turn those on again.
                if (!r.enabled) continue;
                if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue;

                rendList.Add(r);
            }
        }
        bodies = rbList.ToArray();
        renderers = rendList.ToArray();
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
        StartCoroutine(RespawnRoutine());
    }

    IEnumerator RespawnRoutine()
    {
        // wait at least one frame so the code that killed us (e.g. the splash loop) can finish first
        yield return null;
        if (respawnDelay > 0f) yield return new WaitForSeconds(respawnDelay);

        Teleport(ChooseSpawnPosition());

        if (knockback != null) knockback.ResetState();
        health.ResetHealth();
        health.GrantInvulnerability(shieldSeconds);

        if (blinkRoutine != null) StopCoroutine(blinkRoutine);
        blinkRoutine = StartCoroutine(Blink());
    }

    // PROPOSAL rule: choose the spawn point that is farthest from every living opponent
    Vector3 ChooseSpawnPosition()
    {
        var points = SpawnPoint.All;
        if (points.Count == 0)
        {
            Debug.LogWarning("RespawnSystem: no SpawnPoint in the scene. Respawning in place.");
            return feetReference.position;
        }

        SpawnPoint best = null;
        float bestDist = -1f;

        foreach (var p in points)
        {
            float minDist = float.MaxValue;
            foreach (var other in Health.All)
            {
                if (other == health || other.IsDead) continue;
                minDist = Mathf.Min(minDist, Vector3.Distance(other.Position, p.transform.position));
            }
            if (minDist == float.MaxValue) minDist = Random.value * 100f;   // nobody else: random point

            if (minDist > bestDist) { bestDist = minDist; best = p; }
        }
        return best.transform.position;
    }

    void Teleport(Vector3 spawnPos)
    {
        // move everything by the SAME offset so the ragdoll and the animated body stay aligned
        Vector3 delta = spawnPos - feetReference.position + Vector3.up * 0.05f;
        foreach (var root in rootsToMove) root.position += delta;
        Physics.SyncTransforms();

        foreach (var rb in bodies)
        {
            if (rb.isKinematic) continue;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    IEnumerator Blink()
    {
        bool visible = true;
        while (health.IsInvulnerable)
        {
            visible = !visible;
            SetVisible(visible);
            yield return new WaitForSeconds(blinkInterval);
        }
        SetVisible(true);
    }

    void SetVisible(bool v)
    {
        foreach (var r in renderers)
            if (r != null) r.enabled = v;
    }
}