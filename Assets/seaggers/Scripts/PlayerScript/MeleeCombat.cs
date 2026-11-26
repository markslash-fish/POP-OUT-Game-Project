using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Balloon Sword melee attack. Put on Animated_Body (next to AnimatedBodyMover).
/// Hit detection = ONE short "active window" per swing. Every player can be hit at most ONCE per swing.
/// </summary>
public class MeleeCombat : MonoBehaviour
{
    [Header("Slash VFX")]
    public GameObject slashVfxPrefab;
    public Transform slashVfxPoint;
    [Min(0.1f)] public float slashVfxLifetime = 1.5f;

    bool playedSlashVfx;
    [Header("References")]
    public Health ownerHealth;               // the player's Health (Physical_Body)
    public Animator animator;                // auto: same object
    public AnimatedBodyMover mover;          // auto: same object (used to block melee while aiming a balloon)
    public MeleeButton attackButton;         // mobile ATTACK button (optional on PC: Space also works)

    [Header("Attack (docx: 0 damage, 3s cooldown, strong push around the player. Reach is a PROPOSAL)")]
    public float damage = 0f;               // docx: the sword deals NO damage
    public float knockbackSpeed = 12f;       // m/s
    public float knockbackUpward = 0.25f;
    [Range(0f, 1f)] public float minKnockbackScale = 0.35f;   // push strength at the EDGE of the reach (1 at point-blank)
    public float cooldown = 3f;            // seconds between swings
    public float windup = 0.12f;             // delay before the hit zone turns on (match your Swing clip)
    public float activeTime = 0.18f;         // how long the hit zone stays on
    public float inputBuffer = 0.15f;        // a tap slightly before the cooldown ends still counts

    [Header("Hit zone")]
    public float reach = 2.5f;               // meters in front of the player
    [Range(10f, 360f)] public float arcAngle = 360f;   // total width of the swing, in degrees
    public LayerMask hitLayers = ~0;

    [Header("Feedback")]
    public bool showPlaceholderSwing = true;   // orange arc while the hit zone is active. Turn OFF when the real Swing animation exists.
    public string swingTrigger = "Swing";    // Animator trigger
    public GameObject hitEffectPrefab;       // optional particle/sprite spawned at the hit point

    float nextAttackTime, bufferUntil, windupEnd, activeEnd;
    bool swinging;
    readonly HashSet<Health> hitThisSwing = new HashSet<Health>();
    readonly Collider[] overlapBuffer = new Collider[128];
    bool hasSwingParam;
    LineRenderer swingLine;
    const int arcPoints = 13;

    void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (mover == null) mover = GetComponent<AnimatedBodyMover>();
        if (ownerHealth == null) Debug.LogWarning("MeleeCombat: assign Owner Health.", this);
        CacheSwingParam();
        MakeSwingLine();
    }

    void Update()
    {
        if (!MatchManager.InputEnabled)
        {
            bufferUntil = 0f;
            if (attackButton != null) attackButton.ConsumePress();   // throw away taps during countdown / end
            return;
        }

        bool buttonPressed = attackButton != null && attackButton.ConsumePress();
        if (Input.GetKeyDown(KeyCode.Space) || buttonPressed)
            bufferUntil = Time.time + inputBuffer;

        if (Time.time <= bufferUntil && CanSwing())
            StartSwing();
    }

    bool CanSwing()
    {
        if (ownerHealth == null || ownerHealth.IsDead) return false;
        if (ownerHealth.Status != null && !ownerHealth.Status.CanAttack) return false;   // stunned or disarmed
        if (Time.time < nextAttackTime) return false;
        if (mover != null && mover.holding) return false;   // arms are busy aiming a balloon
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

        if (hasSwingParam) animator.SetTrigger(swingTrigger);   // skipped while the animation does not exist yet
    }

    void FixedUpdate()
    {
        if (!swinging) return;
        if (ownerHealth == null || ownerHealth.IsDead || Time.time >= activeEnd) { swinging = false; return; }
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
        Vector3 origin = ownerHealth.Position;
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        fwd.Normalize();

        int count = Physics.OverlapSphereNonAlloc(origin, reach + 0.5f, overlapBuffer, hitLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider col = overlapBuffer[i];
            Health target = col.GetComponentInParent<Health>();
            if (target == null || target == ownerHealth) continue;
            if (hitThisSwing.Contains(target)) continue;
            if (target.IsDead || target.IsInvulnerable) continue;

            Vector3 point = ClosestPointSafe(col, origin);
            Vector3 to = point - origin;
            to.y = 0f;

            if (to.magnitude > reach) continue;
            if (arcAngle < 359f && to.sqrMagnitude > 0.0025f && Vector3.Angle(fwd, to) > arcAngle * 0.5f) continue;

            hitThisSwing.Add(target);   // this player cannot be hit again by this swing
            Hit(target, point, fwd, to.magnitude);
        }
    }

    void Hit(Health target, Vector3 point, Vector3 fwd, float distance)
    {
        if (damage > 0f) target.TakeDamage(damage, ownerHealth, HitType.Sword);
        else target.RegisterAttacker(ownerHealth);   // no damage, but a ring-out must still credit the pusher

        var kb = target.GetComponent<KnockbackReceiver>();
        if (kb != null)
        {
            Vector3 dir = target.Position - ownerHealth.Position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = fwd;
            dir = dir.normalized + Vector3.up * knockbackUpward;
            float scale = Mathf.Lerp(1f, minKnockbackScale, Mathf.Clamp01(distance / reach));   // closer = stronger push
            kb.ApplyKnockback(dir, knockbackSpeed * scale);
        }

        if (hitEffectPrefab != null)
            Destroy(Instantiate(hitEffectPrefab, point, Quaternion.identity), 2f);
    }

    // Does the Animator really have a "Swing" trigger? If not, we skip it quietly (no Console spam).
    void CacheSwingParam()
    {
        hasSwingParam = false;
        if (animator == null) return;

        foreach (var p in animator.parameters)
        {
            if (p.name == swingTrigger && p.type == AnimatorControllerParameterType.Trigger)
            {
                hasSwingParam = true;
                break;
            }
        }

        if (!hasSwingParam)
            Debug.Log("MeleeCombat: Animator has no '" + swingTrigger + "' trigger yet - swing animation skipped (hit detection still works).", this);
    }

    // ---- PLACEHOLDER visual: an orange arc that shows the active hit zone ----
    void MakeSwingLine()
    {
        if (!showPlaceholderSwing) return;

        var go = new GameObject("PlaceholderSwingArc");
        go.transform.SetParent(transform, false);
        swingLine = go.AddComponent<LineRenderer>();
        swingLine.material = new Material(Shader.Find("Sprites/Default"));
        swingLine.startColor = swingLine.endColor = new Color(1f, 0.6f, 0.15f, 0.9f);
        swingLine.startWidth = swingLine.endWidth = 0.12f;
        swingLine.positionCount = arcPoints;
        swingLine.useWorldSpace = true;
        swingLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        swingLine.receiveShadows = false;
        swingLine.enabled = false;
    }

    void LateUpdate()
    {
        if (swingLine == null) return;

        bool active = swinging && Time.time >= windupEnd && Time.time < activeEnd;
        swingLine.enabled = active;
        if (!active || ownerHealth == null) return;

        Vector3 origin = ownerHealth.Position + Vector3.down * 0.3f;
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        fwd.Normalize();

        float half = arcAngle * 0.5f;
        for (int i = 0; i < arcPoints; i++)
        {
            float angle = -half + arcAngle * i / (arcPoints - 1f);
            swingLine.SetPosition(i, origin + Quaternion.AngleAxis(angle, Vector3.up) * fwd * reach);
        }
    }

    static Vector3 ClosestPointSafe(Collider col, Vector3 pos)
    {
        if (col is MeshCollider mc && !mc.convex) return col.bounds.ClosestPoint(pos);
        return col.ClosestPoint(pos);
    }

    // Select the player in the Scene view to SEE the hit zone (yellow) while tuning
    void OnDrawGizmosSelected()
    {
        Vector3 origin = ownerHealth != null ? ownerHealth.Position : transform.position + Vector3.up;
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) return;
        fwd.Normalize();

        Gizmos.color = Color.yellow;
        float half = arcAngle * 0.5f;
        Vector3 left = Quaternion.AngleAxis(-half, Vector3.up) * fwd * reach;
        Vector3 right = Quaternion.AngleAxis(half, Vector3.up) * fwd * reach;
        Gizmos.DrawLine(origin, origin + left);
        Gizmos.DrawLine(origin, origin + right);

        Vector3 prev = origin + left;
        for (int i = 1; i <= 12; i++)
        {
            Vector3 p = origin + Quaternion.AngleAxis(-half + arcAngle * i / 12f, Vector3.up) * fwd * reach;
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
    }
}