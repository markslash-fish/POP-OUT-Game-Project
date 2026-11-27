using UnityEngine;

public class RemotePlayerView : MonoBehaviour
{
    private const uint MovingBit = 1u;
    private const uint GroundedBit = 2u;
    private const uint AimingBit = 4u;
    private const uint ThrowingBit = 8u;
    private const uint JumpBit = 16u;

    [SerializeField] private float positionLerpSpeed = 16f;
    [SerializeField] private float rotationLerpSpeed = 14f;

    private Vector3 targetPosition;
    private Quaternion targetRotation = Quaternion.identity;
    private PlayerMovement playerMovement;
    private PlayerAnimator playerAnimator;
    private uint previousAnimation;
    private bool initialized;

    private void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
        playerAnimator = GetComponent<PlayerAnimator>();
        targetPosition = transform.position;
        targetRotation = transform.rotation;
    }

    private void Update()
    {
        transform.position = Vector3.Lerp(transform.position, targetPosition, 1f - Mathf.Exp(-positionLerpSpeed * Time.deltaTime));
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 1f - Mathf.Exp(-rotationLerpSpeed * Time.deltaTime));
    }

    public void Apply(Vector3 position, float yaw, uint animation, bool immediate = false)
    {
        targetPosition = position;
        targetRotation = Quaternion.Euler(0f, yaw, 0f);

        if (immediate)
        {
            transform.position = targetPosition;
            transform.rotation = targetRotation;
        }

        ApplyAnimation(animation);
        previousAnimation = animation;
        initialized = true;
    }

    private void ApplyAnimation(uint animation)
    {
        if (playerMovement != null)
        {
            playerMovement.movement = (animation & MovingBit) != 0 ? new Vector2(1f, 0f) : Vector2.zero;
            playerMovement.isGrounded = (animation & GroundedBit) != 0;
        }

        if (playerAnimator == null)
        {
            return;
        }

        bool aiming = (animation & AimingBit) != 0;
        bool throwing = (animation & ThrowingBit) != 0;
        bool jumping = (animation & JumpBit) != 0;

        if (jumping && (previousAnimation & JumpBit) == 0)
        {
            playerAnimator.PlayJumpAnim();
        }

        if (initialized && aiming && (previousAnimation & AimingBit) == 0)
        {
            playerAnimator.SetAiming(true);
        }

        if (initialized && throwing && (previousAnimation & ThrowingBit) == 0)
        {
            playerAnimator.PlayThrow();
        }

        if (!aiming && !throwing && ((previousAnimation & (AimingBit | ThrowingBit)) != 0))
        {
            playerAnimator.StopAiming();
        }
    }
}
