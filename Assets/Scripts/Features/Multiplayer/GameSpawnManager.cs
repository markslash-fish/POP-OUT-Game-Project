using System.Collections.Generic;
using UnityEngine;

public static class GameSpawnManager
{
    public const int PlayerLimit = 3;

    private static readonly string[] SpawnPointNames =
    {
        "Player Spawner",
        "Player Spawner (1)",
        "Player Spawner (2)",
        "Player Spawner (3)",
        "Player Spawner (4)",
        "Player Spawner (5)"
    };

    private static readonly List<Transform> spawnPoints = new();

    public static void RefreshSpawnPoints()
    {
        spawnPoints.Clear();

        foreach (string spawnPointName in SpawnPointNames)
        {
            GameObject spawnPoint = GameObject.Find(spawnPointName);
            if (spawnPoint != null)
            {
                spawnPoints.Add(spawnPoint.transform);
            }
        }
    }

    public static Transform GetSpawnPoint(int index)
    {
        if (spawnPoints.Count == 0)
        {
            RefreshSpawnPoints();
        }

        if (spawnPoints.Count == 0)
        {
            return null;
        }

        return spawnPoints[index % spawnPoints.Count];
    }

    public static void Place(Transform target, int index)
    {
        Transform spawnPoint = GetSpawnPoint(index);
        if (target == null || spawnPoint == null)
        {
            return;
        }

        Rigidbody body = target.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.position = spawnPoint.position;
            body.rotation = spawnPoint.rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            return;
        }

        target.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
    }
}
