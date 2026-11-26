using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class InputReader : MonoBehaviour, InputSystem_Actions.IPlayerActions
{
    public static InputReader Instance;
    [SerializeField] InputActionAsset inputActionAsset;
    
    public UnityAction<Vector2> onMove;
    public UnityAction onJumpStarted;
    public UnityAction onJumpHold;
    public UnityAction onJumpFinished;


    public UnityAction<Vector2> onThrowBombAimStarted;
    public UnityAction<Vector2> onThrowBombHold;
    public UnityAction<Vector2> onThrowBombReleased;

    public UnityAction<Vector2> onSlashAimStarted;
    public UnityAction<Vector2> onSlashHold;
    public UnityAction<Vector2> onSlashReleased;

    public UnityAction onCancel;

    [Header("Hold Boolean")] 
    public bool isHoldingJump;
    public bool isHoldingThrow;
    public bool isHoldingSlash;
    [SerializeField] private InputAction moveAction;
    [SerializeField] private InputAction jumpAction;
    [SerializeField] private InputAction throwAction;
    [SerializeField] private InputAction slashAction;

    [SerializeField] private InputAction cancelAction;

    void Awake()
    {
        if(Instance != null && Instance != this )
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    void OnEnable()
    {
         moveAction = inputActionAsset.FindAction("Move");
        jumpAction = inputActionAsset.FindAction("Jump");
        throwAction = inputActionAsset.FindAction("Throw Bomb");
        slashAction = inputActionAsset.FindAction("Slash");
        cancelAction = inputActionAsset.FindAction("Cancel");
        moveAction.started += OnMove;
        moveAction.performed += OnMove;
        moveAction.canceled += OnMove;

       jumpAction.started += OnJump;
       jumpAction.performed += OnJump;
       jumpAction.canceled += OnJump;

       throwAction.started += OnThrowBomb;
       throwAction.performed += OnThrowBomb;
       throwAction.canceled += OnThrowBomb;

       slashAction.started += OnSlash;
       slashAction.performed += OnSlash;
       slashAction.canceled += OnSlash;

       cancelAction.started += OnCancel;
       cancelAction.performed += OnCancel;
       cancelAction.canceled += OnCancel;

       

        moveAction.Enable();
        jumpAction.Enable();
        throwAction.Enable();
        slashAction.Enable();
        cancelAction.Enable();
    }

    
    void OnDisable()
    {
        moveAction.started -= OnMove;
        moveAction.performed -= OnMove;
        moveAction.canceled -= OnMove;

        jumpAction.started -= OnJump;
        jumpAction.performed -= OnJump;
        jumpAction.canceled -= OnJump;

        throwAction.started -= OnThrowBomb;
        throwAction.performed -= OnThrowBomb;
        throwAction.canceled -= OnThrowBomb;

        slashAction.started -= OnSlash;
        slashAction.performed -= OnSlash;
        slashAction.canceled -= OnSlash;

        cancelAction.started -= OnCancel;
        cancelAction.performed -= OnCancel;
        cancelAction.canceled -= OnCancel;

        moveAction.Disable();
        jumpAction.Disable();
        throwAction.Disable();
        slashAction.Disable();
        cancelAction.Disable();
    }

    public void OnJump(InputAction.CallbackContext context)
    {
       if(context.started)
        {
            onJumpStarted?.Invoke();
        }
       else if(context.performed)
        {
            onJumpHold?.Invoke();
            isHoldingJump = true;
        }
       else if(context.canceled)
        {
            onJumpFinished?.Invoke();
            isHoldingJump = false;
        }
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        onMove?.Invoke(context.ReadValue<Vector2>());


    }

    public void OnThrowBomb(InputAction.CallbackContext context)
    {
        if(context.started)
        {
            onThrowBombAimStarted?.Invoke(Vector2.zero);
        }

        if(context.started || context.performed)
        {
            onThrowBombHold?.Invoke(context.ReadValue<Vector2>());
            isHoldingThrow = true;
        }
        else if(context.canceled)
        {
            onThrowBombReleased?.Invoke(context.ReadValue<Vector2>());
            isHoldingThrow = false;
        }
    }
    
    public void OnSlash(InputAction.CallbackContext context)
    {
        if(context.started)
        {
            onSlashAimStarted?.Invoke(Vector2.zero);
        }

        if(context.started || context.performed)
        {
            onSlashHold?.Invoke(context.ReadValue<Vector2>());
            isHoldingSlash = true;
        }
        else if(context.canceled)
        {
            onSlashReleased?.Invoke(context.ReadValue<Vector2>());
            isHoldingSlash = false; 
        }
    }

    public void PressBomb(Vector2 pointerPosition)
    {
        onThrowBombAimStarted?.Invoke(pointerPosition);
        onThrowBombHold?.Invoke(Vector2.zero);
        isHoldingThrow = true;
    }

    public void ReleaseBomb(Vector2 aim)
    {
        onThrowBombReleased?.Invoke(aim);
        isHoldingThrow = false;
    }

    public void PressSlash(Vector2 pointerPosition)
    {
        onSlashAimStarted?.Invoke(pointerPosition);
        onSlashHold?.Invoke(Vector2.zero);
        isHoldingSlash = true;
    }

    public void ReleaseSlash(Vector2 aim)
    {
        onSlashReleased?.Invoke(aim);
        isHoldingSlash = false;
    }

    public void OnCancel(InputAction.CallbackContext context)
    {
        if(context.canceled)
        {
            onCancel?.Invoke();
            
        }
    }
}
