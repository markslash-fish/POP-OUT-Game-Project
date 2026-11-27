using System;
using System.Collections.Generic;
using SpacetimeDB;
using SpacetimeDB.Types;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-500)]
public sealed class PopOutRoom : MonoBehaviour
{
    const string Server = "https://maincloud.spacetimedb.com";
    const string Database = "popout-playtest-20261126";
    const float PoseInterval = 1f / 15f;
    const float PoseHeartbeat = 0.5f;
    const float HorizontalPoseThreshold = 0.04f;
    const float VerticalPoseThreshold = 0.15f;
    const byte BalloonAction = 1;
    const byte MeleeAction = 2;
    const string SixPlayerRoomPrefix = "S6";
    const string RoomVersion = "V3";
    const string DisplayNamePreference = "PopOut.DisplayName";
    const int MaxDisplayNameLength = 20;
    const float PoseBand = 0.25f;
    const float PoseSpeedRange = 0.24f;
    const float ImpactOffsetScale = 0.125f;
    const int ImpactMetadataScale = 32768;
    const int AllPlayerSlots = 0x3f;
    const float LifestealReply = -0.5f;
    static readonly string[] RoomSizes = { "2 players", "6 players" };
    static readonly int SpeedParameter = Animator.StringToHash("Speed");

    [SerializeField] HudController hud;
    [SerializeField] GameObject[] offlinePlayers;
    [SerializeField] Texture2D lobbyLogo;
    [SerializeField] Texture2D lobbyBackground;

    readonly Dictionary<Identity, PopOutRemotePlayer> opponents = new();
    readonly Dictionary<Health, Identity> remoteIdentities = new();
    readonly Queue<PendingAction> outgoingActions = new();
    readonly HashSet<(int Shot, int VictimSlot)> pendingLifesteal = new();

    sealed class PendingAction
    {
        public byte Kind;
        public Vector3 Point;
        public Vector3 Payload;
        public float Yaw;
        public float Expiration;
        public int Shot = -1;
        public int AffectedSlots;
        public int StackEligibleSlots;
        public int DirectSlot;
        public int AbilitySlots;
        public int StacksAfter = -1;
        public int Life;
        public bool Prepared;
    }

    MatchManager match;
    ScoreManager scores;
    Health local;
    Transform localPhysicalSpine;
    RespawnSystem respawn;
    PlayerColorIdentity localColor;
    ColorStackSystem localStacks;
    Thrower thrower;
    MeleeCombat melee;
    Animator animator;
    AnimatedBodyMover mover;
    GameObject replicaTemplate;
    SpawnPoint[] spawnPoints;
    DbConnection connection;
    Identity identity;
    string roomCode = "POPOUT";
    string message = "";
    string displayName = "";
    string nameDraft = "";
    bool enteringName;
    int roomSize = 6;
    TouchScreenKeyboard keyboard;
    GUIStyle lobbyTitle;
    GUIStyle lobbyStatus;
    GUIStyle lobbyPrimaryButton;
    RenderTexture softenedLobbyBackground;
    enum LobbyField { Name, RoomCode }
    LobbyField keyboardField;
    bool joined;
    bool localHasPose;
    bool started;
    bool automaticJoin;
    float nextPose;
    float nextPoseHeartbeat;
    Vector3 lastSentPosition;
    float lastSentYaw;
    float lastSentSpeed;
    float lastSentHealth;
    bool lastSentShield;
    ThrowPose lastSentThrowPose;
    bool waitingForAction;
    float retryActionAt;
    float joinDeadline;
    int localLife;
    int MaxRoomCodeLength => 12 - RoomVersion.Length - (roomSize == 6 ? SixPlayerRoomPrefix.Length : 0);
    bool ShowingLobby => !joined || !started || connection == null;

    void Awake()
    {
        match = GetComponent<MatchManager>();
        scores = GetComponent<ScoreManager>();
        local = hud.localPlayer;
        localPhysicalSpine = local.GetComponent<ActiveRagdoll>().physicalSpine;
        respawn = local.GetComponent<RespawnSystem>();
        localColor = local.GetComponent<PlayerColorIdentity>();
        localStacks = local.GetComponent<ColorStackSystem>();
        localStacks.UseNetworkHits = true;
        thrower = hud.thrower;
        melee = local.GetComponentInChildren<MeleeCombat>();
        animator = melee.animator != null ? melee.animator : melee.GetComponent<Animator>();
        mover = animator.GetComponent<AnimatedBodyMover>();

        match.autoStart = false;
        scores.UseRoomScores = true;
        GetComponent<ColorAssigner>().enabled = false;
        foreach (GameObject player in offlinePlayers) player.SetActive(false);
        local.gameObject.SetActive(false);

        // Clone the configured local rig. An inactive parent lets us disable its input
        // and simulation before any component on the replica receives Awake or OnEnable.
        var templates = new GameObject("Online replica template");
        templates.transform.SetParent(transform);
        templates.SetActive(false);
        replicaTemplate = Instantiate(local.gameObject, templates.transform, true);
        replicaTemplate.SetActive(true);

        spawnPoints = FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None);
        Array.Sort(spawnPoints, (a, b) => string.CompareOrdinal(a.name, b.name));

        thrower.BalloonReleased += SendBalloon;
        thrower.BalloonImpacted += SendImpact;
        melee.SwingActive += SendMelee;
        Health.AnyDied += ReportDeath;
        hud.OnlinePlayAgain = Leave;
        Application.runInBackground = true;
        if (Application.platform == RuntimePlatform.Android) Application.targetFrameRate = 60;
    }

    void Start()
    {
        displayName = PlayerPrefs.GetString(DisplayNamePreference, "").Trim();
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "-popout-room")
            {
                roomCode = args[++i];
                automaticJoin = true;
            }
            else if (args[i] == "-popout-name")
            {
                displayName = args[++i].Trim();
            }
            else if (args[i] == "-popout-players" && int.TryParse(args[i + 1], out int size) &&
                (size == 2 || size == 6))
            {
                roomSize = size;
                i++;
            }
        }
        nameDraft = displayName;
        enteringName = !IsValidDisplayName(displayName);
        if (automaticJoin) Join();
    }

    static bool IsValidDisplayName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxDisplayNameLength) return false;
        foreach (char character in name)
            if (char.IsControl(character) || character is '<' or '>' or '\u2028' or '\u2029' ||
                char.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.Format)
                return false;

        return true;
    }

    void ConfirmDisplayName()
    {
        string name = nameDraft.Trim();
        if (!IsValidDisplayName(name))
        {
            message = "Enter a display name with 1–20 characters on one line.";
            return;
        }

        displayName = name;
        nameDraft = name;
        enteringName = false;
        message = "";
        PlayerPrefs.SetString(DisplayNamePreference, displayName);
        PlayerPrefs.Save();
    }

    void Join()
    {
        if (enteringName || !IsValidDisplayName(displayName))
        {
            message = "Enter your display name first.";
            enteringName = true;
            return;
        }
        roomCode = roomCode.Trim().ToUpperInvariant();
        if (roomCode.Length < 4 || roomCode.Length > MaxRoomCodeLength)
        {
            message = $"Enter 4–{MaxRoomCodeLength} letters or digits.";
            return;
        }
        if (spawnPoints.Length < roomSize)
        {
            JoinFailed($"This arena needs {roomSize} spawn points.");
            return;
        }
        foreach (char character in roomCode)
        {
            if ((character >= 'A' && character <= 'Z') ||
                (character >= '0' && character <= '9')) continue;
            message = "Use letters and digits for the room code.";
            return;
        }
        if (roomSize == 2 && roomCode.StartsWith(SixPlayerRoomPrefix, StringComparison.Ordinal))
        {
            message = "Choose a 2-player room code that doesn't start with S6.";
            return;
        }

        message = "Connecting…";
        joinDeadline = Time.unscaledTime + 20f;
        try
        {
            connection = DbConnection.Builder()
                .WithUri(Server)
                .WithDatabaseName(Database)
                .WithCompression(Compression.None)
                .OnConnect(OnConnected)
                .OnConnectError(error => JoinFailed(error.Message))
                .OnDisconnect((conn, error) =>
                {
                    if (connection == conn) JoinFailed(error?.Message ?? "Disconnected from the room.");
                })
                .Build();
            connection.OnUnhandledReducerError += (ctx, error) =>
            {
                if (!joined) JoinFailed(error.Message);
                else Debug.LogWarning($"Pop Out reducer {ctx.Event.Reducer}: {error.Message}", this);
            };
        }
        catch (Exception error)
        {
            JoinFailed(error.Message);
        }
    }

    void OnConnected(DbConnection conn, Identity playerIdentity, string _)
    {
        identity = playerIdentity;
        var players = conn.Db.RoomPlayers;
        players.OnInsert += (_, row) => ApplyRow(row, false);
        players.OnUpdate += (_, previous, row) => ApplyRow(row, previous.ActionSequence != row.ActionSequence);
        players.OnDelete += (_, row) => RemoveOpponent(row.Identity);

        conn.SubscriptionBuilder()
            .OnApplied(_ =>
            {
                string databaseRoom = (roomSize == 6 ? SixPlayerRoomPrefix : "") + RoomVersion + roomCode;
                conn.Reducers.JoinRoom(databaseRoom, displayName);
            })
            .OnError((_, error) => JoinFailed(error.Message))
            .Subscribe(new[] { "SELECT * FROM room_players" });
    }

    void Update()
    {
        if (ShowingLobby && lobbyBackground != null &&
            (softenedLobbyBackground == null || !softenedLobbyBackground.IsCreated()) &&
            SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            CacheLobbyBackground();

        connection?.FrameTick();
        StartWhenReady();

        if (connection != null && !joined && Time.unscaledTime >= joinDeadline)
            JoinFailed("The room did not respond. Try again.");

        if (keyboard != null && keyboard.status != TouchScreenKeyboard.Status.Visible)
        {
            if (keyboard.status == TouchScreenKeyboard.Status.Done)
            {
                if (keyboardField == LobbyField.Name) nameDraft = keyboard.text;
                else roomCode = keyboard.text;
            }
            keyboard = null;
        }

        float now = Time.unscaledTime;
        if (!joined || connection == null ||
            (match.State == MatchState.Ended && started)) return;

        SendPendingAction(now);
        float health = Mathf.Clamp01(local.Current / local.maxHealth);
        bool shield = local.IsInvulnerable;
        var pose = thrower.IsReleasing ? ThrowPose.Throwing : mover.holding ? ThrowPose.Aiming : ThrowPose.Idle;
        bool priority = shield != lastSentShield || health != lastSentHealth || pose != lastSentThrowPose;
        if (now < nextPose && !priority) return;

        nextPose = now + PoseInterval;
        Vector3 position = localPhysicalSpine.position;
        float yaw = animator.transform.eulerAngles.y;
        float speed = Mathf.Clamp01(animator.GetFloat(SpeedParameter));
        Vector3 movement = position - lastSentPosition;

        // Idle ragdolls still wobble slightly. A heartbeat keeps their pose fresh.
        if (now < nextPoseHeartbeat && !priority &&
            Mathf.Abs(Mathf.DeltaAngle(lastSentYaw, yaw)) < 0.5f &&
            Mathf.Abs(speed - lastSentSpeed) < 0.02f &&
            Mathf.Abs(movement.y) < VerticalPoseThreshold &&
            movement.x * movement.x + movement.z * movement.z < HorizontalPoseThreshold * HorizontalPoseThreshold)
            return;

        // The published pose has one normalized float for movement. Separate bands also carry the throw pose.
        float poseAndSpeed = (int)pose * PoseBand + speed * PoseSpeedRange;
        connection.Reducers.UpdatePose(position.x, position.y, position.z, yaw, poseAndSpeed, health, shield);
        nextPoseHeartbeat = now + PoseHeartbeat;
        lastSentPosition = position;
        lastSentYaw = yaw;
        lastSentSpeed = speed;
        lastSentHealth = health;
        lastSentShield = shield;
        lastSentThrowPose = pose;
    }

    void ApplyRow(RoomPlayerState row, bool newAction)
    {
        if (connection == null) return;

        if (row.Slot >= roomSize)
        {
            if (row.Identity == identity)
                JoinFailed($"This {roomSize}-player room is full.");
            return;
        }

        if (row.Identity == identity)
        {
            if (newAction && waitingForAction && outgoingActions.Count != 0)
            {
                var pending = outgoingActions.Peek();
                if (pending.Kind == row.Action && pending.Yaw == row.ActionYaw)
                {
                    outgoingActions.Dequeue();
                    waitingForAction = false;
                    if (pending.Shot >= 0) ConfirmLocalImpact(pending);
                }
            }
            localHasPose = row.HasPose;
            if (!joined)
            {
                joined = true;
                local.playerName = row.Name;
                local.RoomSlot = row.Slot;
                local.gameObject.SetActive(true);
                local.ResetHealth();
                nextPoseHeartbeat = 0f;
                localColor.SetColor((PlayerColor)row.Color);
                respawn.PlaceAt(spawnPoints[row.Slot].transform.position);
                foreach (PopOutRemotePlayer opponent in opponents.Values) opponent.IgnoreCollisionWith(local);
                Debug.Log($"Pop Out joined room {roomCode} as {row.Name} (slot {row.Slot}, color {localColor.CurrentColor}).");
            }
            if (match.State != MatchState.Ended) scores.SetScore(local, (int)row.Score);
            return;
        }

        if (!opponents.TryGetValue(row.Identity, out PopOutRemotePlayer remote))
        {
            GameObject clone = Instantiate(replicaTemplate, replicaTemplate.transform.parent, true);
            clone.name = row.Name;
            remote = new PopOutRemotePlayer(clone, row);
            if (joined) remote.IgnoreCollisionWith(local);
            opponents.Add(row.Identity, remote);
            remoteIdentities.Add(remote.Health, row.Identity);
            Debug.Log($"Pop Out opponent joined as {row.Name} (slot {row.Slot}, color {(PlayerColor)row.Color}).");
        }

        var throwPose = (ThrowPose)Mathf.Clamp(Mathf.FloorToInt(row.Speed / PoseBand), 0, (int)ThrowPose.Throwing);
        float speed = Mathf.Clamp01((row.Speed - (int)throwPose * PoseBand) / PoseSpeedRange);
        remote.Apply(row, speed, throwPose);
        if (match.State != MatchState.Ended) scores.SetScore(remote.Health, (int)row.Score);
        if (!newAction || match.State != MatchState.Playing) return;

        var position = new Vector3(row.ActionX, row.ActionY, row.ActionZ);
        var payload = new Vector3(row.ActionVx, row.ActionVy, row.ActionVz);
        switch (row.Action)
        {
            case BalloonAction:
                remote.LaunchBalloon(Mathf.RoundToInt(row.ActionYaw), position, payload);
                break;
            case MeleeAction when row.ActionYaw == LifestealReply:
                ReceiveLifesteal(remote, payload);
                break;
            case MeleeAction when row.ActionYaw < 0f:
                ReceiveImpact(remote, position + payload / ImpactOffsetScale, -row.ActionYaw);
                break;
            case MeleeAction:
                remote.Swing(position, row.ActionYaw);
                break;
        }
    }

    void StartWhenReady()
    {
        if (started || connection == null || !joined || !localHasPose || opponents.Count + 1 != roomSize) return;
        foreach (PopOutRemotePlayer opponent in opponents.Values)
            if (!opponent.HasPose) return;

        started = true;
        match.StartMatch();
    }

    void LateUpdate()
    {
        foreach (PopOutRemotePlayer player in opponents.Values) player.ApplyPose();
    }

    void RemoveOpponent(Identity player)
    {
        if (!opponents.Remove(player, out PopOutRemotePlayer remote)) return;
        remoteIdentities.Remove(remote.Health);
        remote.ClearBalloons();
        remote.Health.gameObject.SetActive(false);
        scores.RemovePlayer(remote.Health);
        Destroy(remote.Health.gameObject);
        if (match.State == MatchState.Countdown)
        {
            started = false;
            match.ReturnToWaiting();
        }
        else if (match.State == MatchState.Playing)
        {
            JoinFailed("A player left. Rejoin to start a new match.");
        }
    }

    void SendBalloon(int shot, Vector3 position, Vector3 velocity)
    {
        SendAction(BalloonAction, position, velocity, shot);
    }

    void SendMelee(Vector3 position, float yaw)
    {
        SendAction(MeleeAction, position, Vector3.zero, yaw);
    }

    void SendImpact(int shot, Vector3 point, Health directHit, IReadOnlyCollection<Health> targets)
    {
        var impact = new PendingAction { Kind = MeleeAction, Point = point, Shot = shot, Life = localLife };
        foreach (Health target in targets)
        {
            if (!remoteIdentities.ContainsKey(target)) continue;
            int slot = 1 << target.RoomSlot;
            impact.AffectedSlots |= slot;
            if (!target.IsDead && !target.IsInvulnerable) impact.StackEligibleSlots |= slot;
        }

        impact.DirectSlot = directHit != null && remoteIdentities.ContainsKey(directHit) ? directHit.RoomSlot + 1 : 0;
        QueueAction(impact);
    }

    void PrepareImpact(PendingAction impact)
    {
        if (impact.Life == localLife && !local.IsDead && CombatRules.StacksEnabled && impact.StackEligibleSlots != 0)
        {
            int stacks = localStacks.Stacks;
            for (int slot = 0; slot < roomSize; slot++)
            {
                if ((impact.StackEligibleSlots & (1 << slot)) == 0) continue;
                stacks = localStacks.StacksAfterHit(stacks, impact.DirectSlot == slot + 1, out bool triggersAbility);
                if (triggersAbility) impact.AbilitySlots |= 1 << slot;
            }
            impact.StacksAfter = stacks;
        }

        int metadata = (impact.AbilitySlots << 9) | (impact.AffectedSlots << 3) | impact.DirectSlot;
        // The published reducer accepts two action kinds. A negative melee yaw carries the confirmed balloon impact.
        // Its integer part identifies the throw. Its 15 fractional bits store the effects, targets, and direct hit.
        impact.Yaw = -(impact.Shot + 1 + metadata / (float)ImpactMetadataScale);
        impact.Prepared = true;
    }

    void ConfirmLocalImpact(PendingAction impact)
    {
        ShowRemoteHitBadges(local, impact.AffectedSlots);
        if (impact.StacksAfter < 0 || impact.Life != localLife || local.IsDead) return;

        localStacks.CommitHitStacks(impact.StacksAfter);
        if (impact.AbilitySlots == 0) return;
        if (localColor.CurrentColor == PlayerColor.Orange) localStacks.ApplyAbility(null);
        if (localColor.CurrentColor == PlayerColor.Green)
            for (int slot = 0; slot < roomSize; slot++)
                if ((impact.AbilitySlots & (1 << slot)) != 0) pendingLifesteal.Add((impact.Shot, slot));
    }

    void ReceiveImpact(PopOutRemotePlayer remote, Vector3 point, float marker)
    {
        int metadata = ReadImpactMetadata(marker, out int shot);
        int mask = (metadata >> 3) & AllPlayerSlots;
        int abilitySlots = metadata >> 9;
        Health victim = (mask & (1 << local.RoomSlot)) != 0 ? local : null;
        if (!remote.ImpactBalloon(shot, point, victim, (metadata & 7) == local.RoomSlot + 1, out bool damaged)) return;

        ShowRemoteHitBadges(remote.Health, mask);
        if (!damaged || local.IsDead || (abilitySlots & (1 << local.RoomSlot)) == 0) return;

        float drained = remote.Stacks.ApplyAbility(local);
        if (drained > 0f)
            SendAction(MeleeAction, Vector3.zero, new Vector3(remote.Health.RoomSlot, shot / 32f, drained * ImpactOffsetScale), LifestealReply);
    }

    void ReceiveLifesteal(PopOutRemotePlayer victim, Vector3 payload)
    {
        if (Mathf.RoundToInt(payload.x) != local.RoomSlot ||
            !pendingLifesteal.Remove((Mathf.RoundToInt(payload.y * 32f), victim.Health.RoomSlot))) return;

        float maximumDrain = victim.Health.maxHealth * localStacks.settings.lifestealPercentOfCurrentHealth;
        local.Heal(Mathf.Clamp(payload.z / ImpactOffsetScale, 0f, maximumDrain));
    }

    static int ReadImpactMetadata(float marker, out int shot)
    {
        shot = Mathf.FloorToInt(marker) - 1;
        return Mathf.RoundToInt((marker - shot - 1f) * ImpactMetadataScale);
    }

    void ShowRemoteHitBadges(Health attacker, int mask)
    {
        if (mask == 0) return;

        foreach (PopOutRemotePlayer opponent in opponents.Values)
            if ((mask & (1 << opponent.Health.RoomSlot)) != 0)
                opponent.ShowHitBadge(attacker);
    }

    void SendAction(byte action, Vector3 position, Vector3 velocity, float yaw)
    {
        QueueAction(new PendingAction { Kind = action, Point = position, Payload = velocity, Yaw = yaw });
    }

    void QueueAction(PendingAction action)
    {
        if (!joined || match.State != MatchState.Playing) return;
        action.Expiration = Time.unscaledTime + 3f;
        outgoingActions.Enqueue(action);
        SendPendingAction(Time.unscaledTime);
    }

    void SendPendingAction(float now)
    {
        if (connection == null || match.State != MatchState.Playing || outgoingActions.Count == 0) return;
        if (waitingForAction && now < retryActionAt) return;

        var next = outgoingActions.Peek();
        if (next.Expiration <= now)
        {
            outgoingActions.Dequeue();
            waitingForAction = false;
            return;
        }

        Vector3 position = next.Point;
        Vector3 payload = next.Payload;
        if (next.Shot >= 0)
        {
            if (!next.Prepared) PrepareImpact(next);
            // Keep the validated action origin at the sender's latest pose and transmit a relative impact point.
            position = lastSentPosition;
            payload = (next.Point - position) * ImpactOffsetScale;
        }
        else if (next.Yaw == LifestealReply) position = lastSentPosition;
        waitingForAction = true;
        retryActionAt = now + 0.65f;
        connection.Reducers.PerformAction(next.Kind, position.x, position.y, position.z,
            payload.x, payload.y, payload.z, next.Yaw);
    }

    void ReportDeath(Health victim, Health killer, DeathCause _)
    {
        if (victim != local) return;
        localLife++;
        pendingLifesteal.Clear();
        if (!joined || match.State != MatchState.Playing) return;
        Identity? killerIdentity = null;
        if (killer != null && remoteIdentities.TryGetValue(killer, out Identity opponent)) killerIdentity = opponent;
        connection?.Reducers.ReportKo(killerIdentity);
    }

    void JoinFailed(string error)
    {
        message = error;
        Debug.LogWarning($"Pop Out: {error}", this);
        Disconnect();
        if (joined) match.ReturnToWaiting();
        else if (opponents.Count > 0)
        {
            foreach (Identity player in new List<Identity>(opponents.Keys))
                RemoveOpponent(player);
        }
    }

    void Disconnect()
    {
        DbConnection active = connection;
        connection = null;
        outgoingActions.Clear();
        pendingLifesteal.Clear();
        waitingForAction = false;
        active?.Disconnect();
    }

    void Leave()
    {
        Disconnect();
        SceneManager.LoadScene(gameObject.scene.name);
    }

    void OnDestroy()
    {
        Health.AnyDied -= ReportDeath;
        if (thrower != null)
        {
            thrower.BalloonReleased -= SendBalloon;
            thrower.BalloonImpacted -= SendImpact;
        }
        if (melee != null) melee.SwingActive -= SendMelee;
        if (softenedLobbyBackground != null) Destroy(softenedLobbyBackground);
        Disconnect();
    }

    void OnGUI()
    {
        bool showingLobby = ShowingLobby;
        if (showingLobby) DrawLobbyBackground();
        Rect safe = Screen.safeArea;
        float scale = Mathf.Clamp(Mathf.Min(safe.width / 700f, safe.height / 400f), 0.6f, 2.5f);
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(new Vector3(safe.x, Screen.height - safe.yMax), Quaternion.identity,
            new Vector3(scale, scale, 1f));
        float width = safe.width / scale;
        float height = safe.height / scale;
        if (!showingLobby)
        {
            GUILayout.BeginArea(new Rect(width - 232f, 12f, 220f, 40f), GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{roomCode}  •  {opponents.Count + 1}/{roomSize}");
            if (GUILayout.Button("Leave", GUILayout.Width(70f), GUILayout.Height(28f))) Leave();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
        else
        {
            DrawLobby(width, height);
        }
        GUI.matrix = previous;
    }

    void DrawLobby(float width, float height)
    {
        if (lobbyTitle == null)
        {
            lobbyTitle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            lobbyStatus = new GUIStyle(GUI.skin.label) { wordWrap = true };
            lobbyPrimaryButton = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold };
        }

        float formHeight = joined ? 150f : enteringName ? 167f : connection == null ? 248f : 110f;
        float logoHeight = lobbyLogo == null ? 0f : height < 410f ? 92f : 110f;
        float panelWidth = Mathf.Min(384f, width - 24f);
        float panelHeight = formHeight + logoHeight + 30f;
        var panel = new Rect((width - panelWidth) / 2f, (height - panelHeight) / 2f, panelWidth, panelHeight);

        PaintLobbySurface(new Rect(panel.x + 1f, panel.y + 4f, panel.width, panel.height), new Color(0.02f, 0.03f, 0.06f, 0.55f), 14f);
        PaintLobbySurface(panel, new Color(0.08f, 0.12f, 0.20f, 1f), 14f);
        if (lobbyLogo != null)
            GUI.DrawTexture(new Rect(panel.x + 18f, panel.y + 12f, panel.width - 36f, logoHeight), lobbyLogo, ScaleMode.ScaleToFit);

        GUILayout.BeginArea(new Rect(panel.x + 22f, panel.y + logoHeight + 16f, panel.width - 44f, formHeight));
        GUILayout.Label("Multiplayer", lobbyTitle, GUILayout.Height(28f));
        GUILayout.Space(8f);

        if (joined)
        {
            GUILayout.Label($"{roomCode}  •  {opponents.Count + 1}/{roomSize}");
            if (connection != null) GUILayout.Label("Waiting for players…");
            if (DrawLobbyPrimaryButton("Leave room")) Leave();
        }
        else if (enteringName)
        {
            GUILayout.Label("Display name");
            nameDraft = DrawLobbyField(nameDraft, "Enter your name", MaxDisplayNameLength, LobbyField.Name);
            if (DrawLobbyPrimaryButton("Continue")) ConfirmDisplayName();
        }
        else if (connection == null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Playing as {displayName}");
            if (GUILayout.Button("Change name", GUILayout.Width(100f)))
            {
                enteringName = true;
                nameDraft = displayName;
                message = "";
            }
            GUILayout.EndHorizontal();

            roomSize = GUILayout.SelectionGrid(roomSize == 6 ? 1 : 0, RoomSizes, 2, GUILayout.Height(34f)) == 0 ? 2 : 6;
            GUILayout.Label("Room code");
            roomCode = DrawLobbyField(roomCode, "Enter room code", MaxRoomCodeLength, LobbyField.RoomCode);
            if (DrawLobbyPrimaryButton("Join room")) Join();
        }

        if (!string.IsNullOrEmpty(message)) GUILayout.Label(message, lobbyStatus);
        GUILayout.EndArea();
    }

    void DrawLobbyBackground()
    {
        if (Event.current.type != EventType.Repaint) return;

        var screen = new Rect(0f, 0f, Screen.width, Screen.height);
        PaintLobbySurface(screen, new Color(0.025f, 0.043f, 0.085f), 0f);
        if (lobbyBackground == null) return;
        float size = Mathf.Max(screen.width * 0.91f, screen.height * 1.65f);
        var artwork = new Rect((screen.width - size) * 0.5f, (screen.height - size) * 0.5f, size, size);
        Color previous = GUI.color;
        GUI.color = new Color(0.70f, 0.80f, 1f, 0.43f);
        GUI.DrawTexture(artwork, softenedLobbyBackground != null ? softenedLobbyBackground : lobbyBackground,
            ScaleMode.ScaleToFit, true);
        GUI.color = previous;
    }

    void CacheLobbyBackground()
    {
        // Cache a small copy. Bilinear scaling softens the oversized artwork without per-frame blur passes.
        var previous = RenderTexture.active;
        var downsample = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32);
        downsample.filterMode = FilterMode.Bilinear;
        softenedLobbyBackground ??= new RenderTexture(96, 96, 0, RenderTextureFormat.ARGB32)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        try
        {
            Graphics.Blit(lobbyBackground, downsample);
            Graphics.Blit(downsample, softenedLobbyBackground);
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(downsample);
        }
    }

    static void PaintLobbySurface(Rect bounds, Color tint, float radius)
    {
        GUI.DrawTexture(bounds, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, tint, 0f, radius);
    }

    bool DrawLobbyPrimaryButton(string label)
    {
        Color previous = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.34f, 0.7f, 1f);
        bool pressed = GUILayout.Button(label, lobbyPrimaryButton, GUILayout.Height(42f));
        GUI.backgroundColor = previous;
        return pressed;
    }

    string DrawLobbyField(string value, string placeholder, int maxLength, LobbyField field)
    {
        if (!TouchScreenKeyboard.isSupported)
            return GUILayout.TextField(value, maxLength, GUILayout.Height(36f));

        if (GUILayout.Button(string.IsNullOrEmpty(value) ? placeholder : value, GUILayout.Height(36f)))
        {
            keyboardField = field;
            var type = field == LobbyField.Name ? TouchScreenKeyboardType.Default : TouchScreenKeyboardType.ASCIICapable;
            keyboard = TouchScreenKeyboard.Open(value, type, false, false, false, false, placeholder, maxLength);
        }
        return value;
    }
}
