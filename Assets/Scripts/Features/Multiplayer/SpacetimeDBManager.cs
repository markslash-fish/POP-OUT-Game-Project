using System;
using System.Collections.Generic;
using System.Linq;
using SpacetimeDB;
using SpacetimeDB.Types;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(SpacetimeDBNetworkManager))]
public class SpacetimeDBManager : MonoBehaviour
{
    private const string DefaultServerUri = "https://maincloud.spacetimedb.com";
    private const string DefaultDatabaseName = "pop-out-game";
    private const string GameSceneName = "GameScene";
    private const uint MovingBit = 1u;
    private const uint GroundedBit = 2u;
    private const uint AimingBit = 4u;
    private const uint ThrowingBit = 8u;
    private const uint JumpBit = 16u;

    [SerializeField] private string serverUri = DefaultServerUri;
    [SerializeField] private string databaseName = DefaultDatabaseName;
    [SerializeField] private float stateSendInterval = 0.05f;

    private static SpacetimeDBManager instance;

    private DbConnection connection;
    private Identity? localIdentity;
    private ulong currentMatchId;
    private bool connected;
    private bool subscriptionReady;
    private bool tableCallbacksBound;
    private bool queueRequested;
    private bool queueLeft;
    private bool loadingMatch;
    private bool jumpPulse;
    private float sendTimer;

    private Transform localPlayer;
    private PlayerMovement localMovement;
    private PlayerAnimator localAnimator;
    private InputReader boundInputReader;
    private GameObject queueButton;
    private GameObject loadingPanel;

    private readonly Dictionary<Identity, RemotePlayerView> remotePlayers = new();
    private readonly Dictionary<Identity, int> spawnIndices = new();

    public static SpacetimeDBManager Instance => instance;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        Application.quitting += HandleApplicationQuit;
        BindSceneObjects();
        Connect();
    }

    private void Update()
    {
        if (!connected || localPlayer == null || currentMatchId == 0)
        {
            return;
        }

        sendTimer -= Time.deltaTime;
        if (sendTimer > 0f)
        {
            return;
        }

        sendTimer = stateSendInterval;
        SendLocalState();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        Application.quitting -= HandleApplicationQuit;
        UnbindJumpInput();

        if (connection != null)
        {
            if (!queueLeft && localIdentity.HasValue)
            {
                connection.Reducers.LeaveQueue();
            }
            connection.Disconnect();
        }

        if (instance == this)
        {
            instance = null;
        }
    }

    private void Connect()
    {
        connection = new DbConnectionBuilder<DbConnection>()
            .WithUri(serverUri)
            .WithDatabaseName(databaseName)
            .WithAutomaticReconnect()
            .OnConnect(HandleConnected)
            .OnConnectError(HandleConnectError)
            .OnDisconnect(HandleDisconnected)
            .Build();
    }

    private void HandleConnected(DbConnection conn, Identity identity, string token)
    {
        connection = conn;
        localIdentity = identity;
        connected = true;
        queueLeft = false;
        Debug.Log($"SpacetimeDB connected to {databaseName} as {identity}");
        RefreshLocalMatchId();
        BindTableCallbacks(conn);
        Subscribe(conn);

        if (SceneManager.GetActiveScene().name == GameSceneName)
        {
            PlaceLocalPlayer();
        }
    }

    private void HandleConnectError(Exception exception)
    {
        connected = false;
        Debug.LogError($"SpacetimeDB connect failed: {exception.Message}");
    }

    private void HandleDisconnected(DbConnection conn, Exception exception)
    {
        connected = false;
        if (exception != null)
        {
            Debug.LogWarning($"SpacetimeDB disconnected: {exception.Message}");
        }
    }

    private void BindTableCallbacks(DbConnection conn)
    {
        if (tableCallbacksBound)
        {
            return;
        }

        conn.Db.MatchQueue.OnInsert += (_, row) => HandleQueueRow(row);
        conn.Db.MatchQueue.OnUpdate += (_, _, row) => HandleQueueRow(row);
        conn.Db.MatchQueue.OnDelete += (_, row) => HandleQueueDelete(row);
        conn.Db.MatchState.OnInsert += (_, row) => HandleMatchRow(row);
        conn.Db.MatchState.OnUpdate += (_, _, row) => HandleMatchRow(row);
        conn.Db.PlayerState.OnInsert += (_, row) => ApplyRemoteState(row);
        conn.Db.PlayerState.OnUpdate += (_, _, row) => ApplyRemoteState(row);
        conn.Db.PlayerState.OnDelete += (_, row) => RemoveRemoteState(row);
        tableCallbacksBound = true;
    }

    private void Subscribe(DbConnection conn)
    {
        conn.SubscriptionBuilder()
            .OnApplied(HandleSubscriptionApplied)
            .OnError((_, exception) => Debug.LogError($"SpacetimeDB subscribe failed: {exception.Message}"))
            .AddQuery(qb => qb.From.MatchQueue())
            .AddQuery(qb => qb.From.MatchState())
            .AddQuery(qb => qb.From.PlayerState())
            .Subscribe();
    }

    private void HandleSubscriptionApplied(SubscriptionEventContext context)
    {
        subscriptionReady = true;
        RefreshLocalMatchId();
        SyncRemotePlayers();

        if (queueRequested && currentMatchId == 0)
        {
            connection.Reducers.JoinQueue();
        }
    }

    public void JoinQueue()
    {
        queueRequested = true;
        queueLeft = false;
        ShowLoadingPanel();

        if (!connected || !subscriptionReady || connection == null)
        {
            return;
        }

        connection.Reducers.JoinQueue();
    }

    public void LeaveQueue()
    {
        queueRequested = false;
        queueLeft = true;

        if (!connected || connection == null)
        {
            return;
        }

        connection.Reducers.LeaveQueue();
        currentMatchId = 0;
        ClearRemotePlayers();
    }

    private void HandleQueueRow(MatchQueueEntry row)
    {
        if (!IsLocal(row.Identity))
        {
            return;
        }

        ulong previousMatchId = currentMatchId;
        currentMatchId = row.MatchId;

        if (previousMatchId != currentMatchId)
        {
            ClearRemotePlayers();
        }
    }

    private void HandleQueueDelete(MatchQueueEntry row)
    {
        if (!IsLocal(row.Identity))
        {
            return;
        }

        currentMatchId = 0;
        ClearRemotePlayers();
    }

    private void HandleMatchRow(MatchState row)
    {
        if (currentMatchId == 0 || row.MatchId != currentMatchId || !row.Started)
        {
            return;
        }

        ShowLoadingPanel();

        if (!loadingMatch && SceneManager.GetActiveScene().name != GameSceneName)
        {
            loadingMatch = true;
            SceneManager.LoadScene(GameSceneName);
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        loadingMatch = false;
        BindSceneObjects();
        SyncRemotePlayers();
    }

    private void BindSceneObjects()
    {
        ClearRemotePlayers();
        UnbindJumpInput();

        localPlayer = null;
        localMovement = null;
        localAnimator = null;

        GameObject playerObject = GameObject.Find("Player_Prefab");
        if (playerObject != null)
        {
            localPlayer = playerObject.transform;
            localMovement = playerObject.GetComponent<PlayerMovement>();
            localAnimator = playerObject.GetComponent<PlayerAnimator>();

            if (InputReader.Instance != null)
            {
                boundInputReader = InputReader.Instance;
                boundInputReader.onJumpStarted += HandleLocalJump;
            }

            if (SceneManager.GetActiveScene().name == GameSceneName)
            {
                PlaceLocalPlayer();
            }
        }

        GameObject buttonObject = GameObject.Find("FindMatchButton");
        if (buttonObject != null)
        {
            queueButton = buttonObject;
            Button button = buttonObject.GetComponent<Button>();
            if (button == null)
            {
                button = buttonObject.AddComponent<Button>();
            }
            button.onClick.RemoveListener(JoinQueue);
            button.onClick.AddListener(JoinQueue);
        }

        loadingPanel = Resources.FindObjectsOfTypeAll<GameObject>()
            .FirstOrDefault(go => go.name == "InGameLoadingSceen" && go.scene.IsValid());

        if (queueRequested && SceneManager.GetActiveScene().name != GameSceneName)
        {
            ShowLoadingPanel();
        }
    }

    private void PlaceLocalPlayer()
    {
        if (localPlayer == null || !localIdentity.HasValue)
        {
            return;
        }

        GameSpawnManager.RefreshSpawnPoints();
        int spawnIndex = GetSpawnIndex(localIdentity.Value);
        spawnIndices[localIdentity.Value] = spawnIndex;
        GameSpawnManager.Place(localPlayer, spawnIndex);

        if (localMovement != null)
        {
            localMovement.movement = Vector2.zero;
        }
    }

    private int GetSpawnIndex(Identity identity)
    {
        if (connection == null)
        {
            return 0;
        }

        List<MatchQueueEntry> entries = connection.Db.MatchQueue.Iter()
            .Where(entry => currentMatchId == 0 || entry.MatchId == currentMatchId)
            .OrderBy(entry => entry.QueuedAt)
            .ThenBy(entry => entry.Identity.ToString())
            .ToList();

        int index = entries.FindIndex(entry => entry.Identity.Equals(identity));
        return index >= 0 ? index % GameSpawnManager.PlayerLimit : 0;
    }

    private int GetRemoteSpawnIndex(Identity identity)
    {
        if (spawnIndices.TryGetValue(identity, out int assignedIndex))
        {
            return assignedIndex;
        }

        int spawnIndex = GetSpawnIndex(identity);
        HashSet<int> usedIndices = new(spawnIndices.Values);
        for (int attempt = 0; attempt < GameSpawnManager.PlayerLimit && usedIndices.Contains(spawnIndex); attempt++)
        {
            spawnIndex = (spawnIndex + 1) % GameSpawnManager.PlayerLimit;
        }

        spawnIndices[identity] = spawnIndex;
        return spawnIndex;
    }

    private void ShowLoadingPanel()
    {
        if (loadingPanel != null)
        {
            loadingPanel.SetActive(true);
        }
    }

    private void HandleLocalJump()
    {
        jumpPulse = true;
    }

    private void UnbindJumpInput()
    {
        if (boundInputReader != null)
        {
            boundInputReader.onJumpStarted -= HandleLocalJump;
            boundInputReader = null;
        }
    }

    private void SendLocalState()
    {
        if (connection == null || localPlayer == null)
        {
            return;
        }

        uint animation = 0;

        if (localMovement != null)
        {
            if (localMovement.movement.sqrMagnitude > 0f)
            {
                animation |= MovingBit;
            }
            if (localMovement.isGrounded)
            {
                animation |= GroundedBit;
            }
        }

        if (localAnimator != null)
        {
            if (localAnimator.IsAiming)
            {
                animation |= AimingBit;
            }
            if (localAnimator.IsThrowAnimationActive)
            {
                animation |= ThrowingBit;
            }
        }

        if (jumpPulse)
        {
            animation |= JumpBit;
            jumpPulse = false;
        }

        Vector3 position = localPlayer.position;
        float yaw = localPlayer.eulerAngles.y;
        connection.Reducers.UpdatePlayerState(position.x, position.y, position.z, yaw, animation);
    }

    private void RefreshLocalMatchId()
    {
        if (!localIdentity.HasValue || connection == null)
        {
            return;
        }

        MatchQueueEntry row = connection.Db.MatchQueue.Identity.Find(localIdentity.Value);
        ulong previousMatchId = currentMatchId;
        currentMatchId = row != null ? row.MatchId : 0;

        if (previousMatchId != currentMatchId)
        {
            ClearRemotePlayers();
        }
    }

    private void SyncRemotePlayers()
    {
        if (connection == null || currentMatchId == 0 || localPlayer == null)
        {
            return;
        }

        foreach (PlayerState state in connection.Db.PlayerState.Iter())
        {
            ApplyRemoteState(state);
        }
    }

    private void ApplyRemoteState(PlayerState state)
    {
        if (localPlayer == null || currentMatchId == 0 || state.MatchId != currentMatchId)
        {
            return;
        }

        if (localIdentity.HasValue && state.Identity.Equals(localIdentity.Value))
        {
            return;
        }

        if (!remotePlayers.TryGetValue(state.Identity, out RemotePlayerView view))
        {
            view = CreateRemoteView(state.Identity);
            if (view == null)
            {
                return;
            }
            remotePlayers.Add(state.Identity, view);
        }

        view.Apply(new Vector3(state.X, state.Y, state.Z), state.Yaw, state.Animation);
    }

    private RemotePlayerView CreateRemoteView(Identity identity)
    {
        if (localPlayer == null)
        {
            return null;
        }

        int spawnIndex = GetRemoteSpawnIndex(identity);
        Transform spawnPoint = GameSpawnManager.GetSpawnPoint(spawnIndex);
        Vector3 position = spawnPoint != null ? spawnPoint.position : localPlayer.position;
        Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : localPlayer.rotation;

        GameObject remote = Instantiate(localPlayer.gameObject, position, rotation);
        remote.name = $"RemotePlayer_{identity}";

        PlayerMovement movement = remote.GetComponent<PlayerMovement>();
        if (movement != null)
        {
            movement.enabled = false;
        }

        PlayerAttackMechanics attack = remote.GetComponent<PlayerAttackMechanics>();
        if (attack != null)
        {
            attack.enabled = false;
        }

        Rigidbody body = remote.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.isKinematic = true;
            body.useGravity = false;
        }

        foreach (Collider collider in remote.GetComponentsInChildren<Collider>())
        {
            collider.enabled = false;
        }

        RemotePlayerView view = remote.GetComponent<RemotePlayerView>();
        if (view == null)
        {
            view = remote.AddComponent<RemotePlayerView>();
        }
        return view;
    }

    private void RemoveRemoteState(PlayerState state)
    {
        if (!remotePlayers.TryGetValue(state.Identity, out RemotePlayerView view))
        {
            return;
        }

        remotePlayers.Remove(state.Identity);
        if (view != null)
        {
            Destroy(view.gameObject);
        }
    }

    private void ClearRemotePlayers()
    {
        foreach (RemotePlayerView view in remotePlayers.Values)
        {
            if (view != null)
            {
                Destroy(view.gameObject);
            }
        }
        remotePlayers.Clear();
        spawnIndices.Clear();
    }

    private bool IsLocal(Identity identity)
    {
        return localIdentity.HasValue && identity.Equals(localIdentity.Value);
    }

    private void HandleApplicationQuit()
    {
        LeaveQueue();
    }
}
