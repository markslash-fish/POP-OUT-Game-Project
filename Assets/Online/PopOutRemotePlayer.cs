using System.Collections.Generic;
using SpacetimeDB.Types;
using UnityEngine;

internal sealed class PopOutRemotePlayer
{
    const float FollowRate = 15f;
    const float TeleportDistance = 4f;
    static readonly int SpeedParameter = Animator.StringToHash("Speed");

    readonly ActiveRagdoll rig;
    readonly Animator animator;
    readonly Thrower thrower;
    readonly MeleeCombat melee;
    readonly List<(Transform physical, Transform animated)> bones = new();
    readonly Collider[] colliders;

    Vector3 targetPosition;
    float targetYaw;
    float targetSpeed;
    uint deaths;
    bool hasPose;
    bool snap;

    public Health Health { get; }
    public bool HasPose => hasPose;

    public PopOutRemotePlayer(GameObject clone, RoomPlayerState row)
    {
        Health = clone.GetComponent<Health>();
        Health.playerName = row.Name;
        Health.RoomSlot = row.Slot;
        rig = clone.GetComponent<ActiveRagdoll>();
        thrower = clone.GetComponent<Thrower>();
        melee = clone.GetComponentInChildren<MeleeCombat>(true);
        animator = rig.animatedRoot.GetComponent<Animator>();
        colliders = clone.GetComponentsInChildren<Collider>(true);

        rig.enabled = false;
        thrower.enabled = false;
        clone.GetComponent<RespawnSystem>().enabled = false;
        clone.GetComponent<RingOutDetector>().enabled = false;
        clone.GetComponent<PlayerJump>().enabled = false;
        rig.animatedRoot.GetComponent<AnimatedBodyMover>().enabled = false;
        foreach (var toggle in clone.GetComponentsInChildren<LegGroundToggle>(true)) toggle.enabled = false;
        foreach (Rigidbody body in clone.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;

        var animatedBones = new Dictionary<string, Transform>();
        foreach (Transform bone in rig.animatedSpine.GetComponentsInChildren<Transform>(true))
            animatedBones[bone.name] = bone;
        foreach (Transform bone in rig.physicalSpine.GetComponentsInChildren<Transform>(true))
            if (animatedBones.TryGetValue(bone.name, out Transform source)) bones.Add((bone, source));

        // Keep replicas out of sight until their first uploaded pose.
        if (!row.HasPose) clone.transform.position += Vector3.down * 100f;
        clone.transform.SetParent(null, true);
        clone.GetComponent<PlayerColorIdentity>().SetColor((PlayerColor)row.Color);
    }

    public void IgnoreCollisionWith(Health local)
    {
        foreach (Collider localCollider in local.GetComponentsInChildren<Collider>())
            foreach (Collider remoteCollider in colliders)
                Physics.IgnoreCollision(localCollider, remoteCollider);
    }

    public void Apply(RoomPlayerState row)
    {
        Health.ApplyRemoteState(row.Health * Health.maxHealth, row.Shielded);
        if (!row.HasPose) return;

        Vector3 position = new Vector3(row.X, row.Y, row.Z);
        if (!hasPose || deaths != row.Deaths)
            snap = true;
        else if (position != targetPosition)
            snap |= (position - rig.physicalSpine.position).sqrMagnitude > TeleportDistance * TeleportDistance;

        targetPosition = position;
        targetYaw = row.Yaw;
        targetSpeed = row.Speed;
        hasPose = true;
        deaths = row.Deaths;
    }

    public void ApplyPose()
    {
        if (!hasPose) return;
        float weight = snap ? 1f : 1f - Mathf.Exp(-FollowRate * Time.deltaTime);
        Vector3 spinePosition = Vector3.Lerp(rig.physicalSpine.position, targetPosition, weight);
        float yaw = Mathf.LerpAngle(rig.animatedRoot.eulerAngles.y, targetYaw, weight);
        rig.animatedRoot.rotation = Quaternion.Euler(0f, yaw, 0f);
        rig.animatedRoot.position += spinePosition - rig.animatedSpine.position;
        animator.SetFloat(SpeedParameter, targetSpeed, 0.1f, Time.deltaTime);

        // Preserve the animated joint rotation and keep the physical rig's skinned mesh.
        foreach (var (physical, animated) in bones)
        {
            animated.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
            physical.SetPositionAndRotation(position, rotation);
        }
        snap = false;
    }

    public void Throw(Vector3 position, Vector3 velocity)
    {
        animator.SetTrigger("Throw");
        thrower.ReplayThrow(position, velocity);
    }

    public void Swing(Vector3 position, float yaw)
    {
        melee.ReplaySwing(position, yaw);
    }
}
