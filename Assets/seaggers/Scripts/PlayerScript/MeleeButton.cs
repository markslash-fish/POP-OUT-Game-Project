using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// On-screen ATTACK button. Put on a UI Image (Raycast Target ON). MeleeCombat reads it with ConsumePress().
/// </summary>
public class MeleeButton : MonoBehaviour, IPointerDownHandler
{
    bool pressed;

    public void OnPointerDown(PointerEventData eventData)
    {
        pressed = true;
    }

    /// <summary>Returns true ONCE per tap.</summary>
    public bool ConsumePress()
    {
        bool p = pressed;
        pressed = false;
        return p;
    }

    void OnDisable()
    {
        pressed = false;
    }
}