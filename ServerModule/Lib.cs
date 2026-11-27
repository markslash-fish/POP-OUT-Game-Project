using SpacetimeDB;

public static partial class Module
{
    public const int PlayersPerMatch = 3;

    [SpacetimeDB.Table(Accessor = "match_counter", Public = true)]
    public partial struct MatchCounter
    {
        [SpacetimeDB.PrimaryKey]
        public byte Id;
        public ulong NextMatchId;
    }

    [SpacetimeDB.Table(Accessor = "match_state", Public = true)]
    public partial struct MatchState
    {
        [SpacetimeDB.PrimaryKey]
        public ulong MatchId;
        public ulong PlayerCount;
        public bool Started;
        public ulong CreatedAt;
    }

    [SpacetimeDB.Table(Accessor = "match_queue", Public = true)]
    public partial struct MatchQueueEntry
    {
        [SpacetimeDB.PrimaryKey]
        public Identity Identity;
        [SpacetimeDB.Index.BTree]
        public ulong MatchId;
        public ulong QueuedAt;
    }

    [SpacetimeDB.Table(Accessor = "player_state", Public = true)]
    public partial struct PlayerState
    {
        [SpacetimeDB.PrimaryKey]
        [SpacetimeDB.AutoInc]
        public ulong Id;
        [SpacetimeDB.Unique]
        public Identity Identity;
        [SpacetimeDB.Index.BTree]
        public ulong MatchId;
        public float X;
        public float Y;
        public float Z;
        public float Yaw;
        public uint Animation;
        public ulong UpdatedAt;
    }

    private static ulong Now() => (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static ulong NextMatchId(ReducerContext ctx)
    {
        MatchCounter? counter = ctx.Db.match_counter.Id.Find(0);
        if (counter is null)
        {
            ctx.Db.match_counter.Insert(new MatchCounter { Id = 0, NextMatchId = 2 });
            return 1;
        }

        ulong matchId = counter.Value.NextMatchId;
        ctx.Db.match_counter.Id.Update(new MatchCounter { Id = 0, NextMatchId = matchId + 1 });
        return matchId;
    }

    private static MatchState CreateMatch(ReducerContext ctx)
    {
        var match = new MatchState
        {
            MatchId = NextMatchId(ctx),
            PlayerCount = 0,
            Started = false,
            CreatedAt = Now()
        };
        ctx.Db.match_state.Insert(match);
        return match;
    }

    private static MatchState? OpenMatch(ReducerContext ctx)
    {
        foreach (MatchState match in ctx.Db.match_state.Iter())
        {
            if (!match.Started && match.PlayerCount < PlayersPerMatch)
            {
                return match;
            }
        }
        return null;
    }

    [SpacetimeDB.Reducer]
    public static void JoinQueue(ReducerContext ctx)
    {
        MatchQueueEntry? queued = ctx.Db.match_queue.Identity.Find(ctx.Sender);
        if (queued is not null)
        {
            return;
        }

        MatchState match = OpenMatch(ctx) ?? CreateMatch(ctx);
        ctx.Db.match_queue.Insert(new MatchQueueEntry
        {
            Identity = ctx.Sender,
            MatchId = match.MatchId,
            QueuedAt = Now()
        });

        match.PlayerCount += 1;
        if (match.PlayerCount >= PlayersPerMatch)
        {
            match.Started = true;
        }
        ctx.Db.match_state.MatchId.Update(match);
    }

    [SpacetimeDB.Reducer]
    public static void LeaveQueue(ReducerContext ctx)
    {
        MatchQueueEntry? queued = ctx.Db.match_queue.Identity.Find(ctx.Sender);
        if (queued is null)
        {
            return;
        }

        ctx.Db.match_queue.Identity.Delete(ctx.Sender);
        MatchState? match = ctx.Db.match_state.MatchId.Find(queued.Value.MatchId);
        if (match is null)
        {
            return;
        }

        MatchState updated = match.Value;
        updated.PlayerCount = updated.PlayerCount > 0 ? updated.PlayerCount - 1 : 0;
        if (updated.PlayerCount == 0)
        {
            updated.Started = false;
        }
        ctx.Db.match_state.MatchId.Update(updated);
    }

    [SpacetimeDB.Reducer]
    public static void UpdatePlayerState(ReducerContext ctx, float x, float y, float z, float yaw, uint animation)
    {
        MatchQueueEntry? queued = ctx.Db.match_queue.Identity.Find(ctx.Sender);
        ulong matchId = queued?.MatchId ?? 0;
        PlayerState? existing = ctx.Db.player_state.Identity.Find(ctx.Sender);

        if (existing is PlayerState state)
        {
            state.MatchId = matchId;
            state.X = x;
            state.Y = y;
            state.Z = z;
            state.Yaw = yaw;
            state.Animation = animation;
            state.UpdatedAt = Now();
            ctx.Db.player_state.Id.Update(state);
            return;
        }

        ctx.Db.player_state.Insert(new PlayerState
        {
            Identity = ctx.Sender,
            MatchId = matchId,
            X = x,
            Y = y,
            Z = z,
            Yaw = yaw,
            Animation = animation,
            UpdatedAt = Now()
        });
    }

    [SpacetimeDB.Reducer]
    public static void SetPlayerAnimation(ReducerContext ctx, uint animation)
    {
        MatchQueueEntry? queued = ctx.Db.match_queue.Identity.Find(ctx.Sender);
        ulong matchId = queued?.MatchId ?? 0;
        PlayerState? existing = ctx.Db.player_state.Identity.Find(ctx.Sender);

        if (existing is PlayerState state)
        {
            state.Animation = animation;
            state.MatchId = matchId;
            state.UpdatedAt = Now();
            ctx.Db.player_state.Id.Update(state);
            return;
        }

        ctx.Db.player_state.Insert(new PlayerState
        {
            Identity = ctx.Sender,
            MatchId = matchId,
            Animation = animation,
            UpdatedAt = Now()
        });
    }
}
