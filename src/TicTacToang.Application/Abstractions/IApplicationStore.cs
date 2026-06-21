using TicTacToang.Domain.Matches;
using TicTacToang.Domain.Players;
using TicTacToang.Domain.Rooms;
using TicTacToang.Domain.Social;

namespace TicTacToang.Application.Abstractions;

public interface IApplicationStore
{
    List<Player> Players { get; }
    List<Match> Matches { get; }
    List<GameRoom> Rooms { get; }
    List<FriendRequest> FriendRequests { get; }
    List<Friendship> Friendships { get; }
    List<RoomInvite> RoomInvites { get; }
    Task SaveAsync(CancellationToken cancellationToken = default);
}

public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string password, string hash);
    bool NeedsRehash(string hash);
}
