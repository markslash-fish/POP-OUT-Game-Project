using UnityEngine;

/// <summary>Put on the SAME object as Health. Kills the player when they fall below the RingOutZone.</summary>
public class RingOutDetector : MonoBehaviour
{
    public Health health;
    public float fallbackKillY = -10f;   // used only if the scene has no RingOutZone

    void Awake()
    {
        if (health == null) health = GetComponent<Health>();
    }

    void FixedUpdate()
    {
        if (health == null || health.IsDead) return;

        float killY = RingOutZone.Instance != null ? RingOutZone.Instance.KillY : fallbackKillY;
        if (health.Position.y < killY)
            health.Kill(DeathCause.RingOut);
    }
}