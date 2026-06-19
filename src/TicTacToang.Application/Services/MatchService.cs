using TicTacToang.Application.Abstractions;
using TicTacToang.Application.Contracts;
using TicTacToang.Domain.Common;
using TicTacToang.Domain.Matches;

namespace TicTacToang.Application.Services;

public sealed class MatchService(IApplicationStore store, PlayerService players, GomokuAi ai)
{
    public IReadOnlyList<Match> List() => store.Matches.OrderByDescending(match => match.StartedAt).ToList();
    public IReadOnlyList<Match> History(Guid playerId) => store.Matches
        .Where(match => match.Players.Any(player => player.PlayerId == playerId)
            && match.Status is MatchStatus.Completed or MatchStatus.Abandoned)
        .OrderByDescending(match => match.StartedAt).ToList();
    public Match Get(string id) => store.Matches.FirstOrDefault(match => match.Id == id)
        ?? throw new DomainException("Match not found.");

    public async Task<Match> CreateAsync(CreateMatchRequest request)
    {
        var player = players.Find(request.PlayerId);
        player.RequireActive();
        var opponent = request.Mode == GameMode.Multiplayer
            ? new MatchPlayer(null, "Waiting for player", Marker.O)
            : request.Mode == GameMode.SinglePlayer
                ? new MatchPlayer(null, $"AI ({request.Difficulty})", Marker.O, IsAi: true, AiDifficulty: request.Difficulty)
                : new MatchPlayer(null, "Local Player 2", Marker.O);
        var match = Match.Create(request.Mode, request.BoardSize, request.TimeControlSeconds,
            new MatchPlayer(player.Id, player.Name, Marker.X, player.Avatar), opponent);
        store.Matches.Add(match);
        await store.SaveAsync();
        return match;
    }

    public async Task<Match> JoinAsync(string id, Guid playerId)
    {
        var player = players.Find(playerId);
        player.RequireActive();
        var match = Get(id);
        match.Join(new MatchPlayer(player.Id, player.Name, Marker.O, player.Avatar));
        await store.SaveAsync();
        return match;
    }

    public async Task<Match> PlayAsync(string id, PlayMoveRequest request)
    {
        var match = Get(id);
        match.Play(request.PlayerId, request.Row, request.Column, request.TimeTakenSeconds);
        if (match.Status == MatchStatus.Active)
        {
            var current = match.Players.First(player => player.Marker == match.CurrentTurn);
            while (match.Status == MatchStatus.Active && current.IsAi)
            {
                var tile = ai.ChooseMove(match, current.AiDifficulty ?? AiDifficulty.Medium);
                match.Play(null, tile.Row, tile.Column);
                current = match.Players.First(player => player.Marker == match.CurrentTurn);
            }
        }
        await store.SaveAsync();
        return match;
    }

    public async Task<Match> ResignAsync(string id, Guid playerId)
    {
        var match = Get(id);
        match.Resign(playerId);
        await store.SaveAsync();
        return match;
    }

    public async Task<Match> AbortAsync(string id)
    {
        var match = Get(id);
        match.Abort();
        await store.SaveAsync();
        return match;
    }
}
