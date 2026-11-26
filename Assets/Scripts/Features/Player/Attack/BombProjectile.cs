using UnityEngine;

public class BombProjectile : MonoBehaviour
{
    private PlayerAttackMechanics owner;
    private Vector3 spawnPoint;
    private Vector3 target;
    private float duration;
    private float arcHeight;
    private float elapsed;

    public void Initialize(PlayerAttackMechanics owner, Vector3 spawnPoint, Vector3 target, float duration, float arcHeight)
    {
        this.owner = owner;
        this.spawnPoint = spawnPoint;
        this.target = target;
        this.duration = Mathf.Max(0.01f, duration);
        this.arcHeight = Mathf.Max(0f, arcHeight);

        Collider collider = GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
        }

        Rigidbody body = GetComponent<Rigidbody>();
        if (body != null)
        {
            body.isKinematic = true;
            body.useGravity = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        foreach (PopOnContact pop in GetComponents<PopOnContact>())
        {
            pop.enabled = false;
        }

        transform.position = spawnPoint;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(elapsed / duration);
        Vector3 position = Vector3.Lerp(spawnPoint, target, progress);
        position.y += Mathf.Sin(progress * Mathf.PI) * arcHeight;
        transform.position = position;

        if (progress >= 1f)
        {
            Explode();
        }
    }

    private void Explode()
    {
        if (owner != null)
        {
            owner.ApplyBombDamage(target);
        }

        Destroy(gameObject);
    }
}
