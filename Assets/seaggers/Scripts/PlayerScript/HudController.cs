using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The in-match HUD. It only READS game state (MatchManager, Health, ScoreManager) and never changes it,
/// so it will keep working when those systems become networked.
/// Put on the "HUD" object (e.g. the SafeArea object) and assign the references in the Inspector.
/// </summary>
public class HudController : MonoBehaviour
{
    [Header("Sources")]
    public Health localPlayer;               // the player this screen belongs to
    public MatchManager match;               // auto-found if empty
    public ScoreManager scoreManager;        // auto-found if empty

    [Header("Top bar")]
    public TMP_Text timerText;
    public TMP_Text phaseText;
    public Color timerNormal = Color.white;
    public Color timerLow = new Color(1f, 0.3f, 0.25f);
    public float lowTimeSeconds = 10f;

    [Header("Player panel")]
    public RectTransform hpFill;             // pivot X must be 0 (we scale it)
    public Image hpFillImage;                // same object as hpFill (for color)
    public RectTransform hpTrail;            // pivot X must be 0, drawn BEHIND hpFill
    public TMP_Text hpText;
    public TMP_Text scoreText;
    public Thrower thrower;                  // optional: shows balloon ammo
    public TMP_Text ammoText;                // optional

    [Header("Color / stacks / status (optional)")]
    public PlayerColorIdentity localColor;
    public ColorStackSystem localStacks;
    public StatusEffectSystem localStatus;
    public TMP_Text colorText;               // "BLUE" (in that color)
    public TMP_Text stackText;               // "STACKS 2/3"
    public TMP_Text statusText;              // "STUN  SLOW"
    public GameObject shieldIcon;            // optional
    public Color hpFull = new Color(0.3f, 0.9f, 0.35f);
    public Color hpLow = new Color(0.95f, 0.25f, 0.2f);
    public float trailSpeed = 0.6f;          // how fast the white "damage trail" catches up (per second)

    [Header("Scoreboard")]
    public TMP_Text scoreboardText;
    public Color localPlayerColor = new Color(1f, 0.85f, 0.3f);

    [Header("Center messages")]
    public TMP_Text centerText;              // 3, 2, 1, GO!
    public TMP_Text bannerText;              // phase announcements
    public Color goColor = new Color(0.4f, 1f, 0.5f);
    public Color dominantColor = new Color(1f, 0.6f, 0.2f);

    [Header("Hit feedback")]
    public Image damageFlash;                // full-screen red image, Raycast Target OFF
    public float flashMaxAlpha = 0.35f;
    public float flashFadeTime = 0.4f;

    [Header("Results (TEMPORARY - the real podium comes later)")]
    public GameObject resultsPanel;
    public TMP_Text resultsText;
    public float resultsDelay = 1.5f;

    float hpPct = 1f;
    float trailPct = 1f;
    float flash;
    int lastSecond = -1;
    int lastAmmoKey = -1;
    bool shieldShown;
    Coroutine centerRoutine, bannerRoutine, resultsRoutine;
    readonly StringBuilder sb = new StringBuilder();

    // ------------------------------------------------------------ lifecycle

    void Awake()
    {
        if (match == null) match = FindFirstObjectByType<MatchManager>();
        if (scoreManager == null) scoreManager = FindFirstObjectByType<ScoreManager>();

        if (centerText != null) centerText.text = "";
        if (bannerText != null) bannerText.text = "";
        if (resultsPanel != null) resultsPanel.SetActive(false);
        if (shieldIcon != null) shieldIcon.SetActive(false);
        SetFlashAlpha(0f);
    }

    void OnEnable()
    {
        if (match != null)
        {
            match.StateChanged += OnStateChanged;
            match.CountdownTick += OnCountdownTick;
            match.PhaseChanged += OnPhaseChanged;
            match.MatchEnded += OnMatchEnded;
        }
        if (scoreManager != null) scoreManager.ScoreChanged += OnScoreChanged;
        if (localColor != null) localColor.ColorChanged += OnColorChanged;
        if (localStacks != null) localStacks.StacksChanged += OnStacksChanged;
        if (localStatus != null) localStatus.StatusChanged += OnStatusChanged;
        if (localPlayer != null)
        {
            localPlayer.HealthChanged += RefreshHealth;
            localPlayer.Damaged += OnDamaged;
        }
    }

    void OnDisable()
    {
        if (match != null)
        {
            match.StateChanged -= OnStateChanged;
            match.CountdownTick -= OnCountdownTick;
            match.PhaseChanged -= OnPhaseChanged;
            match.MatchEnded -= OnMatchEnded;
        }
        if (scoreManager != null) scoreManager.ScoreChanged -= OnScoreChanged;
        if (localColor != null) localColor.ColorChanged -= OnColorChanged;
        if (localStacks != null) localStacks.StacksChanged -= OnStacksChanged;
        if (localStatus != null) localStatus.StatusChanged -= OnStatusChanged;
        if (localPlayer != null)
        {
            localPlayer.HealthChanged -= RefreshHealth;
            localPlayer.Damaged -= OnDamaged;
        }
    }

    void Start()
    {
        if (localPlayer == null) Debug.LogWarning("HudController: assign Local Player (Health).", this);
        if (match == null) Debug.LogWarning("HudController: no MatchManager found in the scene.", this);

        if (localPlayer != null)
        {
            RefreshHealth(localPlayer);
            trailPct = hpPct;
        }
        RefreshScore();
        RebuildScoreboard();
        if (match != null) SetTimer(match.TotalDuration);

        if (localColor != null) OnColorChanged(localColor.CurrentColor);
        OnStacksChanged(localStacks != null ? localStacks.Stacks : 0);
        OnStatusChanged();
    }

    void Update()
    {
        if (match != null && match.State == MatchState.Playing)
            SetTimer(match.TimeRemaining);

        // timer "punch" settles back to normal size
        if (timerText != null)
            timerText.rectTransform.localScale = Vector3.Lerp(timerText.rectTransform.localScale, Vector3.one, 10f * Time.unscaledDeltaTime);

        // white damage trail slowly catches up to the real health
        if (hpTrail != null)
        {
            if (trailPct > hpPct) trailPct = Mathf.MoveTowards(trailPct, hpPct, trailSpeed * Time.deltaTime);
            hpTrail.localScale = new Vector3(trailPct, 1f, 1f);
        }

        // red hit flash
        if (flash > 0f)
        {
            flash -= Time.deltaTime / Mathf.Max(0.01f, flashFadeTime);
            SetFlashAlpha(Mathf.Max(0f, flash) * flashMaxAlpha);
        }

        // balloon ammo (only rebuilds the text when something changes)
        if (ammoText != null && thrower != null)
        {
            int key = thrower.IsReloading ? 1000 + Mathf.CeilToInt(thrower.ReloadRemaining * 10f) : thrower.Ammo;
            if (key != lastAmmoKey)
            {
                lastAmmoKey = key;
                ammoText.text = thrower.IsReloading
                    ? "RELOADING " + thrower.ReloadRemaining.ToString("0.0")
                    : "AMMO " + thrower.Ammo + "/" + thrower.MaxAmmo;
            }
        }

        // respawn shield icon
        if (shieldIcon != null && localPlayer != null)
        {
            bool shield = localPlayer.IsInvulnerable && !localPlayer.IsDead;
            if (shield != shieldShown)
            {
                shieldShown = shield;
                shieldIcon.SetActive(shield);
            }
        }
    }

    // ------------------------------------------------------------ player panel

    void RefreshHealth(Health h)
    {
        hpPct = Mathf.Clamp01(h.Current / h.maxHealth);

        if (hpFill != null) hpFill.localScale = new Vector3(hpPct, 1f, 1f);
        if (hpFillImage != null) hpFillImage.color = Color.Lerp(hpLow, hpFull, hpPct);
        if (hpText != null) hpText.text = Mathf.CeilToInt(h.Current) + " / " + Mathf.CeilToInt(h.maxHealth);

        // healing / respawn: the trail must not stay behind the fill
        if (trailPct < hpPct) trailPct = hpPct;
    }

    void OnDamaged(Health victim, float amount)
    {
        flash = 1f;
    }

    void SetFlashAlpha(float a)
    {
        if (damageFlash == null) return;
        Color c = damageFlash.color;
        c.a = a;
        damageFlash.color = c;
    }

    void RefreshScore()
    {
        if (scoreText == null) return;
        int s = (scoreManager != null && localPlayer != null) ? scoreManager.GetScore(localPlayer) : 0;
        scoreText.text = "SCORE  " + s;
    }

    void OnScoreChanged(Health player, int newScore)
    {
        RefreshScore();
        RebuildScoreboard();
    }

    void RebuildScoreboard()
    {
        if (scoreboardText == null || scoreManager == null) return;

        sb.Clear();
        var ranking = scoreManager.GetRanking();
        for (int i = 0; i < ranking.Count && i < 6; i++)
        {
            var h = ranking[i].Key;
            bool me = h == localPlayer;
            if (me) sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(localPlayerColor)).Append('>');
            sb.Append(i + 1).Append(". ").Append(h.playerName).Append("   ").Append(ranking[i].Value);
            if (me) sb.Append("</color>");
            sb.Append('\n');
        }
        scoreboardText.text = sb.ToString();
    }

    // ------------------------------------------------------------ color / stacks / status

    void OnColorChanged(PlayerColor color)
    {
        if (colorText == null) return;
        colorText.text = PlayerColorInfo.DisplayName(color);
        colorText.color = PlayerColorInfo.ToColor(color);
    }

    void OnStacksChanged(int stacks)
    {
        if (stackText == null) return;
        int max = localStacks != null ? localStacks.threshold : 3;
        stackText.text = "STACKS " + stacks + "/" + max;
    }

    void OnStatusChanged()
    {
        if (statusText == null) return;
        statusText.text = localStatus != null ? localStatus.DescribeActive() : "";
    }

    // ------------------------------------------------------------ timer + phases

    void SetTimer(float seconds)
    {
        if (timerText == null) return;

        int secs = Mathf.CeilToInt(Mathf.Max(0f, seconds));
        if (secs == lastSecond) return;
        lastSecond = secs;

        timerText.text = (secs / 60) + ":" + (secs % 60).ToString("00");
        bool low = secs <= lowTimeSeconds;
        timerText.color = low ? timerLow : timerNormal;

        if (low && secs > 0 && match != null && match.State == MatchState.Playing)
            timerText.rectTransform.localScale = Vector3.one * 1.3f;   // tick "punch"
    }

    void OnPhaseChanged(int index, PhaseDef phase)
    {
        string label = phase.name.ToUpperInvariant();
        if (phaseText != null)
            phaseText.text = "PHASE " + (index + 1) + "/" + match.timeline.Count + " - " + label;

        bool dominant = phase.type == PhaseType.Dominant;
        ShowBanner(dominant ? "DOMINANT PHASE!" : label, dominant ? dominantColor : Color.white, dominant ? 2f : 1.2f);
    }

    // ------------------------------------------------------------ match flow

    void OnCountdownTick(int seconds)
    {
        ShowCenter(seconds.ToString(), Color.white, 0.6f);
    }

    void OnStateChanged(MatchState state)
    {
        switch (state)
        {
            case MatchState.Countdown:
                HideResults();
                lastSecond = -1;
                SetTimer(match.TotalDuration);
                if (phaseText != null) phaseText.text = "";
                RefreshScore();
                RebuildScoreboard();
                break;

            case MatchState.Playing:
                ShowCenter("GO!", goColor, 0.7f);
                break;

            case MatchState.Ended:
                lastSecond = -1;
                SetTimer(0f);
                if (phaseText != null) phaseText.text = "MATCH OVER";
                ShowCenter("TIME'S UP!", Color.white, 1.4f);
                if (resultsRoutine != null) StopCoroutine(resultsRoutine);
                resultsRoutine = StartCoroutine(ShowResultsAfterDelay());
                break;
        }
    }

    void OnMatchEnded(System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Health, int>> ranking)
    {
        if (resultsText == null) return;

        string[] medals = { "1ST", "2ND", "3RD" };
        sb.Clear();
        for (int i = 0; i < ranking.Count && i < 3; i++)
            sb.Append(medals[i]).Append("   ").Append(ranking[i].Key.playerName).Append("   ").Append(ranking[i].Value).Append('\n');
        resultsText.text = sb.ToString();
    }

    IEnumerator ShowResultsAfterDelay()
    {
        yield return new WaitForSecondsRealtime(resultsDelay);
        if (resultsPanel != null) resultsPanel.SetActive(true);
    }

    void HideResults()
    {
        if (resultsRoutine != null) StopCoroutine(resultsRoutine);
        if (resultsPanel != null) resultsPanel.SetActive(false);
    }

    /// <summary>Hook this to the PLAY AGAIN button's OnClick (temporary, for testing).</summary>
    public void OnPlayAgain()
    {
        HideResults();
        if (match != null) match.StartMatch();
    }

    // ------------------------------------------------------------ pop-up text

    void ShowCenter(string message, Color color, float hold)
    {
        if (centerText == null) return;
        if (centerRoutine != null) StopCoroutine(centerRoutine);
        centerRoutine = StartCoroutine(PopText(centerText, message, color, hold));
    }

    void ShowBanner(string message, Color color, float hold)
    {
        if (bannerText == null) return;
        if (bannerRoutine != null) StopCoroutine(bannerRoutine);
        bannerRoutine = StartCoroutine(PopText(bannerText, message, color, hold));
    }

    // grows in, holds, fades out
    IEnumerator PopText(TMP_Text t, string message, Color c, float hold)
    {
        t.text = message;
        RectTransform rt = t.rectTransform;

        const float popTime = 0.18f;
        float time = 0f;
        while (time < popTime)
        {
            time += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(time / popTime);
            rt.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, k);
            t.color = new Color(c.r, c.g, c.b, k);
            yield return null;
        }
        rt.localScale = Vector3.one;
        t.color = c;

        yield return new WaitForSecondsRealtime(hold);

        const float fadeTime = 0.25f;
        time = 0f;
        while (time < fadeTime)
        {
            time += Time.unscaledDeltaTime;
            t.color = new Color(c.r, c.g, c.b, 1f - Mathf.Clamp01(time / fadeTime));
            yield return null;
        }
        t.text = "";
    }
}