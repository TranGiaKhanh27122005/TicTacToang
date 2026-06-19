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

    public IReadOnlyList<SuspiciousMatchReport> GetSuspiciousMatches()
    {
        var pairCounts = store.Matches
            .Where(match => match.PlayerX.PlayerId.HasValue && match.PlayerO.PlayerId.HasValue)
            .GroupBy(match => PairKey(match.PlayerX.PlayerId!.Value, match.PlayerO.PlayerId!.Value))
            .ToDictionary(group => group.Key, group => group.Count());

        return store.Matches
            .Select(match => BuildSuspiciousReport(match, pairCounts))
            .Where(report => report.Reasons.Count > 0)
            .OrderByDescending(report => report.Severity)
            .ThenBy(report => report.MoveCount)
            .Take(12)
            .ToList();
    }

    public AiAnalyticsSummary GetAiAnalytics()
    {
        var aiMatches = store.Matches
            .Where(match => match.Players.Any(player => player.IsAi))
            .Where(match => match.Status is MatchStatus.Completed or MatchStatus.Abandoned)
            .ToList();

        var playerWins = aiMatches.Count(PlayerBeatAi);
        var aiWins = aiMatches.Count(AiBeatPlayer);
        var draws = aiMatches.Count(match => match.Result.IsDraw);

        var byDifficulty = aiMatches
            .GroupBy(AiDifficultyLabel)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var matches = group.ToList();
                var difficultyPlayerWins = matches.Count(PlayerBeatAi);
                var difficultyAiWins = matches.Count(AiBeatPlayer);
                var difficultyDraws = matches.Count(match => match.Result.IsDraw);
                return new AiDifficultyAnalytics(
                    Difficulty: group.Key,
                    Matches: matches.Count,
                    PlayerWins: difficultyPlayerWins,
                    AiWins: difficultyAiWins,
                    Draws: difficultyDraws,
                    PlayerWinRate: Percent(difficultyPlayerWins, matches.Count),
                    AverageMoves: matches.Count == 0 ? 0 : Math.Round(matches.Average(match => match.Moves.Count), 1));
            })
            .ToList();

        return new AiAnalyticsSummary(
            TotalAiMatches: aiMatches.Count,
            PlayerWins: playerWins,
            AiWins: aiWins,
            Draws: draws,
            PlayerWinRate: Percent(playerWins, aiMatches.Count),
            ByDifficulty: byDifficulty);
    }

    private static SuspiciousMatchReport BuildSuspiciousReport(Match match, IReadOnlyDictionary<string, int> pairCounts)
    {
        var reasons = new List<string>();
        var severity = 0;
        var averageSecondsPerMove = match.Moves.Count == 0
            ? 0
            : Math.Round(match.Moves.Average(move => move.TimeTakenSeconds), 1);

        if (match.Status == MatchStatus.Abandoned && match.Moves.Count <= 5)
        {
            reasons.Add("Abandoned very early");
            severity += 3;
        }

        if (match.Status == MatchStatus.Completed && match.Moves.Count <= 9)
        {
            reasons.Add("Unusually short completed match");
            severity += 2;
        }

        if (match.Moves.Count >= 5 && averageSecondsPerMove <= 3)
        {
            reasons.Add("Very fast move timing");
            severity += 2;
        }

        if (match.PlayerX.PlayerId.HasValue && match.PlayerO.PlayerId.HasValue &&
            pairCounts.TryGetValue(PairKey(match.PlayerX.PlayerId.Value, match.PlayerO.PlayerId.Value), out var pairCount) &&
            pairCount >= 3)
        {
            reasons.Add("Repeated matches between same players");
            severity += 1;
        }

        if (match.Result.Reason == WinReason.Resignation && match.Moves.Count <= 8)
        {
            reasons.Add("Early resignation");
            severity += 2;
        }

        return new SuspiciousMatchReport(
            MatchId: match.Id,
            Mode: match.Mode.ToString(),
            Status: match.Status.ToString(),
            PlayerX: match.PlayerX.Name,
            PlayerO: string.Join(", ", match.Players.Skip(1).Select(player => player.Name)),
            MoveCount: match.Moves.Count,
            AverageSecondsPerMove: averageSecondsPerMove,
            Severity: severity,
            Reasons: reasons);
    }

    private static string PairKey(Guid first, Guid second)
    {
        var pair = new[] { first, second }.OrderBy(id => id).ToArray();
        return $"{pair[0]:N}:{pair[1]:N}";
    }

    private static string AiDifficultyLabel(Match match)
    {
        var aiPlayer = match.Players.FirstOrDefault(player => player.IsAi);
        return (aiPlayer?.AiDifficulty ?? AiDifficulty.Medium).ToString();
    }

    private static bool PlayerBeatAi(Match match)
    {
        if (!match.Result.Winner.HasValue)
        {
            return false;
        }

        var winner = match.Players.First(player => player.Marker == match.Result.Winner.Value);
        return !winner.IsAi;
    }

    private static bool AiBeatPlayer(Match match)
    {
        if (!match.Result.Winner.HasValue)
        {
            return false;
        }

        var winner = match.Players.First(player => player.Marker == match.Result.Winner.Value);
        return winner.IsAi;
    }

    private static double Percent(int count, int total) => total == 0 ? 0 : Math.Round(count * 100d / total, 1);
}
