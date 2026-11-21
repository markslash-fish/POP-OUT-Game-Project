using UnityEngine;

/// <summary>
/// Place ONE of these in each arena, below the floor. Anything that falls under its Y height is a ring-out.
/// </summary>
public class RingOutZone : MonoBehaviour
{
    public static RingOutZone Instance { get; private set; }
    public float KillY => transform.position.y;

    public Vector2 gizmoSize = new Vector2(60f, 60f);   // only for the red plane you see in the Scene view

    void OnEnable() { Instance = this; }
    void OnDisable() { if (Instance == this) Instance = null; }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.25f);
        Gizmos.DrawCube(transform.position, new Vector3(gizmoSize.x, 0.05f, gizmoSize.y));
        Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.8f);
        Gizmos.DrawWireCube(transform.position, new Vector3(gizmoSize.x, 0.05f, gizmoSize.y));
    }
}