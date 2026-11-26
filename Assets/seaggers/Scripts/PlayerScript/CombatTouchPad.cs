using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class CombatTouchPad : MaskableGraphic, ICanvasRaycastFilter,
    IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public VirtualJoystick bombAimStick;
    public MeleeButton meleeButton;
    public PlayerJump playerJump;

    [Min(0f)] public float dividerWidth = 6f;

    int? aimPointer;

    protected override void Start()
    {
        base.Start();
        if (Application.isPlaying) HideAim();
    }

    protected override void OnDisable()
    {
        if (Application.isPlaying) HideAim();
        base.OnDisable();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left ||
            !MatchManager.InputEnabled)
            return;

        if (!TryGetPoint(eventData.position, eventData.pressEventCamera, out Vector2 point))
            return;

        if (point.y < 0f)
        {
            if (playerJump != null) playerJump.RequestJump();
            return;
        }

        if (point.x >= 0f)
        {
            if (meleeButton != null) meleeButton.OnPointerDown(eventData);
            return;
        }

        if (bombAimStick == null || aimPointer.HasValue) return;

        RectTransform stick = (RectTransform)bombAimStick.transform;
        if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                stick, eventData.position, eventData.pressEventCamera, out Vector3 center))
            return;

        stick.position = center;
        aimPointer = eventData.pointerId;
        bombAimStick.gameObject.SetActive(true);
        bombAimStick.OnPointerDown(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (aimPointer == eventData.pointerId && bombAimStick != null)
            bombAimStick.OnDrag(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (aimPointer != eventData.pointerId) return;

        if (bombAimStick != null) bombAimStick.OnPointerUp(eventData);
        HideAim();
    }

    void HideAim()
    {
        aimPointer = null;
        if (bombAimStick != null) bombAimStick.gameObject.SetActive(false);
    }

    public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
    {
        return TryGetPoint(screenPoint, eventCamera, out _);
    }

    bool TryGetPoint(Vector2 screenPoint, Camera eventCamera, out Vector2 point)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform, screenPoint, eventCamera, out point))
            return false;

        Rect rect = rectTransform.rect;
        point -= rect.center;
        float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
        float gap = Gap(radius);

        return radius > 0f && point.sqrMagnitude <= radius * radius
            && Mathf.Abs(point.y) >= gap
            && (point.y < 0f || Mathf.Abs(point.x) >= gap);
    }

    float Gap(float radius)
    {
        return Mathf.Clamp(dividerWidth * 0.5f, 0f, radius * 0.25f);
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();

        Rect rect = rectTransform.rect;
        float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
        if (radius <= 0f) return;

        Vector2 center = rect.center;
        float gap = Gap(radius);
        float edge = Mathf.Asin(gap / radius);
        float halfPi = Mathf.PI * 0.5f;

        DrawSection(mesh, center, new Vector2(-gap, gap), radius,
            halfPi + edge, Mathf.PI - edge);
        DrawSection(mesh, center, new Vector2(gap, gap), radius,
            edge, halfPi - edge);
        DrawSection(mesh, center, new Vector2(0f, -gap), radius,
            Mathf.PI + edge, 2f * Mathf.PI - edge);
    }

    void DrawSection(
        VertexHelper mesh, Vector2 center, Vector2 innerCorner,
        float radius, float startAngle, float endAngle)
    {
        const int segments = 32;
        int first = mesh.currentVertCount;
        mesh.AddVert(center + innerCorner, color, Vector2.zero);

        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Lerp(startAngle, endAngle, i / (float)segments);
            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            mesh.AddVert(center + offset, color, Vector2.zero);

            if (i > 0) mesh.AddTriangle(first, first + i + 1, first + i);
        }
    }
}