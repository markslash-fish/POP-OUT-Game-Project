using UnityEngine;
using UnityEngine.InputSystem;

public class AnimatedBodyMover : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 1.5f;
    public float turnSpeed = 720f;          // degrees/second
    public VirtualJoystick joystick;        // LEFT stick: movement
    public VirtualJoystick aimJoystick;     // MOBILE: RIGHT stick: aim + throw (optional on PC)
    public Transform cameraTransform;       // leave empty = uses Camera.main
    public StatusEffectSystem status;       // the player's StatusEffectSystem (stun / disarm / slow)

    Animator animator;
    Vector2 moveInput;                      // x = left/right, y = up/down on screen (max length 1)
    const float TurnBlendTime = 0.065f;
    float turnVelocity;
    internal float YawAngularSpeed { get; private set; }

    public bool holding;                    // true while aiming (mouse right-click OR aim stick)
    public float holdBlendSpeed = 8f;       // how fast the arms blend in and out
    float holdWeight;
    public float throwDuration = 0.6f;      // set to your Throw clip's length in seconds
    float throwTimer;

    [Header("Arm drive")]
    public ConfigurableJoint armL;          // Arm.L joint on Physical_Body
    public ConfigurableJoint armR;          // Arm.R joint on Physical_Body
    public float looseSpring = 25f;
    public float looseDamper = 8f;
    public float holdSpring = 3000f;
    public float holdDamper = 150f;

    void Awake()
    {
        animator = GetComponent<Animator>();
    }

    void Start()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    void Update()
    {
        ReadMoveInput();

        animator.SetFloat("Speed", moveInput.magnitude, 0.1f, Time.deltaTime);

        bool canInput = MatchManager.InputEnabled && !IsStunned();   // false during countdown / after the match / while stunned
        bool canAct = canInput && !IsDisarmed();                     // disarmed players cannot hold a balloon
        // Swing (melee) is handled by MeleeCombat now
        if (canInput && Input.GetKeyDown(KeyCode.E)) PlayThrow();

        // MOBILE: aim stick counts as "holding" too
        bool mouseHeld = !Application.isMobilePlatform && Mouse.current?.rightButton.isPressed == true;
        holding = canAct && (mouseHeld || (aimJoystick != null && aimJoystick.IsPressed));

        throwTimer -= Time.deltaTime;

        holdWeight = Mathf.MoveTowards(holdWeight, holding ? 1f : 0f, holdBlendSpeed * Time.deltaTime);
        animator.SetLayerWeight(1, holdWeight);

        // arms stay firm while holding and during the throw
        float firm = throwTimer > 0f ? 1f : holdWeight;
        float spring = Mathf.Lerp(looseSpring, holdSpring, firm);
        float damper = Mathf.Lerp(looseDamper, holdDamper, firm);
        SetArmDrive(armL, spring, damper);
        SetArmDrive(armR, spring, damper);
    }

    internal void PlayThrow()
    {
        animator.SetTrigger("Throw");
        throwTimer = throwDuration;
    }

    void ReadMoveInput()
    {
        if (!MatchManager.InputEnabled || IsStunned()) { moveInput = Vector2.zero; return; }

        Vector2 input = Vector2.zero;

        if (Input.GetKey(KeyCode.W)) input.y += 1f;
        if (Input.GetKey(KeyCode.S)) input.y -= 1f;
        if (Input.GetKey(KeyCode.D)) input.x += 1f;
        if (Input.GetKey(KeyCode.A)) input.x -= 1f;

        if (joystick != null) input += joystick.Value;

        moveInput = Vector2.ClampMagnitude(input, 1f);
    }

    void FixedUpdate()
    {
        Vector3 moveDir = ToWorld(moveInput);

        // MOBILE: while the aim stick is held, face where the stick points (move stick still lets you walk)
        Vector3 aimDir = (MatchManager.InputEnabled && !IsStunned() && aimJoystick != null && aimJoystick.IsPressed) ? ToWorld(aimJoystick.Value) : Vector3.zero;
        Vector3 faceDir = aimDir.sqrMagnitude > 0.0001f ? aimDir : moveDir;
        YawAngularSpeed = 0f;

        if (faceDir.sqrMagnitude > 0.0001f)
        {
            float yaw = transform.eulerAngles.y;
            float targetYaw = Mathf.Atan2(faceDir.x, faceDir.z) * Mathf.Rad2Deg;
            float yawError = Mathf.DeltaAngle(yaw, targetYaw);
            if (yawError * turnVelocity <= 0f) turnVelocity = 0f;

            float step = Time.fixedDeltaTime;
            float nextYaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref turnVelocity, TurnBlendTime, turnSpeed, step);
            transform.rotation = Quaternion.Euler(0f, nextYaw, 0f);
            YawAngularSpeed = Mathf.DeltaAngle(yaw, nextYaw) * Mathf.Deg2Rad / step;
        }
        else turnVelocity = 0f;

        if (moveDir.sqrMagnitude > 0.0001f)
            transform.position += moveDir.normalized * moveInput.magnitude * moveSpeed * SpeedMultiplier() * Time.fixedDeltaTime;
    }

    // Screen-style 2D input -> flat world direction relative to the camera
    public Vector3 ToWorld(Vector2 input)
    {
        if (input.sqrMagnitude < 0.0001f) return Vector3.zero;

        Vector3 camForward = Vector3.forward;
        Vector3 camRight = Vector3.right;
        if (cameraTransform != null)
        {
            camForward = cameraTransform.forward; camForward.y = 0f; camForward.Normalize();
            camRight = cameraTransform.right; camRight.y = 0f; camRight.Normalize();
        }
        return camRight * input.x + camForward * input.y;
    }

    // ---- status effects (Stun / Disarm / Slow) ----
    bool IsStunned() { return status != null && status.IsStunned; }
    bool IsDisarmed() { return status != null && status.IsDisarmed; }
    float SpeedMultiplier() { return status != null ? status.SpeedMultiplier : 1f; }

    void SetArmDrive(ConfigurableJoint j, float spring, float damper)
    {
        if (j == null) return;
        var d = j.slerpDrive;
        d.positionSpring = spring;
        d.positionDamper = damper;
        j.slerpDrive = d;
    }
}
