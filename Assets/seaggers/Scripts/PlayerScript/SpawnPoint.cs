using System.Collections.Generic;
using UnityEngine;

/// <summary>Marker for a spawn location. Place it on the floor, where a player's FEET should land.</summary>
public class SpawnPoint : MonoBehaviour
{
    public static readonly List<SpawnPoint> All = new List<SpawnPoint>();

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.5f, 0.5f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1f);
    }
}