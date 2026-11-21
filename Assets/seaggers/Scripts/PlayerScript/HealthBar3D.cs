using UnityEngine;

/// <summary>
/// Simple health bar above a player. Put on an EMPTY GameObject at the scene root
/// (NOT inside the ragdoll, or it would rotate with it). It builds its own quads.
/// </summary>
public class HealthBar3D : MonoBehaviour
{
    public Health health;
    public Transform follow;                          // e.g. Spine on Physical_Body
    public Vector3 offset = new Vector3(0f, 1.4f, 0f);
    public Vector2 size = new Vector2(1f, 0.12f);
    public Color fullColor = new Color(0.3f, 0.9f, 0.3f);
    public Color lowColor = new Color(0.95f, 0.25f, 0.2f);

    Transform fill;
    Renderer fillRenderer;
    Camera cam;

    void Awake()
    {
        MakeQuad("Back", new Color(0f, 0f, 0f, 0.6f), 0f, out _);
        fill = MakeQuad("Fill", fullColor, -0.001f, out fillRenderer);
    }

    void Start()
    {
        cam = Camera.main;
        if (health != null)
        {
            health.HealthChanged += Refresh;
            Refresh(health);
        }
    }

    void OnDestroy()
    {
        if (health != null) health.HealthChanged -= Refresh;
    }

    Transform MakeQuad(string quadName, Color color, float z, out Renderer rend)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        q.name = quadName;
        q.transform.SetParent(transform, false);
        q.transform.localPosition = new Vector3(0f, 0f, z);
        q.transform.localScale = new Vector3(size.x, size.y, 1f);

        rend = q.GetComponent<Renderer>();
        rend.material = new Material(Shader.Find("Sprites/Default")) { color = color };
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        return q.transform;
    }

    void Refresh(Health h)
    {
        float pct = Mathf.Clamp01(h.Current / h.maxHealth);
        fill.localScale = new Vector3(size.x * pct, size.y, 1f);
        fill.localPosition = new Vector3(-size.x * (1f - pct) * 0.5f, 0f, -0.001f);
        fillRenderer.material.color = Color.Lerp(lowColor, fullColor, pct);
    }

    void LateUpdate()
    {
        if (follow == null) return;
        transform.position = follow.position + offset;      // world-space offset: stays above even if ragdoll tilts
        if (cam != null) transform.rotation = cam.transform.rotation;   // always faces the camera
    }
}