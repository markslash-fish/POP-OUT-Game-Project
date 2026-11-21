using System.Collections.Generic;
using UnityEngine;

public class ActiveRagdoll : MonoBehaviour
{
    [Header("Rig")]
    public Transform animatedRoot;    // Animated_Body (has Animator + AnimatedBodyMover)
    public Transform animatedSpine;   // Spine under Animated_Body
    public Transform physicalSpine;   // Spine under Physical_Body

    [Header("Spine drive")]
    public float posSpring = 1500f;
    public float posDamper = 150f;
    public float rotSpring = 300f;
    public float rotDamper = 30f;

    [Header("Leash")]
    public float maxLeash = 0.75f;    // max horizontal gap between the two bodies

    class Pair
    {
        public ConfigurableJoint joint;
        public Transform anim;
        public Quaternion startAnim;
    }

    List<Pair> pairs = new List<Pair>();
    Rigidbody spineRb;
    float restHeight;   // Spine height above the Animated_Body root at start
    Vector3 Vel(Rigidbody rb)
    {
#if UNITY_6000_0_OR_NEWER
        return rb.linearVelocity;
#else
        return rb.velocity;
#endif
    }

    void Awake()
    {
        spineRb = physicalSpine.GetComponent<Rigidbody>();

        var animBones = new Dictionary<string, Transform>();
        foreach (var t in animatedSpine.GetComponentsInChildren<Transform>())
            animBones[t.name] = t;

        foreach (var j in physicalSpine.GetComponentsInChildren<ConfigurableJoint>())
        {
            if (!animBones.TryGetValue(j.name, out var a))
            {
                Debug.LogWarning("No animated bone named " + j.name);
                continue;
            }
            pairs.Add(new Pair { joint = j, anim = a, startAnim = a.localRotation });
        }
        restHeight = animatedSpine.position.y - animatedRoot.position.y;
    }

    void FixedUpdate()
    {
        // follow the physical body's height so falling works
        if (animatedRoot != null)
        {
            Vector3 p = animatedRoot.position;
            p.y = physicalSpine.position.y - restHeight;
            animatedRoot.position = p;
        }
        // 1. Leash: keep the animated body close to the physical one
        if (animatedRoot != null)
        {
            Vector3 gap = physicalSpine.position - animatedSpine.position;
            gap.y = 0f;
            if (gap.magnitude > maxLeash)
                animatedRoot.position += gap.normalized * (gap.magnitude - maxLeash);
        }

        // 2. Pull the physical Spine toward the animated Spine (position)
        Vector3 posErr = animatedSpine.position - spineRb.position;
        Vector3 v = Vel(spineRb);
        spineRb.AddForce(posErr * posSpring - new Vector3(v.x, 0f, v.z) * posDamper);

        // 3. Rotate the physical Spine toward the animated rotation
        Quaternion rotErr = animatedSpine.rotation * Quaternion.Inverse(spineRb.rotation);
        rotErr.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        if (!float.IsInfinity(axis.x) && Mathf.Abs(angle) > 0.01f)
            spineRb.AddTorque(axis * (angle * Mathf.Deg2Rad * rotSpring)
                              - spineRb.angularVelocity * rotDamper);

        // 4. Joint targets: copy the animated pose onto each joint
        foreach (var p in pairs)
            p.joint.targetRotation = Quaternion.Inverse(p.anim.localRotation) * p.startAnim;
    }
}