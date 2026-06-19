using TicTacToang.Application.Abstractions;
using TicTacToang.Application.Contracts;
using TicTacToang.Domain.Common;
using TicTacToang.Domain.Matches;
using TicTacToang.Domain.Rooms;

namespace TicTacToang.Application.Services;

public sealed class RoomService(IApplicationStore store, PlayerService players)
{
    public IReadOnlyList<GameRoom> List() => store.Rooms.OrderByDescending(room => room.RoomCode).ToList();
    public GameRoom Get(Guid roomId) => store.Rooms.FirstOrDefault(room => room.Id == roomId)
        ?? throw new DomainException("Room not found.");

    public async Task<GameRoom> CreateAsync(Guid hostId, CreateRoomRequest request)
    {
        var host = players.Find(hostId);
        host.RequireActive();
        if (request.Capacity is not (2 or 3)) throw new DomainException("Room capacity must be 2 or 3.");
        var room = new GameRoom
        {
            Name = request.Name.Trim(),
            HostId = hostId,
            Capacity = request.Capacity,
            Settings = new RoomSettings
            {
                BoardSize = request.BoardSize,
                BoardStyle = request.BoardStyle,
                TimeToThinkSeconds = request.TimeToThinkSeconds,
                Marker = request.Marker,
                HostPosition = request.HostPosition
            }
        };
        room.AddMember(new RoomMember(host.Id, host.Name, host.Avatar, false, null, request.Marker, "#f6c94d"));
        store.Rooms.Add(room);
        await store.SaveAsync();
        return room;
    }

    public async Task<GameRoom> JoinAsync(Guid roomId, Guid playerId)
    {
        var player = players.Find(playerId);
        player.RequireActive();
        var room = Get(roomId);
        var seatStyle = NextSeatStyle(room);
        room.AddMember(new RoomMember(player.Id, player.Name, player.Avatar, false, null, seatStyle.Marker, seatStyle.Color));
        await store.SaveAsync();
        return room;
    }

    public async Task<GameRoom> AddAiAsync(Guid roomId, Guid hostId, AiDifficulty difficulty)
    {
        var room = Get(roomId);
        if (room.HostId != hostId) throw new DomainException("Only the host can configure AI players.");
        var seatStyle = NextSeatStyle(room);
        room.AddMember(new RoomMember(null, $"AI {room.Members.Count} ({difficulty})", "", true, difficulty, seatStyle.Marker, seatStyle.Color));
        await store.SaveAsync();
        return room;
    }

    public async Task PostChatAsync(Guid roomId, Guid senderId, string text)
    {
        var sender = players.Find(senderId);
        var room = Get(roomId);
        room.PostMessage(senderId, sender.Name, text);
        await store.SaveAsync();
    }

    public async Task DeleteAsync(Guid roomId, Guid hostId)
    {
        var room = Get(roomId);
        if (room.HostId != hostId)
        {
            throw new DomainException("Only the room owner can delete this room.");
        }

        store.Rooms.Remove(room);
        await store.SaveAsync();
    }

    public async Task<Match> StartAsync(Guid roomId, Guid hostId)
    {
        var room = Get(roomId);
        room.Start(hostId);
        var first = room.Members[0];
        var second = room.Members[1];
        var match = Match.Create(
            second.IsAi ? GameMode.SinglePlayer : GameMode.Multiplayer,
            room.Settings.BoardSize,
            room.Settings.TimeToThinkSeconds,
            new MatchPlayer(first.PlayerId, first.Name, Marker.X, first.Avatar),
            new MatchPlayer(second.PlayerId, second.Name, Marker.O, second.Avatar, second.IsAi, second.Difficulty));
        store.Matches.Add(match);
        await store.SaveAsync();
        return match;
    }

    private static (string Marker, string Color) NextSeatStyle(GameRoom room) =>
        room.Members.Count switch
        {
            1 => ("O", "#f27d68"),
            2 => ("A", "#67e8b7"),
            _ => ("B", "#9c6cff")
        };
}
