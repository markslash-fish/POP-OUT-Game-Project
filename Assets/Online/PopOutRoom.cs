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
    const float PoseInterval = 0.1f;
    const float PoseHeartbeat = 0.5f;
    const float HorizontalPoseThreshold = 0.04f;
    const float VerticalPoseThreshold = 0.15f;
    const byte BalloonAction = 1;
    const byte MeleeAction = 2;
    const string SixPlayerRoomPrefix = "S6";
    static readonly string[] RoomSizes = { "2 players", "6 players" };
    static readonly int SpeedParameter = Animator.StringToHash("Speed");

    [SerializeField] HudController hud;
    [SerializeField] GameObject[] offlinePlayers;

    readonly Dictionary<Identity, PopOutRemotePlayer> opponents = new();
    readonly Dictionary<Health, Identity> remoteIdentities = new();

    MatchManager match;
    ScoreManager scores;
    Health local;
    RespawnSystem respawn;
    PlayerColorIdentity localColor;
    Thrower thrower;
    MeleeCombat melee;
    Animator animator;
    GameObject replicaTemplate;
    SpawnPoint[] spawnPoints;
    DbConnection connection;
    Identity identity;
    string roomCode = "POPOUT";
    string message = "";
    string configuredName;
    int roomSize = 6;
    TouchScreenKeyboard keyboard;
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
    float joinDeadline;
    int MaxRoomCodeLength => roomSize == 6 ? 12 - SixPlayerRoomPrefix.Length : 12;

    void Awake()
    {
        match = GetComponent<MatchManager>();
        scores = GetComponent<ScoreManager>();
        local = hud.localPlayer;
        respawn = local.GetComponent<RespawnSystem>();
        localColor = local.GetComponent<PlayerColorIdentity>();
        thrower = hud.thrower;
        melee = local.GetComponentInChildren<MeleeCombat>();
        animator = melee.animator != null ? melee.animator : melee.GetComponent<Animator>();

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
        melee.SwingStarted += SendMelee;
        Health.AnyDied += ReportDeath;
        hud.OnlinePlayAgain = Leave;
        Application.runInBackground = true;
        if (Application.platform == RuntimePlatform.Android) Application.targetFrameRate = 60;
    }

    void Start()
    {
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
                configuredName = args[++i];
            }
            else if (args[i] == "-popout-players" && int.TryParse(args[i + 1], out int size) &&
                (size == 2 || size == 6))
            {
                roomSize = size;
                i++;
            }
        }
        if (automaticJoin) Join();
    }

    void Join()
    {
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
                string playerName = string.IsNullOrWhiteSpace(configuredName)
                    ? "Player " + playerIdentity.ToString()[^4..].ToUpperInvariant()
                    : configuredName;
                string databaseRoom = roomSize == 6 ? SixPlayerRoomPrefix + roomCode : roomCode;
                conn.Reducers.JoinRoom(databaseRoom, playerName);
            })
            .OnError((_, error) => JoinFailed(error.Message))
            .Subscribe(new[] { "SELECT * FROM room_players" });
    }

    void Update()
    {
        connection?.FrameTick();
        StartWhenReady();

        if (connection != null && !joined && Time.unscaledTime >= joinDeadline)
            JoinFailed("The room did not respond. Try again.");

        if (keyboard != null && keyboard.status != TouchScreenKeyboard.Status.Visible)
        {
            if (keyboard.status == TouchScreenKeyboard.Status.Done) roomCode = keyboard.text;
            keyboard = null;
        }

        float now = Time.unscaledTime;
        if (!joined || connection == null || now < nextPose ||
            (match.State == MatchState.Ended && started)) return;

        nextPose = now + PoseInterval;
        Vector3 position = local.Position;
        float yaw = animator.transform.eulerAngles.y;
        float speed = Mathf.Clamp01(animator.GetFloat(SpeedParameter));
        float health = Mathf.Clamp01(local.Current / local.maxHealth);
        bool shield = local.IsInvulnerable;
        Vector3 movement = position - lastSentPosition;

        // Idle ragdolls still wobble slightly. A heartbeat keeps their pose fresh.
        if (now < nextPoseHeartbeat && shield == lastSentShield && health == lastSentHealth &&
            Mathf.Abs(Mathf.DeltaAngle(lastSentYaw, yaw)) < 0.5f &&
            Mathf.Abs(speed - lastSentSpeed) < 0.02f &&
            Mathf.Abs(movement.y) < VerticalPoseThreshold &&
            movement.x * movement.x + movement.z * movement.z < HorizontalPoseThreshold * HorizontalPoseThreshold)
            return;

        connection.Reducers.UpdatePose(position.x, position.y, position.z, yaw, speed, health, shield);
        nextPoseHeartbeat = now + PoseHeartbeat;
        lastSentPosition = position;
        lastSentYaw = yaw;
        lastSentSpeed = speed;
        lastSentHealth = health;
        lastSentShield = shield;
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

        remote.Apply(row);
        if (match.State != MatchState.Ended) scores.SetScore(remote.Health, (int)row.Score);
        if (!newAction || match.State != MatchState.Playing) return;

        var position = new Vector3(row.ActionX, row.ActionY, row.ActionZ);
        switch (row.Action)
        {
            case BalloonAction:
                remote.Throw(position, new Vector3(row.ActionVx, row.ActionVy, row.ActionVz));
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

    void SendBalloon(Vector3 position, Vector3 velocity, float yaw)
    {
        SendAction(BalloonAction, position, velocity, yaw);
    }

    void SendMelee(Vector3 position, float yaw)
    {
        SendAction(MeleeAction, position, Vector3.zero, yaw);
    }

    void SendAction(byte action, Vector3 position, Vector3 velocity, float yaw)
    {
        if (!joined || match.State != MatchState.Playing) return;
        connection?.Reducers.PerformAction(action, position.x, position.y, position.z,
            velocity.x, velocity.y, velocity.z, yaw);
    }

    void ReportDeath(Health victim, Health killer, DeathCause _)
    {
        if (!joined || victim != local || match.State != MatchState.Playing) return;
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
        if (thrower != null) thrower.BalloonReleased -= SendBalloon;
        if (melee != null) melee.SwingStarted -= SendMelee;
        Disconnect();
    }

    void OnGUI()
    {
        Rect safe = Screen.safeArea;
        float scale = Mathf.Clamp(Mathf.Min(safe.width / 700f, safe.height / 400f), 0.6f, 2.5f);
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(new Vector3(safe.x, Screen.height - safe.yMax), Quaternion.identity,
            new Vector3(scale, scale, 1f));
        float width = safe.width / scale;
        float height = safe.height / scale;
        if (joined)
        {
            bool showMessage = connection == null || !started;
            GUILayout.BeginArea(new Rect(width - 232f, 12f, 220f, showMessage ? 88f : 40f), GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{roomCode}  •  {opponents.Count + 1}/{roomSize}");
            if (GUILayout.Button("Leave", GUILayout.Width(70f), GUILayout.Height(28f))) Leave();
            GUILayout.EndHorizontal();
            if (connection == null)
                GUILayout.Label(message);
            else if (!started)
                GUILayout.Label("Waiting for players…");
            GUILayout.EndArea();
        }
        else
        {
            GUILayout.BeginArea(new Rect((width - 330f) / 2f, (height - 225f) / 2f, 330f, 225f), GUI.skin.box);
            GUILayout.Label("POP OUT MULTIPLAYER");
            if (connection == null)
                roomSize = GUILayout.SelectionGrid(roomSize == 6 ? 1 : 0, RoomSizes, 2, GUILayout.Height(30f)) == 0 ? 2 : 6;
            GUILayout.Label("Room code");
            if (connection == null)
            {
                if (TouchScreenKeyboard.isSupported)
                {
                    if (GUILayout.Button(roomCode, GUILayout.Height(36f)))
                        keyboard = TouchScreenKeyboard.Open(roomCode, TouchScreenKeyboardType.ASCIICapable);
                }
                else roomCode = GUILayout.TextField(roomCode, MaxRoomCodeLength, GUILayout.Height(32f));

                if (GUILayout.Button("Join room", GUILayout.Height(36f))) Join();
            }
            GUILayout.Label(message);
            GUILayout.EndArea();
        }
        GUI.matrix = previous;
    }
}
