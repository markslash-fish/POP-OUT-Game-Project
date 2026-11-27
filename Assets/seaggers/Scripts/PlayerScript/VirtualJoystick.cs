using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// On-screen joystick / skill button. Attach to the BACKGROUND image (Raycast Target ON, pivot at center).
/// Other scripts read 'Value' (x,y from -1 to 1, max length 1) and 'IsPressed'.
/// </summary>
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Header("References")]
    public RectTransform handle;            // the small stick image (optional)

    [Header("Settings")]
    [Range(0f, 0.5f)] public float deadZone = 0.1f;   // ignore tiny movements
    public float handleRange = 0.8f;                  // how far the handle can travel inside the background
    [Tooltip("0 = use the background size. Set e.g. 250 for a small skill button with a big drag area (Wild Rift style).")]
    public float dragRadius = 0f;                     // in Canvas units (reference resolution)

    public Vector2 Value { get; private set; }
    public bool IsPressed => activePointer.HasValue;

    RectTransform background;
    int? activePointer;

    void Awake()
    {
        background = GetComponent<RectTransform>();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || activePointer.HasValue) return;

        activePointer = eventData.pointerId;
        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (activePointer != eventData.pointerId) return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                background, eventData.position, eventData.pressEventCamera, out Vector2 local))
            return;

        // How far the finger must travel for Value to reach 1
        Vector2 dragSize = dragRadius > 0f ? new Vector2(dragRadius, dragRadius) : background.rect.size * 0.5f;
        Vector2 v = new Vector2(local.x / dragSize.x, local.y / dragSize.y);
        v = Vector2.ClampMagnitude(v, 1f);

        float mag = v.magnitude;
        Value = mag < deadZone ? Vector2.zero : v.normalized * ((mag - deadZone) / (1f - deadZone));

        // Handle stays inside the visible button
        if (handle != null)
            handle.anchoredPosition = v * (background.rect.size * 0.5f) * handleRange;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (activePointer == eventData.pointerId) CancelPress();
    }

    void OnDisable()
    {
        CancelPress();
    }

    internal void CancelPress()
    {
        activePointer = null;
        Value = Vector2.zero;
        if (handle != null) handle.anchoredPosition = Vector2.zero;
    }
}
