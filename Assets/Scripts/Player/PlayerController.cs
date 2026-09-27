using UnityEngine;

public class PlayerController : MonoBehaviour
{
   

    [SerializeField] private float movementSpeed = 3f;
    [SerializeField] private float jumpHeight = 3f;
    [SerializeField] private bool isGrounded;

    Rigidbody rb;


    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.03f;
    [SerializeField] private LayerMask groundLayer;
   
    public Vector2 movement;
    void Start()
    {
        
        if(GameInput.instance != null)
        {
            GameInput.instance.onMove += PlayerMove;
            GameInput.instance.onJump += PlayerJump;
        }
        else
        {
            Debug.LogError("PlayerController: GameInput instance not found in scene!");
        }

    }
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        
    }
    private void OnEnable()
    {
       
    }

    // Update is called once per frame
    void Update()
    {
        isGrounded = Physics.CheckSphere(groundCheck.position, groundCheckRadius, groundLayer);
    }
    private void FixedUpdate()
    {
        Vector3 targetDir = (transform.right * movement.x + transform.forward * movement.y).normalized;
        Vector3 targetVelocity = targetDir * movementSpeed;

        rb.linearVelocity = new Vector3(targetVelocity.x, rb.linearVelocity.y, targetVelocity.z);
    }

    void PlayerMove(Vector2 input)
    {
        movement = input;
    }
    void PlayerJump()
    {
        if(isGrounded)
        {
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpHeight, rb.linearVelocity.y);
        }
      


    }

}
