using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Counts eliminations. Put ONE in the scene (e.g. on the "GameManager" object).
/// The MatchManager resets it, freezes it, and reads GetRanking() for the podium.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    [Header("Scoring (PROPOSAL - design.md has no point values yet)")]
    public int pointsPerElimination = 1;     // same for kills and ring-outs for now

    [Header("State")]
    public bool acceptingScores = true;      // MatchManager turns this off outside of Playing

    [Header("Debug view (read only)")]
    public List<string> debugScores = new List<string>();

    readonly Dictionary<Health, int> scores = new Dictionary<Health, int>();

    public event Action<Health, int> ScoreChanged;   // (player, new score)
    public bool UseRoomScores { get; set; }

    void OnEnable()
    {
        Instance = this;
        Health.AnyDied += OnAnyDied;
    }

    void OnDisable()
    {
        Health.AnyDied -= OnAnyDied;
        if (Instance == this) Instance = null;
    }

    void OnAnyDied(Health victim, Health killer, DeathCause cause)
    {
        if (UseRoomScores || !acceptingScores) return;

        if (killer == null || killer == victim)
        {
            Debug.Log(victim.playerName + " was eliminated (" + cause + ") - no one gets a point.");
            return;
        }

        int newScore = GetScore(killer) + pointsPerElimination;
        SetScore(killer, newScore);

        Debug.Log(killer.playerName + " eliminated " + victim.playerName + " by " + cause + " -> " + newScore + " pts");
    }

    public void SetScore(Health player, int score)
    {
        if (scores.TryGetValue(player, out int previous) && previous == score) return;

        scores[player] = score;
        ScoreChanged?.Invoke(player, score);
        RefreshDebug();
    }

    public void RemovePlayer(Health player)
    {
        scores.Remove(player);
        ScoreChanged?.Invoke(player, 0);
        RefreshDebug();
    }

    public int GetScore(Health player)
    {
        return scores.TryGetValue(player, out int s) ? s : 0;
    }

    public void ResetScores()
    {
        scores.Clear();
        RefreshDebug();
    }

    /// <summary>
    /// Highest score first. Display tied online players in the same slot order on every client.
    /// </summary>
    public List<KeyValuePair<Health, int>> GetRanking()
    {
        return Health.All
            .Select(h => new KeyValuePair<Health, int>(h, GetScore(h)))
            .OrderByDescending(p => p.Value)
            .ThenBy(p => p.Key.RoomSlot)
            .ToList();
    }

    void RefreshDebug()
    {
        debugScores.Clear();
        foreach (var pair in GetRanking())
            debugScores.Add(pair.Key.playerName + ": " + pair.Value);
    }
}
