using TicTacToang.Domain.Matches;

namespace TicTacToang.Application.Contracts;

public sealed record RegisterRequest(string Name, string Username, string Email, string Password, string Country);
public sealed record LoginRequest(string UsernameOrEmail, string Password);
public sealed record UpdateProfileRequest(string Name, string Username, string Email, string Country, string Avatar);
public sealed record CreateRoomRequest(
    string Name,
    int Capacity,
    int BoardSize,
    string BoardStyle,
    int TimeToThinkSeconds,
    string Marker = "X",
    int HostPosition = 1);
public sealed record CreateMatchRequest(GameMode Mode, int BoardSize, int TimeControlSeconds, Guid PlayerId, AiDifficulty Difficulty = AiDifficulty.Medium);
public sealed record PlayMoveRequest(Guid? PlayerId, int Row, int Column, int TimeTakenSeconds = 0);
public sealed record AuthResult(PlayerView Player, string Token);

public sealed record AdminUserQuery(string? Search, string? Role, string? Status, string? Premium);

public sealed record AdminDashboardStats(
    int TotalUsers,
    int ActiveUsers,
    int InactiveUsers,
    int PremiumUsers,
    int TotalMatches,
    int ActiveMatches,
    int CompletedMatches,
    int OpenRooms,
    int RoomsCreated,
    int MovesPlayed,
    double AverageMovesPerMatch,
    int TenByTenMatches,
    int FifteenByFifteenMatches);

public sealed record SuspiciousMatchReport(
    string MatchId,
    string Mode,
    string Status,
    string PlayerX,
    string PlayerO,
    int MoveCount,
    double AverageSecondsPerMove,
    int Severity,
    IReadOnlyList<string> Reasons);

public sealed record AiDifficultyAnalytics(
    string Difficulty,
    int Matches,
    int PlayerWins,
    int AiWins,
    int Draws,
    double PlayerWinRate,
    double AverageMoves);

public sealed record AiAnalyticsSummary(
    int TotalAiMatches,
    int PlayerWins,
    int AiWins,
    int Draws,
    double PlayerWinRate,
    IReadOnlyList<AiDifficultyAnalytics> ByDifficulty);

public sealed record PlayerView(
    Guid Id,
    string Name,
    string Username,
    string Email,
    string Country,
    string Role,
    bool IsActive,
    bool IsPremium,
    DateTimeOffset? SubscriptionEndDate,
    string Avatar);
