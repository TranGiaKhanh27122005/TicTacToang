using TicTacToang.Domain.Matches;

namespace TicTacToang.Application.Services;

public sealed class GomokuAi
{
    public Coordinate ChooseMove(Match match, AiDifficulty difficulty)
    {
        var board = match.BuildBoard();
        var empty = EmptyTiles(board, match.BoardSize).ToList();
        if (empty.Count == 0) throw new InvalidOperationException("There are no available moves.");

        if (difficulty == AiDifficulty.Easy)
        {
            var previous = match.Moves.LastOrDefault(move => move.Marker != match.CurrentTurn);
            var adjacent = previous is null ? [] : empty.Where(tile =>
                Math.Abs(tile.Row - previous.Row) <= 1 && Math.Abs(tile.Column - previous.Column) <= 1).ToList();
            return adjacent.Count > 0 ? adjacent[Random.Shared.Next(adjacent.Count)] : empty[Random.Shared.Next(empty.Count)];
        }

        var opponents = match.Players
            .Where(player => player.Marker != match.CurrentTurn)
            .Select(player => player.Marker)
            .ToList();
        return empty.FirstOrDefault(tile => WouldWin(board, tile, match.CurrentTurn, match.BoardSize))
            ?? empty.FirstOrDefault(tile => opponents.Any(opponent => WouldWin(board, tile, opponent, match.BoardSize)))
            ?? ScoreMoves(board, empty, match.CurrentTurn, opponents, match.BoardSize);
    }

    private static Coordinate ScoreMoves(string?[,] board, IEnumerable<Coordinate> choices, Marker marker, IReadOnlyList<Marker> opponents, int size) =>
        choices.OrderByDescending(tile => Score(board, tile, marker, size) + opponents.Max(opponent => Score(board, tile, opponent, size)) * 0.9)
            .ThenBy(tile => Math.Abs(tile.Row - size / 2) + Math.Abs(tile.Column - size / 2)).First();

    private static double Score(string?[,] board, Coordinate tile, Marker marker, int size)
    {
        board[tile.Row, tile.Column] = marker.ToString();
        double best = 0;
        foreach (var (dr, dc) in new[] { (1, 0), (0, 1), (1, 1), (1, -1) })
        {
            var count = 1 + Count(board, tile, marker, dr, dc, size) + Count(board, tile, marker, -dr, -dc, size);
            best = Math.Max(best, Math.Pow(10, Math.Min(count, 5)));
        }
        board[tile.Row, tile.Column] = null;
        return best;
    }

    private static bool WouldWin(string?[,] board, Coordinate tile, Marker marker, int size) => Score(board, tile, marker, size) >= 100000;

    private static int Count(string?[,] board, Coordinate tile, Marker marker, int dr, int dc, int size)
    {
        var count = 0;
        for (var step = 1; step < 5; step++)
        {
            var row = tile.Row + dr * step;
            var col = tile.Column + dc * step;
            if (row < 0 || row >= size || col < 0 || col >= size || board[row, col] != marker.ToString()) break;
            count++;
        }
        return count;
    }

    private static IEnumerable<Coordinate> EmptyTiles(string?[,] board, int size)
    {
        for (var row = 0; row < size; row++)
        for (var col = 0; col < size; col++)
            if (board[row, col] is null) yield return new Coordinate(row, col);
    }
}
