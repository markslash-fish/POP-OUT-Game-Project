using System;
using System.Collections.Generic;
using UnityEngine;

public enum MatchState { Waiting, Countdown, Playing, Ended }
public enum PhaseType { Global, Dominant, Normal }   // Normal is LAST so older saved values stay valid

[Serializable]
public class PhaseDef
{
    public string name = "Phase";
    public PhaseType type = PhaseType.Global;
    public float duration = 60f;   // seconds
}

/// <summary>
/// Owns the match clock, the phase timeline and the match flow. Put ONE in the scene (e.g. on "GameManager").
/// The current phase is always computed from 'MatchElapsed', so a server can later just send the elapsed time.
/// </summary>
public class MatchManager : MonoBehaviour
{
    public static MatchManager Instance { get; private set; }

    /// <summary>Other scripts check this before reading input. True when no MatchManager exists.</summary>
    public static bool InputEnabled { get; private set; } = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; InputEnabled = true; }

    [Header("Match")]
    public bool autoStart = true;              // true = starts by itself (for testing). Later the lobby will call StartMatch().
    public float countdownSeconds = 3f;
    public float expectedMatchLength = 300f;   // five minutes (design.md). Only used for a safety warning.

    [Header("Phase timeline (from the Game Design docx: Normal, Global, Dominant, Global, Dominant, Global)")]
    public List<PhaseDef> timeline = new List<PhaseDef>
    {
        new PhaseDef { name = "Normal",     type = PhaseType.Normal,   duration = 60f },   // 0:00 - 1:00  no hazards
        new PhaseDef { name = "Global 1",   type = PhaseType.Global,   duration = 60f },   // 1:00 - 2:00
        new PhaseDef { name = "Dominant 1", type = PhaseType.Dominant, duration = 30f },   // 2:00 - 2:30
        new PhaseDef { name = "Global 2",   type = PhaseType.Global,   duration = 60f },   // 2:30 - 3:30
        new PhaseDef { name = "Dominant 2", type = PhaseType.Dominant, duration = 30f },   // 3:30 - 4:00
        new PhaseDef { name = "Global 3",   type = PhaseType.Global,   duration = 60f },   // 4:00 - 5:00
    };

    [Header("Temporary debug")]
    public bool showDebugLabel = true;         // simple on-screen text until the real HUD exists

    // ---- Read-only state ----
    public MatchState State { get; private set; } = MatchState.Waiting;
    public float MatchElapsed { get; private set; }
    public int CurrentPhaseIndex { get; private set; } = -1;
    public float CountdownRemaining { get; private set; }
    public List<KeyValuePair<Health, int>> FinalRanking { get; private set; } = new List<KeyValuePair<Health, int>>();

    public PhaseDef CurrentPhase =>
        (CurrentPhaseIndex >= 0 && CurrentPhaseIndex < timeline.Count) ? timeline[CurrentPhaseIndex] : null;

    public float TotalDuration
    {
        get { float t = 0f; foreach (var p in timeline) t += p.duration; return t; }
    }
    public float TimeRemaining => Mathf.Max(0f, TotalDuration - MatchElapsed);

    public float PhaseTimeRemaining
    {
        get
        {
            if (CurrentPhaseIndex < 0) return 0f;
            float end = 0f;
            for (int i = 0; i <= CurrentPhaseIndex && i < timeline.Count; i++) end += timeline[i].duration;
            return Mathf.Max(0f, end - MatchElapsed);
        }
    }

    // ---- Events (HUD, ArenaEventManager, ColorAssigner... will listen to these) ----
    public event Action<MatchState> StateChanged;
    public event Action MatchStarting;
    public event Action<int> CountdownTick;                           // 3, 2, 1
    public event Action<int, PhaseDef> PhaseChanged;                  // (index, phase)
    public event Action<List<KeyValuePair<Health, int>>> MatchEnded;  // full ranking, highest first

    int lastCountdownTick;

    void OnEnable() { Instance = this; }

    void OnDisable()
    {
        if (Instance == this) Instance = null;
        InputEnabled = true;   // never leave input locked when the manager goes away
    }

    void Start()
    {
        if (autoStart) StartMatch();
        else SetState(MatchState.Waiting);
    }

    void OnValidate()
    {
        float total = 0f;
        foreach (var p in timeline) if (p != null) total += p.duration;
        if (Mathf.Abs(total - expectedMatchLength) > 0.01f)
            Debug.LogWarning("MatchManager: timeline totals " + total + "s but the match should be " + expectedMatchLength + "s.", this);
    }

    // ------------------------------------------------------------------ flow

    [ContextMenu("Start / Restart Match")]
    public void StartMatch()
    {
        MatchElapsed = 0f;
        CurrentPhaseIndex = -1;
        FinalRanking = new List<KeyValuePair<Health, int>>();

        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.ResetScores();
            ScoreManager.Instance.acceptingScores = false;   // no points during the countdown
        }
        MatchStarting?.Invoke();

        if (countdownSeconds > 0f)
        {
            CountdownRemaining = countdownSeconds;
            lastCountdownTick = 0;
            SetState(MatchState.Countdown);
        }
        else
        {
            BeginPlaying();
        }
    }

    void BeginPlaying()
    {
        if (ScoreManager.Instance != null) ScoreManager.Instance.acceptingScores = true;
        SetState(MatchState.Playing);
        UpdatePhase();   // fires PhaseChanged for phase 0
    }

    void EndMatch()
    {
        MatchElapsed = TotalDuration;
        if (ScoreManager.Instance != null) ScoreManager.Instance.acceptingScores = false;   // freeze the score

        FinalRanking = ScoreManager.Instance != null
            ? ScoreManager.Instance.GetRanking()
            : new List<KeyValuePair<Health, int>>();

        SetState(MatchState.Ended);

        // Temporary podium: Console only. The real podium scene is a separate step.
        string[] medals = { "1st", "2nd", "3rd" };
        for (int i = 0; i < FinalRanking.Count && i < 3; i++)
            Debug.Log("PODIUM " + medals[i] + ": " + FinalRanking[i].Key.playerName + " (" + FinalRanking[i].Value + " pts)");

        if (FinalRanking.Count >= 2 && FinalRanking[0].Value == FinalRanking[1].Value)
            Debug.LogWarning("MatchManager: tie for first place. Tie-break rule is NOT defined in design.md yet.");

        MatchEnded?.Invoke(FinalRanking);
    }

    void SetState(MatchState newState)
    {
        State = newState;
        InputEnabled = (newState == MatchState.Playing);
        StateChanged?.Invoke(newState);
    }

    void Update()
    {
        switch (State)
        {
            case MatchState.Countdown:
                CountdownRemaining -= Time.deltaTime;
                int sec = Mathf.CeilToInt(CountdownRemaining);
                if (sec > 0 && sec != lastCountdownTick)
                {
                    lastCountdownTick = sec;
                    CountdownTick?.Invoke(sec);
                }
                if (CountdownRemaining <= 0f) BeginPlaying();
                break;

            case MatchState.Playing:
                MatchElapsed += Time.deltaTime;
                if (MatchElapsed >= TotalDuration) EndMatch();
                else UpdatePhase();
                break;
        }
    }

    // The phase is DERIVED from elapsed time (server-friendly)
    void UpdatePhase()
    {
        int index = IndexAt(MatchElapsed);
        if (index == CurrentPhaseIndex) return;

        CurrentPhaseIndex = index;
        PhaseChanged?.Invoke(index, timeline[index]);
        Debug.Log("PHASE -> " + timeline[index].name + " (" + timeline[index].type + ", " + timeline[index].duration + "s)");
    }

    int IndexAt(float t)
    {
        float acc = 0f;
        for (int i = 0; i < timeline.Count; i++)
        {
            acc += timeline[i].duration;
            if (t < acc) return i;
        }
        return Mathf.Max(0, timeline.Count - 1);
    }

    // ------------------------------------------------------- temporary debug

    static string Fmt(float seconds)
    {
        int s = Mathf.CeilToInt(seconds);
        return (s / 60) + ":" + (s % 60).ToString("00");
    }

    void OnGUI()
    {
        if (!showDebugLabel) return;

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(Screen.height * 0.035f),
            fontStyle = FontStyle.Bold
        };
        style.normal.textColor = Color.white;

        string text = "";
        switch (State)
        {
            case MatchState.Waiting: text = "Waiting..."; break;
            case MatchState.Countdown: text = "GET READY  " + Mathf.CeilToInt(CountdownRemaining); break;
            case MatchState.Playing:
                var p = CurrentPhase;
                text = "TIME " + Fmt(TimeRemaining);
                if (p != null)
                    text += "   |   " + (CurrentPhaseIndex + 1) + "/" + timeline.Count + "  " + p.name + " (" + Fmt(PhaseTimeRemaining) + ")";
                break;
            case MatchState.Ended:
                text = "MATCH OVER";
                for (int i = 0; i < FinalRanking.Count && i < 3; i++)
                    text += "\n" + (i + 1) + ". " + FinalRanking[i].Key.playerName + "  " + FinalRanking[i].Value;
                break;
        }

        GUI.Label(new Rect(Screen.width * 0.02f, Screen.height * 0.02f, Screen.width * 0.9f, Screen.height * 0.3f), text, style);
    }
}