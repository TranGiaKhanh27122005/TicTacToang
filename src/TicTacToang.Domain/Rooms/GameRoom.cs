using System.Text.Json.Serialization;
using TicTacToang.Domain.Common;
using TicTacToang.Domain.Matches;

namespace TicTacToang.Domain.Rooms;

public enum RoomStatus { Available, Full, InBattle, Completed }

public sealed record RoomMember(Guid? PlayerId, string Name, string Avatar, bool IsAi, AiDifficulty? Difficulty, string Marker, string MarkerColor);
public sealed record ChatMessage(Guid Id, Guid? SenderId, string SenderName, string Text, DateTimeOffset CreatedAt);

public sealed class RoomSettings
{
    public string BoardStyle { get; set; } = "Classic";
    public int BoardSize { get; set; } = 10;
    public int TimeToThinkSeconds { get; set; } = 60;
    public string Marker { get; set; } = "X";
    public int HostPosition { get; set; } = 1;
}

public sealed class GameRoom
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int RoomCode { get; init; } = Random.Shared.Next(100000, 999999);
    public required string Name { get; set; }
    public Guid HostId { get; init; }
    public int Capacity { get; init; }
    [JsonInclude] public RoomStatus Status { get; private set; } = RoomStatus.Available;
    public RoomSettings Settings { get; set; } = new();
    public List<RoomMember> Members { get; init; } = [];
    public List<ChatMessage> ChatMessages { get; init; } = [];

    public void AddMember(RoomMember member)
    {
        if (Status != RoomStatus.Available || Members.Count >= Capacity)
        {
            throw new DomainException("Room is full or unavailable.");
        }
        if (!member.IsAi && Members.Any(existing => existing.PlayerId == member.PlayerId))
        {
            return;
        }

        Members.Add(member);
        Status = Members.Count >= Capacity ? RoomStatus.Full : RoomStatus.Available;
    }

    public void PostMessage(Guid? senderId, string senderName, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        ChatMessages.Add(new ChatMessage(Guid.NewGuid(), senderId, senderName, text.Trim(), DateTimeOffset.UtcNow));
    }

    public void Start(Guid requestedBy)
    {
        if (requestedBy != HostId)
        {
            throw new DomainException("Only the host can start this room.");
        }
        if (Members.Count < Capacity)
        {
            throw new DomainException("Fill every player slot before starting the game.");
        }
        Status = RoomStatus.InBattle;
    }
}
