using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class PlayerAttackMechanics : MonoBehaviour
{
    public enum AttackMode
    {
        None,
        Bomb,
        Slash
    }

    [Header("Health")]
    [SerializeField] private float maxHealth = 21f;
    [SerializeField] private UnityEvent<float> healthChanged = new UnityEvent<float>();
    [SerializeField] private UnityEvent died = new UnityEvent();

    [Header("Aim")]
    [SerializeField] private AttackAimJoystick aimJoystick;
    [SerializeField] private Camera aimCamera;
    [SerializeField] private LayerMask groundMask = ~0;

    [Header("Bomb")]
    [SerializeField] private GameObject bombPrefab;
    [SerializeField] private Transform leftArm;
    [SerializeField] private Transform rightArm;
    [SerializeField] private float bombHoldHeight = 0.1f;
    [SerializeField] private float bombRange = 21f;
    [SerializeField] private float bombInnerRadius = 2f;
    [SerializeField] private float bombOuterRadius = 5f;
    [SerializeField] private float bombFuse = 0.6f;
    [SerializeField] private float bombArcHeight = 3f;
    [SerializeField] private Color bombIndicatorColor = new Color(1f, 0.45f, 0.2f, 0.85f);

    [Header("Slash")]
    [SerializeField] private float slashRange = 14f;
    [SerializeField] private float slashRadius = 3f;
    [SerializeField] private float slashKnockback = 18f;
    [SerializeField] private float slashLaunch = 4f;
    [SerializeField] private Color slashIndicatorColor = new Color(0.2f, 0.8f, 1f, 0.85f);

    private float currentHealth;
    private AttackMode activeMode;
    private Vector2 currentAim;
    private Rigidbody body;
    private PlayerMovement movement;
    private PlayerAnimator animator;
    private GameObject heldBomb;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public bool IsDead { get; private set; }
    public AttackMode ActiveMode => activeMode;
    public UnityEvent<float> HealthChanged => healthChanged;
    public UnityEvent Died => died;

    private void Awake()
    {
        currentHealth = maxHealth;
        body = GetComponent<Rigidbody>();
        movement = GetComponent<PlayerMovement>();
        animator = GetComponent<PlayerAnimator>();
        ResolveArms();

        if (aimCamera == null)
        {
            aimCamera = Camera.main;
        }

        if (aimJoystick == null)
        {
            aimJoystick = FindFirstObjectByType<AttackAimJoystick>(FindObjectsInactive.Include);
        }

        healthChanged.Invoke(currentHealth);
    }

    private void Start()
    {
        if (InputReader.Instance == null)
        {
            return;
        }

        InputReader.Instance.onThrowBombAimStarted += OnBombAimStarted;
        InputReader.Instance.onThrowBombHold += OnBombHold;
        InputReader.Instance.onThrowBombReleased += OnBombReleased;
        InputReader.Instance.onSlashAimStarted += OnSlashAimStarted;
        InputReader.Instance.onSlashHold += OnSlashHold;
        InputReader.Instance.onSlashReleased += OnSlashReleased;
        InputReader.Instance.onCancel += CancelAim;
    }

    private void OnEnable()
    {
        if (aimJoystick != null)
        {
            aimJoystick.onReleased += OnAimReleased;
        }

        if (animator != null)
        {
            animator.ThrowFinished.AddListener(OnThrowAnimationFinished);
        }
    }

    private void OnDisable()
    {
        if (aimJoystick != null)
        {
            aimJoystick.onReleased -= OnAimReleased;
            aimJoystick.Hide();
        }

        DestroyHeldBomb();

        if (animator != null)
        {
            animator.ThrowFinished.RemoveListener(OnThrowAnimationFinished);
        }

        if (InputReader.Instance == null)
        {
            return;
        }

        InputReader.Instance.onThrowBombAimStarted -= OnBombAimStarted;
        InputReader.Instance.onThrowBombHold -= OnBombHold;
        InputReader.Instance.onThrowBombReleased -= OnBombReleased;
        InputReader.Instance.onSlashAimStarted -= OnSlashAimStarted;
        InputReader.Instance.onSlashHold -= OnSlashHold;
        InputReader.Instance.onSlashReleased -= OnSlashReleased;
        InputReader.Instance.onCancel -= CancelAim;
    }

    private void LateUpdate()
    {
        if (heldBomb != null)
        {
            heldBomb.transform.position = GetBombHoldPoint();
        }
    }

    private void Update()
    {
        if (IsDead || activeMode == AttackMode.None || aimJoystick == null || aimCamera == null)
        {
            return;
        }

        float range = activeMode == AttackMode.Bomb ? bombRange : slashRange;
        float radius = activeMode == AttackMode.Bomb ? bombOuterRadius : slashRadius;
        Color color = activeMode == AttackMode.Bomb ? bombIndicatorColor : slashIndicatorColor;
        Vector3 target = GetTarget(activeMode, currentAim, range);
        aimJoystick.ShowIndicator(target, radius, color);
    }

    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f)
        {
            return;
        }

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        healthChanged.Invoke(currentHealth);

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    public void Knockback(Vector3 impulse)
    {
        if (IsDead || body == null)
        {
            return;
        }

        body.AddForce(impulse, ForceMode.Impulse);
    }

    public void ApplyBombDamage(Vector3 center)
    {
        HashSet<PlayerAttackMechanics> hitPlayers = new HashSet<PlayerAttackMechanics>();

        foreach (Collider hit in Physics.OverlapSphere(center, bombOuterRadius, ~0, QueryTriggerInteraction.Collide))
        {
            PlayerAttackMechanics player = hit.GetComponentInParent<PlayerAttackMechanics>();
            if (player == null || player == this || hitPlayers.Contains(player))
            {
                continue;
            }

            float distance = Vector3.Distance(Flatten(player.transform.position), Flatten(center));
            float damage = distance <= bombInnerRadius
                ? player.maxHealth / 3f
                : player.maxHealth / 7f;

            player.TakeDamage(damage);
            hitPlayers.Add(player);
        }
    }

    public void ApplySlash(Vector3 target)
    {
        Vector3 direction = Flatten(target - transform.position);
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = transform.forward;
        }
        direction.Normalize();

        HashSet<PlayerAttackMechanics> hitPlayers = new HashSet<PlayerAttackMechanics>();

        foreach (Collider hit in Physics.OverlapSphere(target, slashRadius, ~0, QueryTriggerInteraction.Collide))
        {
            PlayerAttackMechanics player = hit.GetComponentInParent<PlayerAttackMechanics>();
            if (player == null || player == this || hitPlayers.Contains(player))
            {
                continue;
            }

            Vector3 toPlayer = Flatten(player.transform.position - transform.position);
            if (toPlayer.sqrMagnitude > 0.0001f && Vector3.Dot(toPlayer.normalized, direction) < 0.2f)
            {
                continue;
            }

            player.Knockback(direction * slashKnockback + Vector3.up * slashLaunch);
            hitPlayers.Add(player);
        }
    }

    private void OnBombAimStarted(Vector2 pointerPosition)
    {
        BeginAim(AttackMode.Bomb, Vector2.zero);
    }

    private void OnBombHold(Vector2 aim)
    {
        BeginAim(AttackMode.Bomb, aim);
    }

    private void OnSlashAimStarted(Vector2 pointerPosition)
    {
        BeginAim(AttackMode.Slash, Vector2.zero);
    }

    private void OnSlashHold(Vector2 aim)
    {
        BeginAim(AttackMode.Slash, aim);
    }

    private void BeginAim(AttackMode mode, Vector2 aim)
    {
        if (IsDead)
        {
            return;
        }

        activeMode = mode;
        currentAim = aim;

        if (aimJoystick != null)
        {
            aimJoystick.Show(aim);
        }

        if (mode == AttackMode.Bomb)
        {
            if (heldBomb == null && bombPrefab != null)
            {
                heldBomb = Instantiate(bombPrefab, GetBombHoldPoint(), Quaternion.identity);
                PrepareBombVisual(heldBomb);
            }

            if (animator != null)
            {
                animator.SetAiming(true);
            }
        }
    }

    private void OnThrowAnimationFinished()
    {
        if (movement != null)
        {
            movement.SetMovementLocked(false);
        }
    }

    private void OnAimReleased(Vector2 aim)
    {
        if (IsDead || activeMode == AttackMode.None)
        {
            return;
        }

        AttackMode mode = activeMode;
        InputReader reader = InputReader.Instance;

        if (reader == null)
        {
            CompleteAttack(mode, aim);
            return;
        }

        if (mode == AttackMode.Bomb)
        {
            reader.ReleaseBomb(aim);
        }
        else
        {
            reader.ReleaseSlash(aim);
        }
    }

    private void OnBombReleased(Vector2 aim)
    {
        if (activeMode == AttackMode.Bomb)
        {
            CompleteAttack(AttackMode.Bomb, aim);
        }
    }

    private void OnSlashReleased(Vector2 aim)
    {
        if (activeMode == AttackMode.Slash)
        {
            CompleteAttack(AttackMode.Slash, aim);
        }
    }

    private void CancelAim()
    {
        activeMode = AttackMode.None;
        currentAim = Vector2.zero;

        if (aimJoystick != null)
        {
            aimJoystick.Hide();
        }

        if (animator != null)
        {
            animator.StopAiming();
        }

        DestroyHeldBomb();
    }

    private void CompleteAttack(AttackMode mode, Vector2 aim)
    {
        if (IsDead || mode == AttackMode.None)
        {
            return;
        }

        currentAim = aim.sqrMagnitude > 0.0001f ? aim : currentAim;

        float range = mode == AttackMode.Bomb ? bombRange : slashRange;
        Vector3 target = GetTarget(mode, currentAim, range);

        activeMode = AttackMode.None;

        if (aimJoystick != null)
        {
            aimJoystick.Hide();
        }

        if (mode == AttackMode.Bomb)
        {
            Vector3 throwDirection = Flatten(target - transform.position);

            if (movement != null)
            {
                movement.FaceDirection(throwDirection);
            }

            if (animator != null)
            {
                if (movement != null)
                {
                    movement.SetMovementLocked(true);
                }

                animator.PlayThrow();
            }

            ThrowBomb(target);
        }
        else
        {
            ApplySlash(target);
        }
    }

    private void ThrowBomb(Vector3 target)
    {
        if (bombPrefab == null && heldBomb == null)
        {
            ApplyBombDamage(target);
            return;
        }

        Vector3 spawnPoint = heldBomb != null ? heldBomb.transform.position : GetBombHoldPoint();
        GameObject bomb = heldBomb;

        if (bomb == null)
        {
            bomb = Instantiate(bombPrefab, spawnPoint, Quaternion.identity);
            PrepareBombVisual(bomb);
        }

        heldBomb = null;
        BombProjectile projectile = bomb.AddComponent<BombProjectile>();
        projectile.Initialize(this, spawnPoint, target, bombFuse, bombArcHeight);
    }

    private Vector3 GetBombHoldPoint()
    {
        if (leftArm != null && rightArm != null)
        {
            return (leftArm.position + rightArm.position) * 0.5f + Vector3.up * bombHoldHeight;
        }

        return transform.position + transform.forward * 0.6f + Vector3.up * 1.2f;
    }

    private void PrepareBombVisual(GameObject bomb)
    {
        Collider collider = bomb.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
        }

        Rigidbody body = bomb.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.isKinematic = true;
            body.useGravity = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        foreach (PopOnContact pop in bomb.GetComponents<PopOnContact>())
        {
            pop.enabled = false;
        }
    }

    private void DestroyHeldBomb()
    {
        if (heldBomb != null)
        {
            Destroy(heldBomb);
            heldBomb = null;
        }
    }

    private void ResolveArms()
    {
        if (leftArm != null && rightArm != null)
        {
            return;
        }

        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            string name = child.name.ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);

            if (leftArm == null && (name.Contains("leftarm") || name.EndsWith("arml") || name.StartsWith("larm") || name.EndsWith("armleft")))
            {
                leftArm = child;
            }

            if (rightArm == null && (name.Contains("rightarm") || name.EndsWith("armr") || name.StartsWith("rarm") || name.EndsWith("armright")))
            {
                rightArm = child;
            }
        }
    }

    private Vector3 GetTarget(AttackMode mode, Vector2 aim, float range)
    {
        if (aimJoystick != null && aimCamera != null)
        {
            return aimJoystick.GetWorldTarget(transform.position, range, aimCamera, groundMask);
        }

        return transform.position + GetAimDirection(aim) * range;
    }

    private Vector3 GetAimDirection(Vector2 aim)
    {
        if (aim.sqrMagnitude < 0.0001f)
        {
            return transform.forward;
        }

        Camera camera = aimCamera != null ? aimCamera : Camera.main;
        Vector3 forward = camera != null ? camera.transform.forward : transform.forward;
        Vector3 right = camera != null ? camera.transform.right : transform.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        return (forward * aim.y + right * aim.x).normalized;
    }

    private static Vector3 Flatten(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

    private void Die()
    {
        IsDead = true;
        activeMode = AttackMode.None;

        if (aimJoystick != null)
        {
            aimJoystick.Hide();
        }

        if (animator != null)
        {
            animator.StopAiming();
        }

        DestroyHeldBomb();

        if (movement != null)
        {
            movement.SetMovementLocked(false);
            movement.enabled = false;
        }

        if (animator != null)
        {
            animator.enabled = false;
        }

        died.Invoke();
    }
}
