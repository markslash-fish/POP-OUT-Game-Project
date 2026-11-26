using UnityEngine;
using UnityEngine.EventSystems;

public class AttackButtonInput : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public enum AttackButton
    {
        Bomb,
        Slash
    }

    [SerializeField] private AttackButton button;
    [SerializeField] private AttackAimJoystick aimJoystick;

    public void OnPointerDown(PointerEventData eventData)
    {
        InputReader reader = InputReader.Instance;
        if (reader == null)
        {
            return;
        }

        if (button == AttackButton.Bomb)
        {
            reader.PressBomb(eventData.position);
        }
        else
        {
            reader.PressSlash(eventData.position);
        }

        aimJoystick?.BeginPointer(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        aimJoystick?.DragPointer(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        aimJoystick?.EndPointer(eventData);
    }
}
