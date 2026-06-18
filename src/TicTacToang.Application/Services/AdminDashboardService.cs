using TicTacToang.Application.Abstractions;
using TicTacToang.Application.Contracts;
using TicTacToang.Domain.Matches;
using TicTacToang.Domain.Players;
using TicTacToang.Domain.Rooms;

namespace TicTacToang.Application.Services;

public sealed class AdminDashboardService(IApplicationStore store)
{
    public AdminDashboardStats GetStats()
    {
        var totalMatches = store.Matches.Count;
        var movesPlayed = store.Matches.Sum(match => match.Moves.Count);

        return new AdminDashboardStats(
            TotalUsers: store.Players.Count,
            ActiveUsers: store.Players.Count(player => player.Status == AccountStatus.Active),
            InactiveUsers: store.Players.Count(player => player.Status == AccountStatus.Inactive),
            PremiumUsers: store.Players.Count(player => player.HasPremiumAccess(DateTimeOffset.UtcNow)),
            TotalMatches: totalMatches,
            ActiveMatches: store.Matches.Count(match => match.Status is MatchStatus.Active or MatchStatus.Waiting),
            CompletedMatches: store.Matches.Count(match => match.Status == MatchStatus.Completed),
            OpenRooms: store.Rooms.Count(room => room.Status == RoomStatus.Available),
            RoomsCreated: store.Rooms.Count,
            MovesPlayed: movesPlayed,
            AverageMovesPerMatch: totalMatches == 0 ? 0 : Math.Round((double)movesPlayed / totalMatches, 1),
            TenByTenMatches: store.Matches.Count(match => match.BoardSize == 10),
            FifteenByFifteenMatches: store.Matches.Count(match => match.BoardSize == 15));
    }
}
