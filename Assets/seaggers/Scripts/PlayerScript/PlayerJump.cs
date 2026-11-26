using UnityEngine;

public class PlayerJump : MonoBehaviour
{
    public Health health;
    public Rigidbody body;
    public Transform groundCheck;
    public LayerMask groundLayers;

    [Header("Tester controls")]
    public bool enableTesterInput;
    public bool logJumpChecks = true;

    [Header("Jump settings")]
    [Min(0.1f)] public float jumpSpeed = 5f;
    [Min(0.01f)] public float checkRadius = 0.15f;
    [Min(0.01f)] public float checkDistance = 0.18f;

    const float ProbeLift = 0.35f;
    readonly RaycastHit[] groundHits = new RaycastHit[32];

    bool jumpRequested;

    void Awake()
    {
        if (health == null) health = GetComponent<Health>();

        var knockback = GetComponent<KnockbackReceiver>();
        if (knockback == null && health != null)
            knockback = health.GetComponent<KnockbackReceiver>();

        if (knockback != null)
        {
            if (body == null) body = knockback.body;

            if (groundCheck == null && knockback.ragdoll != null)
                groundCheck = knockback.ragdoll.animatedRoot;
        }
    }

    public void RequestJump()
    {
        if (isActiveAndEnabled) jumpRequested = true;
    }

    void Update()
    {
        if (enableTesterInput && Input.GetKeyDown(KeyCode.Space))
            RequestJump();
    }

    void OnDisable()
    {
        jumpRequested = false;
    }

    void FixedUpdate()
    {
        if (!jumpRequested) return;
        jumpRequested = false;

        if (!MatchManager.InputEnabled)
        {
            Report("Wait for GO before jumping.");
            return;
        }

        if (health != null &&
            (health.IsDead || (health.Status != null && health.Status.IsStunned)))
        {
            Report("The player is dead or stunned.");
            return;
        }

        if (body == null)
        {
            Report("Assign the same Spine Rigidbody used by KnockbackReceiver.");
            return;
        }

        if (!body.gameObject.activeInHierarchy || body.isKinematic ||
            (body.constraints & RigidbodyConstraints.FreezePositionY) != 0)
        {
            Report("The Spine Rigidbody must be dynamic with Position Y free.");
            return;
        }

        if (groundCheck == null || groundLayers.value == 0)
        {
            Report("Assign Ground Check at the feet and select the floor layer.");
            return;
        }

        if (body.linearVelocity.y > 1f)
        {
            Report("The player is already moving up.");
            return;
        }

        if (!HasGround())
        {
            Report("Ground Check found no floor in Ground Layers.");
            return;
        }

        foreach (var part in body.GetComponentsInChildren<Rigidbody>())
        {
            if (part.isKinematic) continue;

            Vector3 velocity = part.linearVelocity;
            velocity.y = jumpSpeed;
            part.linearVelocity = velocity;
        }

        Report("Jump applied to the ragdoll.");
    }

    bool HasGround()
    {
        Vector3 origin = groundCheck.position + Vector3.up * (checkRadius + ProbeLift);
        int count = Physics.SphereCastNonAlloc(
            origin,
            checkRadius,
            Vector3.down,
            groundHits,
            ProbeLift + checkDistance,
            groundLayers,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundHits[i];
            if (hit.collider == null || hit.normal.y < 0.5f) continue;

            Transform hitTransform = hit.collider.transform;
            if (hitTransform.IsChildOf(body.transform)) continue;
            if (health != null && hitTransform.IsChildOf(health.transform)) continue;

            return true;
        }

        return false;
    }

    void Report(string message)
    {
        if (logJumpChecks)
            Debug.Log($"[PlayerJump] {message}", this);
    }
}