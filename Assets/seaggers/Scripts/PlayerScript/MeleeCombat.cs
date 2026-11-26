using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MeleeCombat : MonoBehaviour
{
    [Header("References")]
    public Health ownerHealth;
    public Animator animator;
    public AnimatedBodyMover mover;
    public MeleeButton attackButton;

    [Header("Tester controls")]
    public bool enableTesterInput;

    [Header("Attack")]
    public float damage = 0f;
    public float knockbackSpeed = 12f;
    public float knockbackUpward = 0.25f;
    [Range(0f, 1f)] public float minKnockbackScale = 0.35f;
    public float cooldown = 3f;
    public float windup = 0.12f;
    public float activeTime = 0.18f;
    public float inputBuffer = 0.15f;

    [Header("Hit zone")]
    public float reach = 2.5f;
    [Range(10f, 360f)] public float arcAngle = 360f;
    public LayerMask hitLayers = ~0;

    [Header("Feedback")]
    public bool showPlaceholderSwing = true;
    public string swingTrigger = "Swing";
    public GameObject hitEffectPrefab;

    [Header("Slash VFX")]
    public GameObject slashVfxPrefab;
    public Transform slashVfxPoint;
    [Min(0.1f)] public float slashVfxLifetime = 1.5f;

    float nextAttackTime, bufferUntil, windupEnd, activeEnd;
    bool swinging;
    bool playedSlashVfx;
    readonly HashSet<Health> hitThisSwing = new HashSet<Health>();
    readonly List<RaycastResult> pointerHits = new List<RaycastResult>();
    readonly Collider[] overlapBuffer = new Collider[128];
    bool hasSwingParam;
    LineRenderer swingLine;
    Material swingLineMaterial;
    Vector3[] swingPositions;
    const int arcPoints = 13;

    public event System.Action<Vector3, float> SwingStarted;
    bool replaying;
    Vector3 replayOrigin;
    Vector3 replayForward;

    void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (mover == null) mover = GetComponent<AnimatedBodyMover>();
        if (ownerHealth == null)
            Debug.LogWarning("MeleeCombat: assign Owner Health.", this);

        CacheSwingParam();
        MakeSwingLine();
    }

    void Update()
    {
        if (ownerHealth != null && ownerHealth.IsRemoteReplica) return;

        if (!MatchManager.InputEnabled)
        {
            bufferUntil = 0f;
            if (attackButton != null) attackButton.ConsumePress();
            return;
        }

        bool buttonPressed = attackButton != null && attackButton.ConsumePress();
        if (buttonPressed || TesterClickPressed())
            bufferUntil = Time.time + inputBuffer;

        if (bufferUntil > 0f && Time.time <= bufferUntil && CanSwing())
        {
            StartSwing();
            SwingStarted?.Invoke(ownerHealth.Position, transform.eulerAngles.y);
        }
    }

    public void ReplaySwing(Vector3 origin, float yaw)
    {
        replaying = true;
        replayOrigin = origin;
        replayForward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        StartSwing();
    }

    bool TesterClickPressed()
    {
        if (!enableTesterInput || Input.touchCount > 0 ||
            !Input.GetMouseButtonDown(0))
            return false;

        var eventSystem = EventSystem.current;
        if (eventSystem == null) return true;

        var pointer = new PointerEventData(eventSystem)
        {
            position = Input.mousePosition
        };

        eventSystem.RaycastAll(pointer, pointerHits);

        foreach (var hit in pointerHits)
        {
            if (hit.module is GraphicRaycaster) return false;
        }

        return true;
    }

    bool CanSwing()
    {
        if (ownerHealth == null || ownerHealth.IsDead) return false;
        if (ownerHealth.Status != null && !ownerHealth.Status.CanAttack) return false;
        if (Time.time < nextAttackTime) return false;
        if (mover != null && mover.holding) return false;
        return true;
    }

    void StartSwing()
    {
        bufferUntil = 0f;
        nextAttackTime = Time.time + cooldown;
        windupEnd = Time.time + windup;
        activeEnd = windupEnd + activeTime;
        swinging = true;
        playedSlashVfx = false;
        hitThisSwing.Clear();

        if (hasSwingParam) animator.SetTrigger(swingTrigger);
    }

    void FixedUpdate()
    {
        if (!swinging) return;

        if (ownerHealth == null || ownerHealth.IsDead || Time.time >= activeEnd)
        {
            swinging = false;
            return;
        }

        if (Time.time < windupEnd) return;

        if (!playedSlashVfx && slashVfxPrefab != null)
        {
            playedSlashVfx = true;

            Vector3 position = slashVfxPoint != null
                ? slashVfxPoint.position
                : ownerHealth.Position;

            Quaternion rotation = slashVfxPoint != null
                ? slashVfxPoint.rotation
                : transform.rotation;

            var effect = Instantiate(slashVfxPrefab, position, rotation);
            Destroy(effect, slashVfxLifetime);
        }

        DetectHits();
    }

    void DetectHits()
    {
        Vector3 origin = replaying ? replayOrigin : ownerHealth.Position;
        Vector3 forward = replaying ? replayForward : transform.forward;
        forward.y = 0f;
        forward.Normalize();

        int count = Physics.OverlapSphereNonAlloc(
            origin, reach + 0.5f, overlapBuffer, hitLayers,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < count; i++)
        {
            Collider col = overlapBuffer[i];
            Health target = col.GetComponentInParent<Health>();

            if (target == null || target == ownerHealth || target.IsRemoteReplica) continue;
            if (hitThisSwing.Contains(target)) continue;
            if (target.IsDead || target.IsInvulnerable) continue;

            Vector3 point = ClosestPointSafe(col, origin);
            Vector3 toTarget = point - origin;
            toTarget.y = 0f;

            if (toTarget.magnitude > reach) continue;

            if (arcAngle < 359f && toTarget.sqrMagnitude > 0.0025f &&
                Vector3.Angle(forward, toTarget) > arcAngle * 0.5f)
                continue;

            hitThisSwing.Add(target);
            Hit(target, point, origin, forward, toTarget.magnitude);
        }
    }

    void Hit(Health target, Vector3 point, Vector3 origin, Vector3 forward, float distance)
    {
        if (damage > 0f)
            target.TakeDamage(damage, ownerHealth, HitType.Sword);
        else
            target.RegisterAttacker(ownerHealth);

        var knockback = target.GetComponent<KnockbackReceiver>();
        if (knockback != null)
        {
            Vector3 direction = target.Position - origin;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) direction = forward;

            direction = direction.normalized + Vector3.up * knockbackUpward;
            float scale = Mathf.Lerp(
                1f, minKnockbackScale, Mathf.Clamp01(distance / reach)
            );

            knockback.ApplyKnockback(direction, knockbackSpeed * scale);
        }

        if (hitEffectPrefab != null)
            Destroy(Instantiate(hitEffectPrefab, point, Quaternion.identity), 2f);
    }

    void CacheSwingParam()
    {
        hasSwingParam = false;
        if (animator == null) return;

        foreach (var parameter in animator.parameters)
        {
            if (parameter.name == swingTrigger &&
                parameter.type == AnimatorControllerParameterType.Trigger)
            {
                hasSwingParam = true;
                break;
            }
        }

        if (!hasSwingParam)
            Debug.Log($"MeleeCombat: Animator has no '{swingTrigger}' trigger.", this);
    }

    void MakeSwingLine()
    {
        if (!showPlaceholderSwing) return;

        var arc = new GameObject("PlaceholderSwingArc");
        arc.transform.SetParent(transform, false);
        swingLine = arc.AddComponent<LineRenderer>();
        swingLineMaterial = new Material(Shader.Find("Sprites/Default"));
        swingLine.sharedMaterial = swingLineMaterial;
        swingPositions = new Vector3[arcPoints];
        swingLine.startColor = swingLine.endColor = new Color(1f, 0.6f, 0.15f, 0.9f);
        swingLine.startWidth = swingLine.endWidth = 0.12f;
        swingLine.positionCount = arcPoints;
        swingLine.useWorldSpace = true;
        swingLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        swingLine.receiveShadows = false;
        swingLine.enabled = false;
    }

    void OnDestroy()
    {
        if (swingLineMaterial != null) Destroy(swingLineMaterial);
    }

    void LateUpdate()
    {
        if (swingLine == null) return;

        bool active = swinging && Time.time >= windupEnd && Time.time < activeEnd;
        swingLine.enabled = active;
        if (!active || ownerHealth == null) return;

        Vector3 origin = ownerHealth.Position + Vector3.down * 0.3f;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        forward.Normalize();

        float half = arcAngle * 0.5f;

        for (int i = 0; i < arcPoints; i++)
        {
            float angle = -half + arcAngle * i / (arcPoints - 1f);
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * forward;
            swingPositions[i] = origin + direction * reach;
        }
        swingLine.SetPositions(swingPositions);
    }

    static Vector3 ClosestPointSafe(Collider col, Vector3 position)
    {
        if (col is MeshCollider mesh && !mesh.convex)
            return col.bounds.ClosestPoint(position);

        return col.ClosestPoint(position);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 origin = ownerHealth != null
            ? ownerHealth.Position
            : transform.position + Vector3.up;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) return;
        forward.Normalize();

        Gizmos.color = Color.yellow;

        float half = arcAngle * 0.5f;
        Vector3 left = Quaternion.AngleAxis(-half, Vector3.up) * forward * reach;
        Vector3 right = Quaternion.AngleAxis(half, Vector3.up) * forward * reach;
        Gizmos.DrawLine(origin, origin + left);
        Gizmos.DrawLine(origin, origin + right);

        Vector3 previous = origin + left;

        for (int i = 1; i <= 12; i++)
        {
            float angle = -half + arcAngle * i / 12f;
            Vector3 next =
                origin + Quaternion.AngleAxis(angle, Vector3.up) * forward * reach;
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
