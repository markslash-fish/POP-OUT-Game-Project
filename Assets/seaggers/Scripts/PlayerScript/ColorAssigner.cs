using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class ColorAssigner : MonoBehaviour
{
    public MatchManager match;
    public bool allowRepeats = false;

    [Header("Test tools")]
    public bool debugForceColor = false;
    public Health debugPlayer;
    public PlayerColor debugColor = PlayerColor.Red;

    void Awake()
    {
        if (match == null) match = FindFirstObjectByType<MatchManager>();
    }

    void OnEnable()
    {
        if (match != null) match.MatchStarting += AssignColors;
    }

    void OnDisable()
    {
        if (match != null) match.MatchStarting -= AssignColors;
    }

    void AssignColors()
    {
        var pool = new List<PlayerColor>(PlayerColorInfo.All);
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        var players = Health.All;
        var log = new StringBuilder("COLORS: ");

        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            var identity = player.GetComponent<PlayerColorIdentity>();
            if (identity == null) continue;

            PlayerColor color = allowRepeats
                ? pool[Random.Range(0, pool.Count)]
                : pool[i % pool.Count];

            if (debugForceColor && player == debugPlayer)
                color = debugColor;

            identity.SetColor(color);
            log.Append(player.playerName).Append('=').Append(color).Append("  ");
        }

        Debug.Log(log.ToString());
    }
}