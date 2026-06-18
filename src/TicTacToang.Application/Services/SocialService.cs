using TicTacToang.Application.Abstractions;
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
