using System.Text.Json;
using System.Text.Json.Serialization;
using TicTacToang.Application.Abstractions;
using TicTacToang.Domain.Matches;
using TicTacToang.Domain.Players;
using TicTacToang.Domain.Rooms;
using TicTacToang.Domain.Social;
using TicTacToang.Infrastructure.Security;

namespace TicTacToang.Infrastructure.Persistence;

public sealed class JsonApplicationStore : IApplicationStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public List<Player> Players { get; private set; } = [];
    public List<Match> Matches { get; private set; } = [];
    public List<GameRoom> Rooms { get; private set; } = [];
    public List<FriendRequest> FriendRequests { get; private set; } = [];
    public List<Friendship> Friendships { get; private set; } = [];
    public List<RoomInvite> RoomInvites { get; private set; } = [];

    public JsonApplicationStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppContext.BaseDirectory, "Data", "tictactoang.json");
        LoadOrSeed();
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var state = new StoreState(Players, Matches, Rooms, FriendRequests, Friendships, RoomInvites);
            await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(state, _options), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void LoadOrSeed()
    {
        if (File.Exists(_path))
        {
            var loaded = JsonSerializer.Deserialize<StoreState>(File.ReadAllText(_path), _options);
            if (loaded is not null)
            {
                Players = loaded.Players;
                Matches = loaded.Matches;
                Rooms = loaded.Rooms;
                FriendRequests = loaded.FriendRequests;
                Friendships = loaded.Friendships;
                RoomInvites = loaded.RoomInvites;
                return;
            }
        }

        var passwords = new Pbkdf2PasswordService();
        var admin = new Player
        {
            Name = "Admin", Username = "admin", Email = "admin@tictactoang.com",
            Country = "Vietnam", PasswordHash = passwords.Hash("Admin@1234"), Role = PlayerRole.Admin
        };
        var playerA = new Player
        {
            Name = "Player A", Username = "playera", Email = "playera@tictactoang.com",
            Country = "Vietnam", PasswordHash = passwords.Hash("PlayerA@123")
        };
        playerA.ActivatePremium(DateTimeOffset.UtcNow.AddMonths(1));
        var playerB = new Player
        {
            Name = "Player B", Username = "playerb", Email = "playerb@tictactoang.com",
            Country = "Vietnam", PasswordHash = passwords.Hash("PlayerB@123")
        };
        Players.AddRange([admin, playerA, playerB]);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var state = new StoreState(Players, Matches, Rooms, FriendRequests, Friendships, RoomInvites);
        File.WriteAllText(_path, JsonSerializer.Serialize(state, _options));
    }

    private sealed record StoreState(
        List<Player> Players,
        List<Match> Matches,
        List<GameRoom> Rooms,
        List<FriendRequest> FriendRequests,
        List<Friendship> Friendships,
        List<RoomInvite> RoomInvites);
}
