namespace TicTacToang.Domain.Social;

public enum RequestStatus { Pending, Accepted, Declined }

public sealed class FriendRequest
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid RequesterId { get; init; }
    public Guid RecipientId { get; init; }
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record Friendship(Guid UserA, Guid UserB, DateTimeOffset CreatedAt);

public sealed class RoomInvite
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SenderId { get; init; }
    public Guid RecipientId { get; init; }
    public Guid RoomId { get; init; }
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
