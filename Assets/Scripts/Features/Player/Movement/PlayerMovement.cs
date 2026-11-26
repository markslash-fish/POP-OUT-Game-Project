using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private Rigidbody rb;
    [SerializeField] private PlayerAnimator playerAnimator;
    [SerializeField] private Transform playerBody;
    [SerializeField] private float turnSpeed = 14f;

    [Header("Movement Values")]
    [SerializeField] private float moveSpeed = 30f;
    [SerializeField] private float jumpHeight = 42f;
    [SerializeField] private float baseGravity = -75f;
    [SerializeField] private float apexGravityMultiplier = 0.5f;
    [SerializeField] private float apexThreshold = 9f;
    [SerializeField] private float fallMultiplier = 2.4f;
    [SerializeField] private float maxFallSpeed = 95f;

    public bool isGrounded;
    public Vector2 movement;

    private bool movementLocked;
    private bool previousUseGravity = true;

    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundDistance = 1f;
    [SerializeField] private LayerMask groundMask;

    private void Awake()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }
        if (playerAnimator == null)
        {
            playerAnimator = GetComponent<PlayerAnimator>();
        }

        if (playerBody == null)
        {
            playerBody = transform;
        }
    }

    private void Start()
    {
        if (InputReader.Instance != null)
        {
            InputReader.Instance.onMove += Move;
            InputReader.Instance.onJumpStarted += Jump;
        }
    }

    private void OnDisable()
    {
        if (InputReader.Instance != null)
        {
            InputReader.Instance.onMove -= Move;
            InputReader.Instance.onJumpStarted -= Jump;
        }
    }

    private void Update()
    {
        isGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask);
    }

    public bool MovementLocked => movementLocked;

    public void SetMovementLocked(bool locked)
    {
        if (movementLocked == locked)
        {
            return;
        }

        movementLocked = locked;

        if (locked)
        {
            movement = Vector2.zero;

            if (rb != null)
            {
                previousUseGravity = rb.useGravity;
                rb.useGravity = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            return;
        }

        if (rb != null)
        {
            rb.useGravity = previousUseGravity;
        }
    }

    public void FaceDirection(Vector3 direction)
    {
        direction.y = 0f;

        if (playerBody == null || direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        playerBody.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private void FixedUpdate()
    {
        if (movementLocked)
        {
            return;
        }

        Vector3 moveDir = GetCameraRelativeDirection(movement);
        Vector3 moveVelocity = moveDir * moveSpeed;

        float verticalVelocity = rb.linearVelocity.y;

        float apexFactor = 1f - Mathf.Clamp01(Mathf.Abs(verticalVelocity) / apexThreshold);
        float gravityScale = verticalVelocity < 0f ? fallMultiplier : 1f;
        gravityScale = Mathf.Lerp(gravityScale, apexGravityMultiplier, apexFactor);

        verticalVelocity += baseGravity * gravityScale * Time.fixedDeltaTime;
        verticalVelocity = Mathf.Max(verticalVelocity, -Mathf.Abs(maxFallSpeed));

        rb.linearVelocity = new Vector3(moveVelocity.x, verticalVelocity, moveVelocity.z);

        if (moveDir.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
            playerBody.rotation = Quaternion.Slerp(playerBody.rotation, targetRot, turnSpeed * Time.fixedDeltaTime);
        }
    }

    private Vector3 GetCameraRelativeDirection(Vector2 input)
    {
        if (input.sqrMagnitude < 0.0001f)
        {
            return Vector3.zero;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            return new Vector3(input.x, 0f, input.y).normalized;
        }

        Vector3 camForward = cam.transform.forward;
        Vector3 camRight = cam.transform.right;
        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        Vector3 dir = camForward * input.y + camRight * input.x;
        return dir.sqrMagnitude > 1f ? dir.normalized : dir;
    }

    private void Move(Vector2 input)
    {
        if (movementLocked)
        {
            return;
        }

        movement = input;
    }

    private void Jump()
    {
        if (movementLocked || !isGrounded) return;

        rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpHeight, rb.linearVelocity.z);
        isGrounded = false;
        playerAnimator.PlayJumpAnim();
        Debug.Log("Player Movement: Player Succesfully Jumped.");
    }
}