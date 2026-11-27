using UnityEngine;

/// <summary>
/// Lets a player be pushed. Put on the SAME object as Health.
/// While staggered, the ActiveRagdoll spring is weakened so the push is not instantly cancelled.
/// </summary>
public class KnockbackReceiver : MonoBehaviour
{
    [Header("References")]
    public Rigidbody body;              // the Spine Rigidbody on Physical_Body
    public ActiveRagdoll ragdoll;       // the ActiveRagdoll component

    [Header("Stagger")]
    [Range(0f, 1f)] public float staggerSpringScale = 0.1f; // 0.1 = springs at 10% strength during stagger
    public float staggerTime = 0.35f;   // seconds the ragdoll stays loose
    public float recoverTime = 0.25f;   // seconds to blend the springs back to normal

    float basePosSpring, basePosDamper, baseRotSpring, baseRotDamper;
    float timer;                        // counts down from (staggerTime + recoverTime)
    internal bool IsStaggered => timer > 0f;

    void Awake()
    {
        if (ragdoll != null)
        {
            basePosSpring = ragdoll.posSpring;
            basePosDamper = ragdoll.posDamper;
            baseRotSpring = ragdoll.rotSpring;
            baseRotDamper = ragdoll.rotDamper;
        }
    }

    /// <param name="direction">push direction (will be normalized)</param>
    /// <param name="speed">velocity change in meters/second (ignores mass)</param>
    public void ApplyKnockback(Vector3 direction, float speed)
    {
        if (body == null || speed <= 0f) return;

        body.AddForce(direction.normalized * speed, ForceMode.VelocityChange);
        timer = staggerTime + recoverTime;
    }

    /// <summary>Cancel any stagger and restore full spring strength (used on respawn).</summary>
    public void ResetState()
    {
        timer = 0f;
        if (ragdoll == null) return;
        ragdoll.posSpring = basePosSpring;
        ragdoll.posDamper = basePosDamper;
        ragdoll.rotSpring = baseRotSpring;
        ragdoll.rotDamper = baseRotDamper;
    }

    void FixedUpdate()
    {
        if (ragdoll == null || timer <= 0f) return;

        timer -= Time.fixedDeltaTime;

        // 1 while loose, then blends back up to 1 (full strength) during the recover time
        float strength;
        if (timer > recoverTime) strength = staggerSpringScale;
        else if (timer > 0f) strength = Mathf.Lerp(1f, staggerSpringScale, timer / recoverTime);
        else strength = 1f;

        ragdoll.posSpring = basePosSpring * strength;
        ragdoll.posDamper = basePosDamper * strength;
        ragdoll.rotSpring = baseRotSpring * strength;
        ragdoll.rotDamper = baseRotDamper * strength;
    }
}
