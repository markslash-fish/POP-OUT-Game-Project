using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

public class AttackAimJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [SerializeField] private RectTransform handle;
    [SerializeField] private float handleRadius = 110f;
    [SerializeField] private int indicatorSegments = 48;
    [SerializeField] private float indicatorWidth = 0.35f;

    private RectTransform rectTransform;
    private LineRenderer indicator;
    private Material indicatorMaterial;
    private bool dragging;
    private Vector2 dragStartLocal;

    public UnityAction<Vector2> onReleased;

    public Vector2 CurrentAim => handle == null || handleRadius <= 0f
        ? Vector2.zero
        : Vector2.ClampMagnitude((handle.anchoredPosition + GetAnchorPointInParent(handle)) / handleRadius, 1f);

    private Camera CanvasCamera
    {
        get
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            return canvas == null ? null : canvas.worldCamera;
        }
    }

    private void Awake()
    {
        rectTransform = (RectTransform)transform;

        if (handle == null)
        {
            foreach (RectTransform child in rectTransform)
            {
                handle = child;
                break;
            }
        }
    }

    private void OnDestroy()
    {
        if (indicatorMaterial != null)
        {
            Destroy(indicatorMaterial);
        }
    }

    public void Show(Vector2 initialAim)
    {
        gameObject.SetActive(true);
        dragging = false;
        SetHandle(initialAim * handleRadius);
    }

    public void Hide()
    {
        dragging = false;
        SetHandle(Vector2.zero);

        if (indicator != null)
        {
            indicator.enabled = false;
        }

        gameObject.SetActive(false);
    }

    public Vector3 GetWorldTarget(Vector3 origin, float range, Camera aimCamera, LayerMask groundMask)
    {
        Vector2 aimScreenOffset = Vector2.zero;
        if (handle != null)
        {
            Vector2 joystickScreenPosition = RectTransformUtility.WorldToScreenPoint(CanvasCamera, rectTransform.position);
            Vector2 handleScreenPosition = RectTransformUtility.WorldToScreenPoint(CanvasCamera, handle.position);
            aimScreenOffset = handleScreenPosition - joystickScreenPosition;
        }

        Vector3 target = origin;
        if (aimScreenOffset.sqrMagnitude > 0.01f)
        {
            Vector2 playerScreenPosition = aimCamera.WorldToScreenPoint(origin);
            Ray ray = aimCamera.ScreenPointToRay(playerScreenPosition + aimScreenOffset);
            Plane plane = new Plane(Vector3.up, origin);

            if (plane.Raycast(ray, out float distance) && distance > 0f)
            {
                target = ray.GetPoint(distance);
            }
            else
            {
                target = origin + GetCameraForward(aimCamera) * range;
            }
        }
        else
        {
            target = origin + GetCameraForward(aimCamera) * range;
        }

        Vector3 flatOffset = target - origin;
        flatOffset.y = 0f;
        if (flatOffset.sqrMagnitude > range * range)
        {
            target = origin + flatOffset.normalized * range;
        }

        target.y = origin.y;
        if (Physics.Raycast(new Vector3(target.x, origin.y + 30f, target.z), Vector3.down, out RaycastHit hit, 100f, groundMask, QueryTriggerInteraction.Ignore))
        {
            target.y = hit.point.y;
        }

        return target;
    }

    public void ShowIndicator(Vector3 center, float radius, Color color)
    {
        EnsureIndicator();

        indicator.enabled = true;
        indicator.startColor = color;
        indicator.endColor = color;
        indicator.startWidth = indicatorWidth;
        indicator.endWidth = indicatorWidth;
        indicator.positionCount = indicatorSegments;
        indicator.loop = true;

        float angleStep = Mathf.PI * 2f / indicatorSegments;
        for (int i = 0; i < indicatorSegments; i++)
        {
            float angle = i * angleStep;
            Vector3 point = center + new Vector3(Mathf.Cos(angle) * radius, 0.1f, Mathf.Sin(angle) * radius);
            indicator.SetPosition(i, point);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        BeginPointer(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        DragPointer(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        EndPointer(eventData);
    }

    public void BeginPointer(PointerEventData eventData)
    {
        dragging = true;
        eventData.pointerDrag = gameObject;

        if (TryGetLocalPoint(eventData.position, out Vector2 localPosition))
        {
            dragStartLocal = localPosition;
        }

        SetHandle(Vector2.zero);
    }

    public void DragPointer(PointerEventData eventData)
    {
        if (!dragging)
        {
            return;
        }

        UpdateHandle(eventData.position);
    }

    public void EndPointer(PointerEventData eventData)
    {
        if (!dragging)
        {
            return;
        }

        dragging = false;
        eventData.pointerDrag = null;
        UpdateHandle(eventData.position);
        onReleased?.Invoke(CurrentAim);
        Hide();
    }

    private void UpdateHandle(Vector2 screenPosition)
    {
        if (handle == null || handleRadius <= 0f)
        {
            return;
        }

        if (TryGetLocalPoint(screenPosition, out Vector2 localPosition))
        {
            SetHandle(localPosition - dragStartLocal);
        }
    }

    private void SetHandle(Vector2 offsetFromCenter)
    {
        if (handle == null)
        {
            return;
        }

        Vector2 clamped = Vector2.ClampMagnitude(offsetFromCenter, handleRadius);
        handle.anchoredPosition = clamped - GetAnchorPointInParent(handle);
    }

    private bool TryGetLocalPoint(Vector2 screenPosition, out Vector2 localPosition)
    {
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPosition, CanvasCamera, out localPosition);
    }

    private static Vector2 GetAnchorPointInParent(RectTransform child)
    {
        RectTransform parent = child.parent as RectTransform;
        if (parent == null)
        {
            return Vector2.zero;
        }

        Vector2 anchor = Vector2.Lerp(child.anchorMin, child.anchorMax, 0.5f);
        return parent.rect.position + Vector2.Scale(anchor, parent.rect.size);
    }

    private static Vector3 GetCameraForward(Camera camera)
    {
        Vector3 forward = camera.transform.forward;
        forward.y = 0f;
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
    }

    private void EnsureIndicator()
    {
        if (indicator != null)
        {
            return;
        }

        GameObject indicatorObject = new GameObject("AttackTargetIndicator");
        indicator = indicatorObject.AddComponent<LineRenderer>();
        indicator.useWorldSpace = true;
        indicator.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        indicator.receiveShadows = false;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader != null)
        {
            indicatorMaterial = new Material(shader);
            indicator.material = indicatorMaterial;
        }
    }
}
