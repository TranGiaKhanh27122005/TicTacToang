using System.Text.Json.Serialization;
using TicTacToang.Domain.Common;

namespace TicTacToang.Domain.Matches;

public enum GameMode { SinglePlayer, Multiplayer, Local }
public enum MatchStatus { Waiting, Active, Completed, Abandoned }
public enum Marker { X, O }
public enum WinReason { FiveInRow, Timeout, Resignation, DrawAgreement, Aborted }
public enum AiDifficulty { Easy, Medium, Hard }

public sealed record Coordinate(int Row, int Column);

public sealed record MatchPlayer(
    Guid? PlayerId,
    string Name,
    Marker Marker,
    string Avatar = "",
    bool IsAi = false,
    AiDifficulty? AiDifficulty = null,
    int Rank = 1200);

public sealed record Move(int Number, Marker Marker, int Row, int Column, DateTimeOffset PlayedAt, int TimeTakenSeconds);

public sealed class MatchResult
{
    public Marker? Winner { get; set; }
    public bool IsDraw { get; set; }
    public WinReason? Reason { get; set; }
    public List<Coordinate> WinningTiles { get; set; } = [];
}

public sealed class Match
{
    public string Id { get; init; } = $"game-{Guid.NewGuid():N}";
    public int BoardSize { get; init; }
    public GameMode Mode { get; init; }
    public int TimeControlSeconds { get; init; }
    public MatchPlayer PlayerX { get; init; } = null!;
    [JsonInclude] public MatchPlayer PlayerO { get; private set; } = null!;
    [JsonInclude] public Marker CurrentTurn { get; private set; } = Marker.X;
    [JsonInclude] public MatchStatus Status { get; private set; }
    public List<Move> Moves { get; init; } = [];
    [JsonInclude] public MatchResult Result { get; private set; } = new();
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    [JsonInclude] public DateTimeOffset? CompletedAt { get; private set; }

    public static Match Create(GameMode mode, int boardSize, int timeControlSeconds, MatchPlayer playerX, MatchPlayer playerO)
    {
        if (boardSize is not (10 or 15))
        {
            throw new DomainException("Board size must be 10 or 15.");
        }

        if (timeControlSeconds is < 30 or > 720)
        {
            throw new DomainException("Time control must be between 30 and 720 seconds.");
        }

        return new Match
        {
            BoardSize = boardSize,
            Mode = mode,
            TimeControlSeconds = timeControlSeconds,
            PlayerX = playerX with { Marker = Marker.X },
            PlayerO = playerO with { Marker = Marker.O },
            Status = mode == GameMode.Multiplayer && playerO.PlayerId is null ? MatchStatus.Waiting : MatchStatus.Active
        };
    }

    public void Join(MatchPlayer opponent)
    {
        if (Mode != GameMode.Multiplayer || Status != MatchStatus.Waiting)
        {
            throw new DomainException("This match is not waiting for an opponent.");
        }

        PlayerO = opponent with { Marker = Marker.O };
        Status = MatchStatus.Active;
    }

    public Move Play(Guid? playerId, int row, int column, int timeTakenSeconds = 0)
    {
        RequireActive();
        var player = CurrentTurn == Marker.X ? PlayerX : PlayerO;
        if (Mode != GameMode.Local && !player.IsAi && player.PlayerId != playerId)
        {
            throw new DomainException("It is not your turn.");
        }

        if (row < 0 || row >= BoardSize || column < 0 || column >= BoardSize)
        {
            throw new DomainException("Move is outside the board.");
        }

        if (Moves.Any(move => move.Row == row && move.Column == column))
        {
            throw new DomainException("Tile is already occupied.");
        }

        var move = new Move(Moves.Count + 1, CurrentTurn, row, column, DateTimeOffset.UtcNow, Math.Max(0, timeTakenSeconds));
        Moves.Add(move);

        var winningTiles = FindWinningLine(row, column, CurrentTurn);
        if (winningTiles.Count >= 5)
        {
            Complete(new MatchResult { Winner = CurrentTurn, Reason = WinReason.FiveInRow, WinningTiles = winningTiles });
        }
        else if (Moves.Count == BoardSize * BoardSize)
        {
            Complete(new MatchResult { IsDraw = true, Reason = WinReason.DrawAgreement });
        }
        else
        {
            CurrentTurn = CurrentTurn == Marker.X ? Marker.O : Marker.X;
        }

        return move;
    }

    public void Resign(Guid playerId)
    {
        RequireActive();
        var winner = PlayerX.PlayerId == playerId ? Marker.O
            : PlayerO.PlayerId == playerId ? Marker.X
            : throw new DomainException("Player is not part of this match.");
        Complete(new MatchResult { Winner = winner, Reason = WinReason.Resignation });
    }

    public void Abort(WinReason reason = WinReason.Aborted)
    {
        if (Status is MatchStatus.Completed or MatchStatus.Abandoned)
        {
            return;
        }

        Status = MatchStatus.Abandoned;
        Result = new MatchResult { Reason = reason };
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public string?[,] BuildBoard(int? throughMove = null)
    {
        var board = new string?[BoardSize, BoardSize];
        foreach (var move in Moves.Take(throughMove ?? Moves.Count))
        {
            board[move.Row, move.Column] = move.Marker.ToString();
        }
        return board;
    }

    private List<Coordinate> FindWinningLine(int row, int column, Marker marker)
    {
        var occupied = Moves.Where(move => move.Marker == marker)
            .Select(move => (move.Row, move.Column)).ToHashSet();
        foreach (var (rowStep, columnStep) in new[] { (1, 0), (0, 1), (1, 1), (1, -1) })
        {
            var line = new List<Coordinate> { new(row, column) };
            AddDirection(line, occupied, row, column, rowStep, columnStep);
            AddDirection(line, occupied, row, column, -rowStep, -columnStep);
            if (line.Count >= 5)
            {
                return line.OrderBy(tile => tile.Row).ThenBy(tile => tile.Column).ToList();
            }
        }

        return [];
    }

    private static void AddDirection(List<Coordinate> line, HashSet<(int Row, int Column)> occupied, int row, int column, int dr, int dc)
    {
        for (var distance = 1; distance < 5; distance++)
        {
            var candidate = (row + dr * distance, column + dc * distance);
            if (!occupied.Contains(candidate))
            {
                break;
            }
            line.Add(new Coordinate(candidate.Item1, candidate.Item2));
        }
    }

    private void Complete(MatchResult result)
    {
        Result = result;
        Status = MatchStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    private void RequireActive()
    {
        if (Status != MatchStatus.Active)
        {
            throw new DomainException("Match is not active.");
        }
    }
}
