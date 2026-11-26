using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class RespawnSystem : MonoBehaviour
{
    [Header("References")]
    public Health health;
    public KnockbackReceiver knockback;
    public Transform feetReference;
    public Transform[] rootsToMove;

    [Header("Respawn")]
    public float respawnDelay = 0f;
    public float shieldSeconds = 1.5f;
    public float blinkInterval = 0.1f;

    [Header("Respawn VFX")]
    public GameObject respawnVfxPrefab;
    [Min(0.1f)] public float respawnVfxLifetime = 2f;

    [Header("Shield VFX")]
    public GameObject shieldVfxPrefab;
    public Transform shieldVfxPoint;

    Rigidbody[] bodies;
    Renderer[] renderers;
    Coroutine blinkRoutine;
    GameObject shieldVfxInstance;

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

        var rbList = new List<Rigidbody>();
        var rendList = new List<Renderer>();

        foreach (var root in rootsToMove)
        {
            rbList.AddRange(root.GetComponentsInChildren<Rigidbody>(true));

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                // Blink visible character meshes only.
                if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                if (!r.enabled) continue;
                if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;

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
        ClearShieldVfx();
    }

    void OnDied(Health victim, Health killer)
    {
        ClearShieldVfx();
        StartCoroutine(RespawnRoutine());
    }

    IEnumerator RespawnRoutine()
    {
        // Let the damage event finish before respawning.
        yield return null;
        if (respawnDelay > 0f) yield return new WaitForSeconds(respawnDelay);

        Teleport(ChooseSpawnPosition());

        if (knockback != null) knockback.ResetState();
        health.ResetHealth();
        health.GrantInvulnerability(shieldSeconds);
        PlayShieldVfx();

        if (respawnVfxPrefab != null)
        {
            var effect = Instantiate(
                respawnVfxPrefab,
                feetReference.position,
                respawnVfxPrefab.transform.rotation
            );
            Destroy(effect, respawnVfxLifetime);
        }

        if (blinkRoutine != null) StopCoroutine(blinkRoutine);
        blinkRoutine = StartCoroutine(Blink());
    }

    // Pick the spawn point farthest from its nearest living opponent.
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
                minDist = Mathf.Min(
                    minDist,
                    Vector3.Distance(other.Position, p.transform.position)
                );
            }

            if (minDist == float.MaxValue) minDist = Random.value * 100f;

            if (minDist > bestDist)
            {
                bestDist = minDist;
                best = p;
            }
        }

        return best.transform.position;
    }

    void Teleport(Vector3 spawnPos)
    {
        // Keep the ragdoll and animated body aligned as they move.
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

    void SetVisible(bool visible)
    {
        foreach (var r in renderers)
            if (r != null) r.enabled = visible;
    }

    void PlayShieldVfx()
    {
        if (!isActiveAndEnabled || shieldVfxPrefab == null || shieldSeconds <= 0f)
            return;

        Transform point = shieldVfxPoint != null ? shieldVfxPoint : feetReference;
        shieldVfxInstance = Instantiate(
            shieldVfxPrefab, point.position, point.rotation, point
        );
        Destroy(shieldVfxInstance, shieldSeconds);
    }

    void ClearShieldVfx()
    {
        if (shieldVfxInstance == null) return;

        shieldVfxInstance.SetActive(false);
        Destroy(shieldVfxInstance);
        shieldVfxInstance = null;
    }
}