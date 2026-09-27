using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class GameInput : MonoBehaviour, InputSystem.IPlayerActions
{

    public static GameInput instance { get; private set; }
    public  UnityAction<Vector2> onMove;
    public  UnityAction onJump;
    public  UnityAction onFillBalloon;
    public  UnityAction onThrowBalloon;

    public InputActionAsset inputActions;

    public InputAction moveAction;
    public InputAction jumpAction;
    public InputAction fillAction;
    public InputAction throwAction;


    public void Awake()
    {
        if(instance != null && instance != this )
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
    }
    public void OnEnable()
    {
        moveAction = inputActions.FindAction("Move");
        jumpAction = inputActions.FindAction("Jump");
        fillAction = inputActions.FindAction("Fill Balloon");
        throwAction = inputActions.FindAction("Throw Balloon");

        moveAction.started += OnMove;
        moveAction.performed += OnMove;
        moveAction.canceled += OnMove;

        jumpAction.started += OnJump;
        jumpAction.performed += OnJump;
        jumpAction.canceled += OnJump;

        fillAction.started += OnFillBalloon;
        fillAction.performed += OnFillBalloon;
        fillAction.canceled += OnFillBalloon;

        throwAction.started += OnThrowBalloon;
        throwAction.performed += OnThrowBalloon;
        throwAction.canceled += OnThrowBalloon;

        moveAction.Enable();
        jumpAction.Enable();
        fillAction.Enable();
        throwAction.Enable();
    }
    public void OnDisable()
    {
        moveAction.started -= OnMove;
        moveAction.performed -= OnMove;
        moveAction.canceled -= OnMove;

        jumpAction.started -= OnJump;
        jumpAction.performed -= OnJump;
        jumpAction.canceled -= OnJump;

        fillAction.started -= OnFillBalloon;
        fillAction.performed -= OnFillBalloon;
        fillAction.canceled -= OnFillBalloon;

        throwAction.started -= OnThrowBalloon;
        throwAction.performed -= OnThrowBalloon;
        throwAction.canceled -= OnThrowBalloon;

        moveAction.Disable();
        jumpAction.Disable();
        fillAction.Disable();
        throwAction.Disable();
    }
    void Start()
    {
        
    }

  
    void Update()
    {
        
    }

    public void OnFillBalloon(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
      if (onFillBalloon != null && context.started)
        {
            onFillBalloon?.Invoke();
        }
    }

    public void OnJump(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
      if( onJump != null && context.started)
        {
            onJump?.Invoke();
        }
    }

    public void OnMove(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        onMove?.Invoke(context.ReadValue<Vector2>());
    }

    public void OnThrowBalloon(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
      if (onThrowBalloon != null && context.started)
        {
            onThrowBalloon?.Invoke();
        }
    }


}
