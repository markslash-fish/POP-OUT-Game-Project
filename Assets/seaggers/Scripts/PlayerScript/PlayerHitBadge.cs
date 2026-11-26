using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHitBadges : MonoBehaviour
{
    public Health target;
    public Transform head;
    public Camera viewCamera;
    public Image[] slots = new Image[5];

    [Min(0f)] public float heightAboveHead = 0.45f;
    [Min(0.1f)] public float visibleSeconds = 4f;

    struct Badge
    {
        public Health attacker;
        public Color color;
        public float expiresAt;
    }

    readonly List<Badge> badges = new List<Badge>();
    MatchManager match;

    void Awake()
    {
        match = FindFirstObjectByType<MatchManager>();
    }

    void OnEnable()
    {
        Health.AnyDamaged += OnDamage;
        if (target != null) target.Died += OnTargetDied;
        if (match != null) match.StateChanged += OnMatchStateChanged;
        RefreshRow();
    }

    void OnDisable()
    {
        Health.AnyDamaged -= OnDamage;
        if (target != null) target.Died -= OnTargetDied;
        if (match != null) match.StateChanged -= OnMatchStateChanged;
        ClearBadges();
    }

    void OnDamage(Health victim, Health attacker, float damage, HitType type)
    {
        if (target == null || victim != target || attacker == null || attacker == target) return;
        if (!MatchManager.InputEnabled || target.IsDead || target.Current <= 0f) return;
        if (type != HitType.BalloonCenter && type != HitType.BalloonOuter) return;
        if (slots == null || slots.Length == 0) return;

        var identity = attacker.GetComponent<PlayerColorIdentity>();
        if (identity == null) return;

        badges.RemoveAll(IsExpired);
        int index = badges.FindIndex(badge => badge.attacker == attacker);
        var updated = new Badge
        {
            attacker = attacker,
            color = PlayerColorInfo.ToColor(identity.CurrentColor),
            expiresAt = Time.time + visibleSeconds
        };

        if (index >= 0) badges[index] = updated;
        else if (badges.Count < slots.Length) badges.Add(updated);

        RefreshRow();
    }

    void OnTargetDied(Health victim, Health killer) => ClearBadges();

    void OnMatchStateChanged(MatchState state) => ClearBadges();

    void ClearBadges()
    {
        badges.Clear();
        RefreshRow();
    }

    static bool IsExpired(Badge badge) => Time.time >= badge.expiresAt;

    void RefreshRow()
    {
        if (slots == null) return;

        for (int i = 0; i < slots.Length; i++)
        {
            var image = slots[i];
            if (image == null) continue;

            bool visible = i < badges.Count;
            if (visible) image.color = badges[i].color;
            image.gameObject.SetActive(visible);
        }
    }

    void LateUpdate()
    {
        if (badges.RemoveAll(IsExpired) > 0) RefreshRow();
        if (badges.Count == 0 || target == null) return;

        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null) return;

        Transform view = viewCamera.transform;
        Vector3 point = head != null ? head.position : target.Position;
        transform.SetPositionAndRotation(
            point + view.up * heightAboveHead,
            view.rotation
        );
    }
}