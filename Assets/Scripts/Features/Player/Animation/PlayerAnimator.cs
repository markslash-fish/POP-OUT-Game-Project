using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class PlayerAnimator : MonoBehaviour
{
    private static readonly int IsMoving = Animator.StringToHash("IsMoving");
    private static readonly int IsGrounded = Animator.StringToHash("isGrounded");
    private static readonly int Jump = Animator.StringToHash("Jump");
    private static readonly int IsAiming = Animator.StringToHash("IsAiming");
    private static readonly int Throw = Animator.StringToHash("Throw");

    [SerializeField] private string holdThrowLayerName = "Hold/Throw Bomb";
    [SerializeField] private float throwTransitionTimeout = 2f;
    [SerializeField] private UnityEvent throwStarted = new UnityEvent();
    [SerializeField] private UnityEvent throwFinished = new UnityEvent();

    public UnityEvent ThrowStarted => throwStarted;
    public UnityEvent ThrowFinished => throwFinished;
    public bool IsThrowAnimationActive { get; private set; }

    private Animator animator;
    private PlayerMovement playerMovement;
    private int holdThrowLayer = -1;
    private Coroutine throwRoutine;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        playerMovement = GetComponent<PlayerMovement>();

        if (animator == null)
        {
            return;
        }

        for (int i = 0; i < animator.layerCount; i++)
        {
            if (animator.GetLayerName(i) == holdThrowLayerName)
            {
                holdThrowLayer = i;
                break;
            }
        }
    }

    private void Update()
    {
        if (animator == null || playerMovement == null)
        {
            return;
        }

        CheckGround();
        PlayWalkAnim();
    }

    public void PlayWalkAnim()
    {
        if (animator == null || playerMovement == null)
        {
            return;
        }

        animator.SetBool(IsMoving, playerMovement.movement.magnitude > 0f);
    }

    public void PlayJumpAnim()
    {
        if (animator == null)
        {
            return;
        }

        animator.SetTrigger(Jump);
    }

    public void CheckGround()
    {
        if (animator == null || playerMovement == null)
        {
            return;
        }

        animator.SetBool(IsGrounded, playerMovement.isGrounded);
    }

    public void SetAiming(bool aiming)
    {
        if (animator == null)
        {
            return;
        }

        if (throwRoutine != null)
        {
            StopCoroutine(throwRoutine);
            throwRoutine = null;
            FinishThrowAnimation();
        }

        animator.ResetTrigger(Throw);
        animator.SetBool(IsAiming, aiming);
        SetHoldThrowWeight(aiming ? 1f : 0f);
    }

    public void PlayThrow()
    {
        if (animator == null)
        {
            return;
        }

        if (throwRoutine != null)
        {
            StopCoroutine(throwRoutine);
        }

        animator.ResetTrigger(Throw);
        animator.SetBool(IsAiming, true);
        animator.SetTrigger(Throw);
        SetHoldThrowWeight(1f);
        IsThrowAnimationActive = true;
        throwStarted.Invoke();
        throwRoutine = StartCoroutine(ThrowSequence());
    }

    public void StopAiming()
    {
        if (animator == null)
        {
            return;
        }

        if (throwRoutine != null)
        {
            StopCoroutine(throwRoutine);
            throwRoutine = null;
            FinishThrowAnimation();
        }

        animator.SetBool(IsAiming, false);
        animator.ResetTrigger(Throw);
        SetHoldThrowWeight(0f);
    }

    private IEnumerator ThrowSequence()
    {
        if (holdThrowLayer < 0)
        {
            yield return new WaitForSeconds(throwTransitionTimeout);
            FinishThrowAnimation();
            throwRoutine = null;
            yield break;
        }

        bool enteredThrow = false;
        float elapsed = 0f;

        while (elapsed < throwTransitionTimeout)
        {
            if (animator.GetCurrentAnimatorStateInfo(holdThrowLayer).IsName("Throw"))
            {
                enteredThrow = true;
                break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        animator.SetBool(IsAiming, false);
        animator.ResetTrigger(Throw);

        if (enteredThrow)
        {
            elapsed = 0f;

            while (elapsed < throwTransitionTimeout)
            {
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(holdThrowLayer);
                if (!state.IsName("Throw") || state.normalizedTime >= 1f)
                {
                    break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        SetHoldThrowWeight(0f);
        FinishThrowAnimation();
        throwRoutine = null;
    }

    private void FinishThrowAnimation()
    {
        if (!IsThrowAnimationActive)
        {
            return;
        }

        IsThrowAnimationActive = false;
        throwFinished.Invoke();
    }

    private void SetHoldThrowWeight(float weight)
    {
        if (animator != null && holdThrowLayer >= 0)
        {
            animator.SetLayerWeight(holdThrowLayer, weight);
        }
    }
}
