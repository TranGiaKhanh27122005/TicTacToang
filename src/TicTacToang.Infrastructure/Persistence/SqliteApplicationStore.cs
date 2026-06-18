using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TicTacToang.Application.Abstractions;
using TicTacToang.Domain.Matches;
using TicTacToang.Domain.Players;
using TicTacToang.Domain.Rooms;
using TicTacToang.Domain.Social;
using TicTacToang.Infrastructure.Security;

namespace TicTacToang.Infrastructure.Persistence;

public sealed class SqliteApplicationStore : IApplicationStore
{
    private const int LegacyStateId = 1;
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public List<Player> Players { get; private set; } = [];
    public List<Match> Matches { get; private set; } = [];
    public List<GameRoom> Rooms { get; private set; } = [];
    public List<FriendRequest> FriendRequests { get; private set; } = [];
    public List<Friendship> Friendships { get; private set; } = [];
    public List<RoomInvite> RoomInvites { get; private set; } = [];

    public SqliteApplicationStore(IDbContextFactory<ApplicationDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
        LoadOrSeedAsync().GetAwaiter().GetResult();
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await context.Database.EnsureCreatedAsync(cancellationToken);
            await EnsureSchemaAsync(context, cancellationToken);

            context.Players.RemoveRange(context.Players);
            context.Matches.RemoveRange(context.Matches);
            context.Rooms.RemoveRange(context.Rooms);
            context.FriendRequests.RemoveRange(context.FriendRequests);
            context.Friendships.RemoveRange(context.Friendships);
            context.RoomInvites.RemoveRange(context.RoomInvites);

            context.Players.AddRange(Players.Select(ToRecord));
            context.Matches.AddRange(Matches.Select(ToRecord));
            context.Rooms.AddRange(Rooms.Select(ToRecord));
            context.FriendRequests.AddRange(FriendRequests.Select(ToRecord));
            context.Friendships.AddRange(Friendships.Select(ToRecord));
            context.RoomInvites.AddRange(RoomInvites.Select(ToRecord));

            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task LoadOrSeedAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            await context.Database.EnsureCreatedAsync();
            await EnsureSchemaAsync(context);

            if (await context.Players.AnyAsync())
            {
                Players = await ReadPayloads<PlayerRecord, Player>(context.Players, record => record.Payload);
                Matches = await ReadPayloads<MatchRecord, Match>(context.Matches, record => record.Payload);
                Rooms = await ReadPayloads<GameRoomRecord, GameRoom>(context.Rooms, record => record.Payload);
                FriendRequests = await ReadPayloads<FriendRequestRecord, FriendRequest>(context.FriendRequests, record => record.Payload);
                Friendships = await ReadPayloads<FriendshipRecord, Friendship>(context.Friendships, record => record.Payload);
                RoomInvites = await ReadPayloads<RoomInviteRecord, RoomInvite>(context.RoomInvites, record => record.Payload);
                if (EnsureDemoData())
                {
                    context.Players.RemoveRange(context.Players);
                    context.Matches.RemoveRange(context.Matches);
                    context.Rooms.RemoveRange(context.Rooms);
                    context.FriendRequests.RemoveRange(context.FriendRequests);
                    context.Friendships.RemoveRange(context.Friendships);
                    context.RoomInvites.RemoveRange(context.RoomInvites);
                    await context.SaveChangesAsync();
                    await SaveCurrentStateAsync(context);
                }
                return;
            }

            var legacy = await context.ApplicationStates.AsNoTracking()
                .FirstOrDefaultAsync(state => state.Id == LegacyStateId);
            if (legacy is not null && !string.IsNullOrWhiteSpace(legacy.Payload))
            {
                var loaded = JsonSerializer.Deserialize<StoreState>(legacy.Payload, _options);
                if (loaded is not null)
                {
                    Apply(loaded);
                    await SaveCurrentStateAsync(context);
                    return;
                }
            }

            Seed();
            EnsureDemoData();
            await SaveCurrentStateAsync(context);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveCurrentStateAsync(ApplicationDbContext context)
    {
        context.Players.AddRange(Players.Select(ToRecord));
        context.Matches.AddRange(Matches.Select(ToRecord));
        context.Rooms.AddRange(Rooms.Select(ToRecord));
        context.FriendRequests.AddRange(FriendRequests.Select(ToRecord));
        context.Friendships.AddRange(Friendships.Select(ToRecord));
        context.RoomInvites.AddRange(RoomInvites.Select(ToRecord));
        await context.SaveChangesAsync();
    }

    private static async Task EnsureSchemaAsync(ApplicationDbContext context, CancellationToken cancellationToken = default)
    {
        if ((context.Database.ProviderName ?? "").Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await EnsurePostgresSchemaAsync(context, cancellationToken);
            return;
        }

        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Players" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Players" PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "Username" TEXT NOT NULL,
                "Email" TEXT NOT NULL,
                "Country" TEXT NOT NULL,
                "Role" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "IsPremium" INTEGER NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "LastLoginAt" TEXT NULL,
                "Payload" TEXT NOT NULL
            );
            """, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_Players_Username" ON "Players" ("Username");""", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_Players_Email" ON "Players" ("Email");""", cancellationToken);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Matches" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Matches" PRIMARY KEY,
                "Mode" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "BoardSize" INTEGER NOT NULL,
                "MoveCount" INTEGER NOT NULL,
                "PlayerXId" TEXT NULL,
                "PlayerOId" TEXT NULL,
                "StartedAt" TEXT NOT NULL,
                "CompletedAt" TEXT NULL,
                "Payload" TEXT NOT NULL
            );
            """, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_Matches_StartedAt" ON "Matches" ("StartedAt");""", cancellationToken);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Rooms" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Rooms" PRIMARY KEY,
                "RoomCode" INTEGER NOT NULL,
                "Name" TEXT NOT NULL,
                "HostId" TEXT NOT NULL,
                "Capacity" INTEGER NOT NULL,
                "Status" TEXT NOT NULL,
                "MemberCount" INTEGER NOT NULL,
                "BoardSize" INTEGER NOT NULL,
                "BoardStyle" TEXT NOT NULL,
                "Payload" TEXT NOT NULL
            );
            """, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_Rooms_RoomCode" ON "Rooms" ("RoomCode");""", cancellationToken);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "FriendRequests" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_FriendRequests" PRIMARY KEY,
                "RequesterId" TEXT NOT NULL,
                "RecipientId" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "Payload" TEXT NOT NULL
            );
            """, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_FriendRequests_RecipientId" ON "FriendRequests" ("RecipientId");""", cancellationToken);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Friendships" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Friendships" PRIMARY KEY,
                "UserA" TEXT NOT NULL,
                "UserB" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "Payload" TEXT NOT NULL
            );
            """, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_Friendships_UserA" ON "Friendships" ("UserA");""", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_Friendships_UserB" ON "Friendships" ("UserB");""", cancellationToken);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "RoomInvites" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_RoomInvites" PRIMARY KEY,
                "SenderId" TEXT NOT NULL,
                "RecipientId" TEXT NOT NULL,
                "RoomId" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "Payload" TEXT NOT NULL
            );
            """, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_RoomInvites_RecipientId" ON "RoomInvites" ("RecipientId");""", cancellationToken);
    }

    private static async Task EnsurePostgresSchemaAsync(ApplicationDbContext context, CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "ApplicationStates" (
                "Id" integer GENERATED BY DEFAULT AS IDENTITY,
                "Payload" text NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_ApplicationStates" PRIMARY KEY ("Id")
            );
            """, cancellationToken);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Players" (
                "Id" uuid NOT NULL,
                "Name" text NOT NULL,
                "Username" text NOT NULL,
                "Email" text NOT NULL,
                "Country" text NOT NULL,
                "Role" text NOT NULL,
                "Status" text NOT NULL,
                "IsPremium" boolean NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "LastLoginAt" timestamp with time zone NULL,
                "Payload" text NOT NULL,
                CONSTRAINT "PK_Players" PRIMARY KEY ("Id")
            );
            """, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_Players_Username" ON "Players" ("Username");""", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_Players_Email" ON "Players" ("Email");""", cancellationToken);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Matches" (
                "Id" text NOT NULL,
                "Mode" text NOT NULL,
                "Status" text NOT NULL,
                "BoardSize" integer NOT NULL,
                "MoveCount" integer NOT NULL,
                "PlayerXId" uuid NULL,
                "PlayerOId" uuid NULL,
                "StartedAt" timestamp with time zone NOT NULL,
                "CompletedAt" timestamp with time zone NULL,
                "Payload" text NOT NULL,
                CONSTRAINT "PK_Matches" PRIMARY KEY ("Id")
            );
            """, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_Matches_StartedAt" ON "Matches" ("StartedAt");""", cancellationToken);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Rooms" (
                "Id" uuid NOT NULL,
                "RoomCode" integer NOT NULL,
                "Name" text NOT NULL,
                "HostId" uuid NOT NULL,
                "Capacity" integer NOT NULL,
                "Status" text NOT NULL,
                "MemberCount" integer NOT NULL,
                "BoardSize" integer NOT NULL,
                "BoardStyle" text NOT NULL,
                "Payload" text NOT NULL,
                CONSTRAINT "PK_Rooms" PRIMARY KEY ("Id")
            );
            """, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_Rooms_RoomCode" ON "Rooms" ("RoomCode");""", cancellationToken);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "FriendRequests" (
                "Id" uuid NOT NULL,
                "RequesterId" uuid NOT NULL,
                "RecipientId" uuid NOT NULL,
                "Status" text NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "Payload" text NOT NULL,
                CONSTRAINT "PK_FriendRequests" PRIMARY KEY ("Id")
            );
            """, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_FriendRequests_RecipientId" ON "FriendRequests" ("RecipientId");""", cancellationToken);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Friendships" (
                "Id" text NOT NULL,
                "UserA" uuid NOT NULL,
                "UserB" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "Payload" text NOT NULL,
                CONSTRAINT "PK_Friendships" PRIMARY KEY ("Id")
            );
            """, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_Friendships_UserA" ON "Friendships" ("UserA");""", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_Friendships_UserB" ON "Friendships" ("UserB");""", cancellationToken);

        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "RoomInvites" (
                "Id" uuid NOT NULL,
                "SenderId" uuid NOT NULL,
                "RecipientId" uuid NOT NULL,
                "RoomId" uuid NOT NULL,
                "Status" text NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "Payload" text NOT NULL,
                CONSTRAINT "PK_RoomInvites" PRIMARY KEY ("Id")
            );
            """, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_RoomInvites_RecipientId" ON "RoomInvites" ("RecipientId");""", cancellationToken);
    }

    private async Task<List<TModel>> ReadPayloads<TRecord, TModel>(
        DbSet<TRecord> set,
        Func<TRecord, string> payload)
        where TRecord : class
    {
        var records = await set.AsNoTracking().ToListAsync();
        return records
            .Select(record => JsonSerializer.Deserialize<TModel>(payload(record), _options))
            .OfType<TModel>()
            .ToList();
    }

    private PlayerRecord ToRecord(Player player) => new()
    {
        Id = player.Id,
        Name = player.Name,
        Username = player.Username,
        Email = player.Email,
        Country = player.Country,
        Role = player.Role.ToString(),
        Status = player.Status.ToString(),
        IsPremium = player.HasPremiumAccess(DateTimeOffset.UtcNow),
        CreatedAt = player.CreatedAt,
        LastLoginAt = player.LastLoginAt,
        Payload = JsonSerializer.Serialize(player, _options)
    };

    private MatchRecord ToRecord(Match match) => new()
    {
        Id = match.Id,
        Mode = match.Mode.ToString(),
        Status = match.Status.ToString(),
        BoardSize = match.BoardSize,
        MoveCount = match.Moves.Count,
        PlayerXId = match.PlayerX.PlayerId,
        PlayerOId = match.PlayerO.PlayerId,
        StartedAt = match.StartedAt,
        CompletedAt = match.CompletedAt,
        Payload = JsonSerializer.Serialize(match, _options)
    };

    private GameRoomRecord ToRecord(GameRoom room) => new()
    {
        Id = room.Id,
        RoomCode = room.RoomCode,
        Name = room.Name,
        HostId = room.HostId,
        Capacity = room.Capacity,
        Status = room.Status.ToString(),
        MemberCount = room.Members.Count,
        BoardSize = room.Settings.BoardSize,
        BoardStyle = room.Settings.BoardStyle,
        Payload = JsonSerializer.Serialize(room, _options)
    };

    private FriendRequestRecord ToRecord(FriendRequest request) => new()
    {
        Id = request.Id,
        RequesterId = request.RequesterId,
        RecipientId = request.RecipientId,
        Status = request.Status.ToString(),
        CreatedAt = request.CreatedAt,
        Payload = JsonSerializer.Serialize(request, _options)
    };

    private FriendshipRecord ToRecord(Friendship friendship)
    {
        var pair = new[] { friendship.UserA, friendship.UserB }.OrderBy(id => id).ToArray();
        return new FriendshipRecord
        {
            Id = $"{pair[0]:N}-{pair[1]:N}",
            UserA = pair[0],
            UserB = pair[1],
            CreatedAt = friendship.CreatedAt,
            Payload = JsonSerializer.Serialize(friendship, _options)
        };
    }

    private RoomInviteRecord ToRecord(RoomInvite invite) => new()
    {
        Id = invite.Id,
        SenderId = invite.SenderId,
        RecipientId = invite.RecipientId,
        RoomId = invite.RoomId,
        Status = invite.Status.ToString(),
        CreatedAt = invite.CreatedAt,
        Payload = JsonSerializer.Serialize(invite, _options)
    };

    private void Seed()
    {
        var passwords = new Pbkdf2PasswordService();
        var admin = new Player
        {
            Name = "Admin",
            Username = "admin",
            Email = "admin@tictactoang.com",
            Country = "Vietnam",
            PasswordHash = passwords.Hash("Admin@1234"),
            Role = PlayerRole.Admin
        };
        var playerA = new Player
        {
            Name = "Player A",
            Username = "playera",
            Email = "playera@tictactoang.com",
            Country = "Vietnam",
            PasswordHash = passwords.Hash("PlayerA@123")
        };
        playerA.ActivatePremium(DateTimeOffset.UtcNow.AddMonths(1));
        var playerB = new Player
        {
            Name = "Player B",
            Username = "playerb",
            Email = "playerb@tictactoang.com",
            Country = "Vietnam",
            PasswordHash = passwords.Hash("PlayerB@123")
        };

        Players.AddRange([admin, playerA, playerB]);
    }

    private bool EnsureDemoData()
    {
        var changed = false;
        var passwords = new Pbkdf2PasswordService();

        changed |= AddDemoPlayer("Mina Tran", "minat", "mina@tictactoang.com", "Vietnam", true, AccountStatus.Active, PlayerRole.Player, passwords);
        changed |= AddDemoPlayer("Noah Lee", "noahl", "noah@tictactoang.com", "Singapore", false, AccountStatus.Active, PlayerRole.Player, passwords);
        changed |= AddDemoPlayer("Ava Chen", "avac", "ava@tictactoang.com", "Taiwan", true, AccountStatus.Active, PlayerRole.Moderator, passwords);
        changed |= AddDemoPlayer("Liam Pham", "liamp", "liam@tictactoang.com", "Vietnam", false, AccountStatus.Inactive, PlayerRole.Player, passwords);
        changed |= AddDemoPlayer("Sora Kim", "sorak", "sora@tictactoang.com", "South Korea", false, AccountStatus.Active, PlayerRole.Player, passwords);
        changed |= AddDemoPlayer("Kai Nguyen", "kaing", "kai@tictactoang.com", "Vietnam", true, AccountStatus.Active, PlayerRole.Player, passwords);

        if (Matches.Count == 0)
        {
            AddDemoMatches();
            changed = true;
        }

        if (Rooms.Count == 0)
        {
            AddDemoRooms();
            changed = true;
        }

        if (Friendships.Count == 0 || FriendRequests.Count == 0 || RoomInvites.Count == 0)
        {
            AddDemoSocialData();
            changed = true;
        }

        return changed;
    }

    private bool AddDemoPlayer(
        string name,
        string username,
        string email,
        string country,
        bool premium,
        AccountStatus status,
        PlayerRole role,
        Pbkdf2PasswordService passwords)
    {
        if (Players.Any(player => player.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var player = new Player
        {
            Name = name,
            Username = username,
            Email = email,
            Country = country,
            PasswordHash = passwords.Hash("Demo@1234"),
            Role = role,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-Random.Shared.Next(3, 45)),
            LastLoginAt = DateTimeOffset.UtcNow.AddHours(-Random.Shared.Next(1, 120)),
            LastSeenAt = DateTimeOffset.UtcNow.AddMinutes(-Random.Shared.Next(10, 600))
        };

        if (premium)
        {
            player.ActivatePremium(DateTimeOffset.UtcNow.AddMonths(1));
        }

        Players.Add(player);
        return true;
    }

    private void AddDemoMatches()
    {
        var playerA = FindPlayer("playera");
        var playerB = FindPlayer("playerb");
        var mina = FindPlayer("minat");
        var noah = FindPlayer("noahl");
        var ava = FindPlayer("avac");
        var kai = FindPlayer("kaing");

        var classicWin = Match.Create(
            GameMode.Multiplayer,
            15,
            240,
            new MatchPlayer(playerA.Id, playerA.Name, Marker.X, playerA.Avatar),
            new MatchPlayer(mina.Id, mina.Name, Marker.O, mina.Avatar));
        PlaySequence(classicWin, playerA.Id, mina.Id, [(0, 0), (1, 0), (0, 1), (1, 1), (0, 2), (1, 2), (0, 3), (1, 3), (0, 4)]);

        var aiWin = Match.Create(
            GameMode.SinglePlayer,
            10,
            360,
            new MatchPlayer(noah.Id, noah.Name, Marker.X, noah.Avatar),
            new MatchPlayer(null, "AI (Medium)", Marker.O, "", true, AiDifficulty.Medium));
        PlaySequence(aiWin, noah.Id, null, [(4, 4), (0, 0), (5, 4), (0, 1), (6, 4), (0, 2), (7, 4), (0, 3), (8, 4)]);

        var activeMatch = Match.Create(
            GameMode.Multiplayer,
            15,
            600,
            new MatchPlayer(ava.Id, ava.Name, Marker.X, ava.Avatar),
            new MatchPlayer(kai.Id, kai.Name, Marker.O, kai.Avatar));
        PlaySequence(activeMatch, ava.Id, kai.Id, [(7, 7), (7, 8), (8, 8), (8, 9), (9, 9), (9, 10)]);

        var waitingMatch = Match.Create(
            GameMode.Multiplayer,
            10,
            480,
            new MatchPlayer(kai.Id, kai.Name, Marker.X, kai.Avatar),
            new MatchPlayer(null, "Waiting Player", Marker.O));

        var abandonedMatch = Match.Create(
            GameMode.Multiplayer,
            10,
            240,
            new MatchPlayer(playerB.Id, playerB.Name, Marker.X, playerB.Avatar),
            new MatchPlayer(noah.Id, noah.Name, Marker.O, noah.Avatar));
        PlaySequence(abandonedMatch, playerB.Id, noah.Id, [(2, 2), (2, 3), (3, 3)]);
        abandonedMatch.Abort();

        Matches.AddRange([classicWin, aiWin, activeMatch, waitingMatch, abandonedMatch]);
    }

    private static void PlaySequence(Match match, Guid? playerXId, Guid? playerOId, IReadOnlyList<(int Row, int Column)> moves)
    {
        for (var index = 0; index < moves.Count; index++)
        {
            var playerId = index % 2 == 0 ? playerXId : playerOId;
            match.Play(playerId, moves[index].Row, moves[index].Column, Random.Shared.Next(3, 18));
        }
    }

    private void AddDemoRooms()
    {
        var playerA = FindPlayer("playera");
        var mina = FindPlayer("minat");
        var ava = FindPlayer("avac");
        var kai = FindPlayer("kaing");

        var openRoom = new GameRoom
        {
            Name = "Evening Ranked Lobby",
            HostId = playerA.Id,
            Capacity = 2,
            Settings = new RoomSettings { BoardSize = 15, BoardStyle = "Classic", TimeToThinkSeconds = 240, Marker = "X", HostPosition = 1 }
        };
        openRoom.AddMember(new RoomMember(playerA.Id, playerA.Name, playerA.Avatar, false, null, "X", "#f6c94d"));
        openRoom.PostMessage(playerA.Id, playerA.Name, "Waiting for one more player.");

        var fullRoom = new GameRoom
        {
            Name = "Premium Practice Table",
            HostId = mina.Id,
            Capacity = 2,
            Settings = new RoomSettings { BoardSize = 10, BoardStyle = "Modern", TimeToThinkSeconds = 360, Marker = "★", HostPosition = 1 }
        };
        fullRoom.AddMember(new RoomMember(mina.Id, mina.Name, mina.Avatar, false, null, "★", "#f6c94d"));
        fullRoom.AddMember(new RoomMember(null, "AI (Hard)", "", true, AiDifficulty.Hard, "O", "#f27d68"));
        fullRoom.PostMessage(mina.Id, mina.Name, "Trying hard AI tonight.");

        var battleRoom = new GameRoom
        {
            Name = "Moderator Review Room",
            HostId = ava.Id,
            Capacity = 3,
            Settings = new RoomSettings { BoardSize = 15, BoardStyle = "Minimal", TimeToThinkSeconds = 600, Marker = "▲", HostPosition = 2 }
        };
        battleRoom.AddMember(new RoomMember(ava.Id, ava.Name, ava.Avatar, false, null, "▲", "#f6c94d"));
        battleRoom.AddMember(new RoomMember(kai.Id, kai.Name, kai.Avatar, false, null, "O", "#f27d68"));
        battleRoom.PostMessage(kai.Id, kai.Name, "Ready when you are.");
        battleRoom.Start(ava.Id);

        Rooms.AddRange([openRoom, fullRoom, battleRoom]);
    }

    private void AddDemoSocialData()
    {
        var playerA = FindPlayer("playera");
        var playerB = FindPlayer("playerb");
        var mina = FindPlayer("minat");
        var noah = FindPlayer("noahl");
        var ava = FindPlayer("avac");
        var kai = FindPlayer("kaing");

        AddFriendship(playerA.Id, mina.Id);
        AddFriendship(ava.Id, kai.Id);
        AddFriendship(noah.Id, playerB.Id);

        if (!FriendRequests.Any())
        {
            FriendRequests.AddRange([
                new FriendRequest { RequesterId = noah.Id, RecipientId = playerA.Id },
                new FriendRequest { RequesterId = kai.Id, RecipientId = mina.Id }
            ]);
        }

        var room = Rooms.FirstOrDefault();
        if (room is not null && !RoomInvites.Any())
        {
            RoomInvites.AddRange([
                new RoomInvite { SenderId = playerA.Id, RecipientId = playerB.Id, RoomId = room.Id },
                new RoomInvite { SenderId = mina.Id, RecipientId = ava.Id, RoomId = room.Id }
            ]);
        }
    }

    private void AddFriendship(Guid first, Guid second)
    {
        var pair = new[] { first, second }.OrderBy(id => id).ToArray();
        if (Friendships.Any(friendship => friendship.UserA == pair[0] && friendship.UserB == pair[1]))
        {
            return;
        }

        Friendships.Add(new Friendship(pair[0], pair[1], DateTimeOffset.UtcNow.AddDays(-Random.Shared.Next(1, 20))));
    }

    private Player FindPlayer(string username) => Players.First(player => player.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

    private void Apply(StoreState state)
    {
        Players = state.Players;
        Matches = state.Matches;
        Rooms = state.Rooms;
        FriendRequests = state.FriendRequests;
        Friendships = state.Friendships;
        RoomInvites = state.RoomInvites;
    }

    private sealed record StoreState(
        List<Player> Players,
        List<Match> Matches,
        List<GameRoom> Rooms,
        List<FriendRequest> FriendRequests,
        List<Friendship> Friendships,
        List<RoomInvite> RoomInvites);
}
