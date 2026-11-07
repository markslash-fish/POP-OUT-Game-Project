using UnityEngine;

public class AnimatedBodyMover : MonoBehaviour
{
    public float moveSpeed = 1.5f;
    public float turnSpeed = 90f;

    Animator animator;
    float forwardInput;
    float turnInput;
    public bool holding;              // tick this to test in the Inspector
    public float holdBlendSpeed = 8f; // how fast the arms blend in and out
    float holdWeight;
    public float throwDuration = 0.6f;  // set to your Throw clip's length in seconds
    float throwTimer;
    bool wasHolding;

    [Header("Arm drive")]
    public ConfigurableJoint armL;    // Arm.L joint on Physical_Body
    public ConfigurableJoint armR;    // Arm.R joint on Physical_Body
    public float looseSpring = 25f;
    public float looseDamper = 8f;
    public float holdSpring = 3000f;
    public float holdDamper = 150f;

    void Awake()
    {
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        forwardInput = Input.GetKey(KeyCode.W) ? 1f : 0f;
        turnInput = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);

        animator.SetFloat("Speed", forwardInput);

        if (Input.GetKeyDown(KeyCode.Space)) animator.SetTrigger("Swing");
        if (Input.GetKeyDown(KeyCode.E)) animator.SetTrigger("Throw");
        holding = Input.GetMouseButton(1);

        // releasing right click = throw
        if (wasHolding && !holding)
        {
            animator.SetTrigger("Throw");
            throwTimer = throwDuration;
        }
        wasHolding = holding;
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

    void FixedUpdate()
    {
        transform.Rotate(0f, turnInput * turnSpeed * Time.fixedDeltaTime, 0f);
        transform.position += transform.forward * forwardInput * moveSpeed * Time.fixedDeltaTime;
    }

    void SetArmDrive(ConfigurableJoint j, float spring, float damper)
    {
        if (j == null) return;
        var d = j.slerpDrive;
        d.positionSpring = spring;
        d.positionDamper = damper;
        j.slerpDrive = d;
    }
}