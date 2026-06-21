using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TicTacToang.Domain.Matches;
using TicTacToang.Domain.Players;
using TicTacToang.Domain.Rooms;
using TicTacToang.Domain.Social;
using TicTacToang.Infrastructure.Persistence;

var options = ImportOptions.Parse(args);
if (!File.Exists(options.InputPath))
{
    Console.Error.WriteLine($"Export file not found: {options.InputPath}");
    return 1;
}

var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>();
if (options.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase) ||
    options.Provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
{
    dbOptions.UseNpgsql(options.ConnectionString);
}
else
{
    dbOptions.UseSqlite(options.ConnectionString);
}

using var document = JsonDocument.Parse(await File.ReadAllTextAsync(options.InputPath));
var root = document.RootElement;
var store = new SqliteApplicationStore(new ContextFactory(dbOptions.Options));
var importer = new MongoExportImporter(root);
var imported = importer.Build();

if (options.Replace)
{
    store.Players.Clear();
    store.Matches.Clear();
    store.Rooms.Clear();
    store.FriendRequests.Clear();
    store.Friendships.Clear();
    store.RoomInvites.Clear();
}

Merge(store.Players, imported.Players, player => player.Id);
Merge(store.Matches, imported.Matches, match => match.Id);
Merge(store.Rooms, imported.Rooms, room => room.Id);
Merge(store.FriendRequests, imported.FriendRequests, request => request.Id);
Merge(store.Friendships, imported.Friendships, friendship => $"{friendship.UserA:N}:{friendship.UserB:N}");
Merge(store.RoomInvites, imported.RoomInvites, invite => invite.Id);
await store.SaveAsync();

Console.WriteLine($"Imported {imported.Players.Count} users, {imported.Matches.Count} matches, and {imported.Rooms.Count} rooms.");
Console.WriteLine($"Imported {imported.FriendRequests.Count} friend requests, {imported.Friendships.Count} friendships, and {imported.RoomInvites.Count} room invites.");
Console.WriteLine(options.Replace ? "Target data was replaced." : "Imported records were merged into target data.");
return 0;

static void Merge<T, TKey>(List<T> target, IEnumerable<T> source, Func<T, TKey> keySelector) where TKey : notnull
{
    var positions = target.Select((item, index) => (Key: keySelector(item), Index: index))
        .ToDictionary(item => item.Key, item => item.Index);
    foreach (var item in source)
    {
        var key = keySelector(item);
        if (positions.TryGetValue(key, out var index)) target[index] = item;
        else
        {
            positions[key] = target.Count;
            target.Add(item);
        }
    }
}

sealed class ContextFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext() => new(options);
}

sealed record ImportOptions(string InputPath, string Provider, string ConnectionString, bool Replace)
{
    public static ImportOptions Parse(string[] args)
    {
        string Read(string name, string fallback)
        {
            var index = Array.FindIndex(args, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        var input = Path.GetFullPath(Read("--input", Path.Combine("Data", "mongo-export.json")));
        var provider = Read("--provider", "Sqlite");
        var connection = Read("--connection", "Data Source=Data/tictactoang.db");
        return new(input, provider, connection, args.Contains("--replace", StringComparer.OrdinalIgnoreCase));
    }
}

sealed record ImportResult(
    List<Player> Players,
    List<Match> Matches,
    List<GameRoom> Rooms,
    List<FriendRequest> FriendRequests,
    List<Friendship> Friendships,
    List<RoomInvite> RoomInvites);

sealed class MongoExportImporter(JsonElement root)
{
    private readonly Dictionary<string, Guid> _players = new(StringComparer.OrdinalIgnoreCase);

    public ImportResult Build()
    {
        var players = BuildPlayers();
        return new ImportResult(players, BuildMatches(), BuildRooms(), BuildFriendRequests(), BuildFriendships(), BuildRoomInvites());
    }

    private List<Player> BuildPlayers()
    {
        var result = new List<Player>();
        foreach (var source in Array("users"))
        {
            var sourceId = Text(source, "_id");
            var id = StableGuid($"user:{sourceId}");
            _players[sourceId] = id;
            var player = new Player
            {
                Id = id,
                Name = Text(source, "name", Text(source, "username", "Imported Player")),
                Username = Text(source, "username", $"imported-{id:N}"),
                Email = Text(source, "email", $"{id:N}@imported.local").ToLowerInvariant(),
                PasswordHash = Text(source, "password"),
                Country = Text(source, "country", "Unknown"),
                Role = EnumValue(Text(source, "role"), PlayerRole.Player),
                Status = EnumValue(Text(source, "accountStatus"), AccountStatus.Active),
                Avatar = Text(source, "avatar", "Mambo.png"),
                LastLoginAt = Date(source, "lastLoginAt"),
                LastSeenAt = Date(source, "lastSeenAt"),
                CreatedAt = Date(source, "createdAt") ?? DateTimeOffset.UtcNow
            };
            if (Boolean(source, "isPremium"))
            {
                var end = Date(source, "subscriptionEndDate") ?? DateTimeOffset.UtcNow.AddMonths(1);
                if (end > DateTimeOffset.UtcNow) player.ActivatePremium(end);
            }
            result.Add(player);
        }
        return result;
    }

    private List<Match> BuildMatches()
    {
        var result = new List<Match>();
        foreach (var source in Array("games"))
        {
            var players = MatchPlayers(source);
            if (players.Count < 2) continue;
            var moves = Array(source, "moves").Select(move => new Move(
                Integer(move, "moveNumber", 1), MarkerValue(Text(move, "player")), Integer(move, "row"), Integer(move, "col"),
                Date(move, "timestamp") ?? DateTimeOffset.UtcNow, Integer(move, "timeTaken"))).ToList();
            var resultSource = Object(source, "result");
            var winnerText = Text(resultSource, "winner");
            var matchResult = new MatchResult
            {
                Winner = string.IsNullOrWhiteSpace(winnerText) || winnerText.Equals("draw", StringComparison.OrdinalIgnoreCase) ? null : MarkerValue(winnerText),
                IsDraw = winnerText.Equals("draw", StringComparison.OrdinalIgnoreCase),
                Reason = WinReasonValue(Text(resultSource, "winReason")),
                WinningTiles = Array(resultSource, "winningTiles").Select(tile => new Coordinate(Integer(tile, "row"), Integer(tile, "col"))).ToList()
            };
            var id = Text(source, "gameId", $"import-{Text(source, "_id")}");
            result.Add(Match.Restore(id, ModeValue(Text(source, "gameMode")), Integer(source, "boardSize", 15),
                Integer(source, "timeControl", 240), players, MarkerValue(Text(source, "currentTurn", "X")),
                EnumValue(Text(source, "status"), MatchStatus.Waiting), moves, matchResult,
                Date(source, "startedAt") ?? DateTimeOffset.UtcNow, Date(source, "completedAt")));
        }
        return result;
    }

    private List<MatchPlayer> MatchPlayers(JsonElement source)
    {
        var result = new List<MatchPlayer>();
        var participants = Array(source, "participants").OrderBy(item => Integer(item, "order", result.Count + 1)).ToList();
        foreach (var participant in participants.Take(3)) result.Add(ToMatchPlayer(participant, result.Count));
        if (result.Count >= 2) return result;
        var players = Object(source, "players");
        foreach (var key in new[] { "X", "O" })
        {
            if (players.ValueKind == JsonValueKind.Object && players.TryGetProperty(key, out var player)) result.Add(ToMatchPlayer(player, result.Count));
        }
        return result;
    }

    private MatchPlayer ToMatchPlayer(JsonElement source, int index)
    {
        var isAi = Boolean(source, "isAI");
        var sourceId = Text(source, "playerId");
        return new MatchPlayer(isAi || string.IsNullOrWhiteSpace(sourceId) ? null : PlayerId(sourceId),
            Text(source, "playerName", isAi ? "AI" : "Imported Player"), index switch { 0 => Marker.X, 1 => Marker.O, _ => Marker.A },
            Text(source, "avatar"), isAi, Difficulty(Text(source, "aiDifficulty")), Integer(source, "playerRank", 1200));
    }

    private List<GameRoom> BuildRooms()
    {
        var result = new List<GameRoom>();
        foreach (var source in Array("rooms"))
        {
            var mongoId = Text(source, "_id");
            var settingsSource = Object(source, "gameSettings");
            var members = Array(source, "players").Select(member => new RoomMember(
                Text(member, "type").Equals("ai", StringComparison.OrdinalIgnoreCase) ? null : PlayerId(Text(member, "userId")),
                Text(member, "name", "Imported Player"), Text(member, "avatar"), Text(member, "type").Equals("ai", StringComparison.OrdinalIgnoreCase),
                Difficulty(Text(member, "aiDifficulty")), Text(member, "marker", "X"), Text(member, "markerColor", "#7aa2ff"))).ToList();
            var messages = Array(source, "chatMessages").Select(message => new ChatMessage(
                StableGuid($"chat:{Text(message, "id", Text(message, "_id"))}"), PlayerIdOrNull(Text(message, "senderId")),
                Text(message, "senderName", "Player"), Text(message, "text"), Date(message, "createdAt") ?? DateTimeOffset.UtcNow)).ToList();
            var boardSizeText = Text(settingsSource, "boardSize", "10x10");
            var roomCode = Integer(source, "roomId", Math.Abs(StableGuid(mongoId).GetHashCode() % 900000) + 100000);
            result.Add(GameRoom.Restore(StableGuid($"room:{mongoId}"), roomCode, Text(source, "roomName", "Imported Room"),
                PlayerId(Text(source, "host")), Integer(source, "size", 2), RoomStatusValue(Text(source, "status")),
                new RoomSettings { BoardStyle = Text(settingsSource, "boardStyle", "Classic"), BoardSize = boardSizeText.StartsWith("15") ? 15 : 10,
                    Marker = Text(settingsSource, "marker", "X"), TimeToThinkSeconds = Integer(settingsSource, "timeToThink", 60), HostPosition = Integer(settingsSource, "hostPosition", 1) },
                members, messages));
        }
        return result;
    }

    private List<FriendRequest> BuildFriendRequests() => Array("friendRequests").Select(source => new FriendRequest
    {
        Id = StableGuid($"request:{Text(source, "_id")}"), RequesterId = PlayerId(Text(source, "requester")),
        RecipientId = PlayerId(Text(source, "recipient")), Status = EnumValue(Text(source, "status"), RequestStatus.Pending),
        CreatedAt = Date(source, "createdAt") ?? DateTimeOffset.UtcNow
    }).ToList();

    private List<Friendship> BuildFriendships() => Array("friendships").Select(source =>
    {
        var pair = new[] { PlayerId(Text(source, "userA")), PlayerId(Text(source, "userB")) }.Order().ToArray();
        return new Friendship(pair[0], pair[1], Date(source, "createdAt") ?? DateTimeOffset.UtcNow);
    }).ToList();

    private List<RoomInvite> BuildRoomInvites() => Array("roomInvites").Select(source => new RoomInvite
    {
        Id = StableGuid($"invite:{Text(source, "_id")}"), SenderId = PlayerId(Text(source, "sender")),
        RecipientId = PlayerId(Text(source, "recipient")), RoomId = StableGuid($"room:{Text(source, "roomMongoId")}"),
        Status = EnumValue(Text(source, "status"), RequestStatus.Pending), CreatedAt = Date(source, "createdAt") ?? DateTimeOffset.UtcNow
    }).ToList();

    private IEnumerable<JsonElement> Array(string name) => Array(root, name);
    private static IEnumerable<JsonElement> Array(JsonElement source, string name) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    private static JsonElement Object(JsonElement source, string name) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
    private static string Text(JsonElement source, string name, string fallback = "")
    {
        if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return fallback;
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : value.ToString();
    }
    private static int Integer(JsonElement source, string name, int fallback = 0) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : fallback;
    private static bool Boolean(JsonElement source, string name) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static DateTimeOffset? Date(JsonElement source, string name) => DateTimeOffset.TryParse(Text(source, name), out var value) ? value : null;
    private Guid PlayerId(string sourceId) => _players.TryGetValue(sourceId, out var id) ? id : StableGuid($"user:{sourceId}");
    private Guid? PlayerIdOrNull(string sourceId) => string.IsNullOrWhiteSpace(sourceId) ? null : PlayerId(sourceId);
    private static T EnumValue<T>(string value, T fallback) where T : struct, Enum => Enum.TryParse<T>(value, true, out var result) ? result : fallback;
    private static Marker MarkerValue(string value) => value.ToUpperInvariant() switch { "O" or "P2" => Marker.O, "A" or "P3" => Marker.A, _ => Marker.X };
    private static GameMode ModeValue(string value) => value.ToLowerInvariant() switch { "singleplayer" => GameMode.SinglePlayer, "local" => GameMode.Local, _ => GameMode.Multiplayer };
    private static RoomStatus RoomStatusValue(string value) => value.ToLowerInvariant() switch { "full" => RoomStatus.Full, "in-battle" => RoomStatus.InBattle, "completed" => RoomStatus.Completed, _ => RoomStatus.Available };
    private static AiDifficulty? Difficulty(string value) => string.IsNullOrWhiteSpace(value) ? null : EnumValue(value, AiDifficulty.Medium);
    private static WinReason? WinReasonValue(string value) => value.ToLowerInvariant() switch { "five_in_row" => WinReason.FiveInRow, "timeout" => WinReason.Timeout, "resignation" => WinReason.Resignation, "draw_agreement" => WinReason.DrawAgreement, _ => null };
    private static Guid StableGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value)).Take(16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }
}
