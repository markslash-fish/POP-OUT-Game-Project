using UnityEngine;

public class LegGroundToggle : MonoBehaviour
{
    public Animator animator;          // the Animator on Animated_Body
    public string legsLayer = "RagdollLegs";
    public string groundLayer = "Default";
    public float walkThreshold = 0.1f;

    int legs;
    int ground;

    void Start()
    {
        legs = LayerMask.NameToLayer(legsLayer);
        ground = LayerMask.NameToLayer(groundLayer);
    }

    void FixedUpdate()
    {
        bool walking = animator.GetFloat("Speed") > walkThreshold;
        Physics.IgnoreLayerCollision(legs, ground, walking);
    }
}