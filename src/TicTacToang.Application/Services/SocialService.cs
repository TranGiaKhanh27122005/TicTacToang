using TicTacToang.Application.Abstractions;
using TicTacToang.Application.Contracts;
using TicTacToang.Domain.Common;
using TicTacToang.Domain.Social;

namespace TicTacToang.Application.Services;

public sealed class SocialService(IApplicationStore store, PlayerService players)
{
    public IReadOnlyList<Friendship> Friends(Guid playerId) =>
        store.Friendships.Where(link => link.UserA == playerId || link.UserB == playerId).ToList();
    public IReadOnlyList<FriendRequest> Requests(Guid playerId) =>
        store.FriendRequests.Where(request => request.RequesterId == playerId || request.RecipientId == playerId).ToList();
    public IReadOnlyList<RoomInvite> Invites(Guid playerId) =>
        store.RoomInvites.Where(invite => invite.RecipientId == playerId).OrderByDescending(invite => invite.CreatedAt).ToList();

    public IReadOnlyList<FriendSummary> FriendSummaries(Guid playerId)
    {
        var result = new List<FriendSummary>();
        foreach (var link in Friends(playerId))
        {
            var friendId = link.UserA == playerId ? link.UserB : link.UserA;
            var friend = store.Players.FirstOrDefault(player => player.Id == friendId);
            if (friend is null) continue;
            result.Add(new FriendSummary(friend.Id, friend.Name, friend.Username, friend.Avatar,
                friend.LastSeenAt >= DateTimeOffset.UtcNow.AddMinutes(-10)));
        }
        return result.OrderByDescending(friend => friend.IsOnline).ThenBy(friend => friend.Name).ToList();
    }

    public IReadOnlyList<SocialAlertSummary> AlertSummaries(Guid playerId)
    {
        var result = new List<SocialAlertSummary>();
        foreach (var invite in store.RoomInvites.Where(invite => invite.RecipientId == playerId && invite.Status == RequestStatus.Pending))
        {
            var sender = store.Players.FirstOrDefault(player => player.Id == invite.SenderId);
            if (sender is null) continue;
            result.Add(new SocialAlertSummary(invite.Id, sender.Name,
                store.Rooms.FirstOrDefault(room => room.Id == invite.RoomId)?.Name ?? "Game Room",
                invite.RoomId, invite.CreatedAt));
        }
        return result.OrderByDescending(alert => alert.CreatedAt).ToList();
    }

    public IReadOnlyList<FriendRequestSummary> PendingRequestSummaries(Guid playerId)
    {
        var result = new List<FriendRequestSummary>();
        foreach (var request in store.FriendRequests.Where(request => request.RecipientId == playerId && request.Status == RequestStatus.Pending))
        {
            var sender = store.Players.FirstOrDefault(player => player.Id == request.RequesterId);
            if (sender is null) continue;
            result.Add(new FriendRequestSummary(request.Id, sender.Name, sender.Username, sender.Avatar, request.CreatedAt));
        }
        return result.OrderByDescending(request => request.CreatedAt).ToList();
    }

    public async Task<FriendRequest> SendRequestAsync(Guid requesterId, Guid recipientId)
    {
        players.Find(requesterId).RequireActive();
        players.Find(recipientId);
        if (requesterId == recipientId) throw new DomainException("You cannot add yourself as a friend.");
        if (store.FriendRequests.Any(request => request.RequesterId == requesterId &&
            request.RecipientId == recipientId && request.Status == RequestStatus.Pending))
            throw new DomainException("A friend request is already pending.");
        var request = new FriendRequest { RequesterId = requesterId, RecipientId = recipientId };
        store.FriendRequests.Add(request);
        await store.SaveAsync();
        return request;
    }

    public async Task AcceptRequestAsync(Guid requestId, Guid recipientId)
    {
        var request = store.FriendRequests.FirstOrDefault(item => item.Id == requestId && item.RecipientId == recipientId)
            ?? throw new DomainException("Friend request not found.");
        request.Status = RequestStatus.Accepted;
        var pair = new[] { request.RequesterId, request.RecipientId }.Order().ToArray();
        if (!store.Friendships.Any(item => item.UserA == pair[0] && item.UserB == pair[1]))
            store.Friendships.Add(new Friendship(pair[0], pair[1], DateTimeOffset.UtcNow));
        await store.SaveAsync();
    }

    public async Task<RoomInvite> InviteToRoomAsync(Guid senderId, Guid recipientId, Guid roomId)
    {
        players.Find(senderId).RequireActive();
        var invite = new RoomInvite { SenderId = senderId, RecipientId = recipientId, RoomId = roomId };
        store.RoomInvites.Add(invite);
        await store.SaveAsync();
        return invite;
    }
}
