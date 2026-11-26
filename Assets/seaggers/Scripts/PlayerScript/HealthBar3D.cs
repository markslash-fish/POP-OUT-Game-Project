using UnityEngine;
using UnityEngine.UI;

public class HealthBar3D : MonoBehaviour
{
    [Header("Player and UI")]
    public Health health;
    public Image fill;
    public Transform follow;

    [Header("Placement")]
    public bool followPlayer = true;
    public Camera viewCamera;
    public float sideOffset = 0.9f;
    public float heightOffset = 0.1f;

    Health subscribedHealth;

    void OnEnable()
    {
        subscribedHealth = health;
        if (subscribedHealth == null) return;

        subscribedHealth.HealthChanged += Refresh;
        Refresh(subscribedHealth);
    }

    void Start()
    {
        if (subscribedHealth != null) Refresh(subscribedHealth);
    }

    void OnDisable()
    {
        if (subscribedHealth != null)
            subscribedHealth.HealthChanged -= Refresh;

        subscribedHealth = null;
    }

    void Refresh(Health player)
    {
        if (fill == null) return;

        fill.fillAmount = player.maxHealth > 0f
            ? Mathf.Clamp01(player.Current / player.maxHealth)
            : 0f;
    }

    void LateUpdate()
    {
        if (!followPlayer || subscribedHealth == null) return;

        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null) return;

        Transform view = viewCamera.transform;
        Vector3 anchor = follow != null ? follow.position : subscribedHealth.Position;
        Vector3 position = anchor + view.right * sideOffset + view.up * heightOffset;

        transform.SetPositionAndRotation(position, view.rotation);
    }
}