using TicTacToang.Domain.Common;
using TicTacToang.Domain.Matches;

var specs = new (string Name, Action Execute)[]
{
    ("five horizontal markers completes the match", HorizontalWin),
    ("five diagonal markers records winning tiles", DiagonalWin),
    ("occupied cells are rejected", OccupiedCell),
    ("opponent cannot move out of turn", WrongTurn),
    ("resignation awards win to opponent", Resignation),
    ("waiting multiplayer match activates after join", MultiplayerJoin)
};

var failed = 0;
foreach (var spec in specs)
{
    try
    {
        spec.Execute();
        Console.WriteLine($"PASS {spec.Name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.WriteLine($"FAIL {spec.Name}: {exception.Message}");
    }
}

return failed == 0 ? 0 : 1;

static Match ActiveMatch(GameMode mode = GameMode.Local) =>
    Match.Create(mode, 10, 60, new MatchPlayer(Guid.Parse("00000000-0000-0000-0000-000000000001"), "A", Marker.X),
        new MatchPlayer(mode == GameMode.Multiplayer ? null : Guid.Parse("00000000-0000-0000-0000-000000000002"), "B", Marker.O));

static void HorizontalWin()
{
    var match = ActiveMatch();
    for (var column = 0; column < 5; column++)
    {
        match.Play(null, 1, column);
        if (column < 4) match.Play(null, 2, column);
    }
    Assert(match.Status == MatchStatus.Completed && match.Result.Winner == Marker.X, "Expected X to win.");
}

static void DiagonalWin()
{
    var match = ActiveMatch();
    for (var cell = 0; cell < 5; cell++)
    {
        match.Play(null, cell, cell);
        if (cell < 4) match.Play(null, cell, cell + 1);
    }
    Assert(match.Result.WinningTiles.Count >= 5, "Expected diagonal winning positions.");
}

static void OccupiedCell()
{
    var match = ActiveMatch();
    match.Play(null, 0, 0);
    Throws(() => match.Play(null, 0, 0));
}

static void WrongTurn()
{
    var a = Guid.Parse("00000000-0000-0000-0000-000000000001");
    var b = Guid.Parse("00000000-0000-0000-0000-000000000002");
    var match = Match.Create(GameMode.SinglePlayer, 10, 60, new MatchPlayer(a, "A", Marker.X), new MatchPlayer(b, "B", Marker.O));
    Throws(() => match.Play(b, 0, 0));
}

static void Resignation()
{
    var a = Guid.Parse("00000000-0000-0000-0000-000000000001");
    var match = ActiveMatch(GameMode.SinglePlayer);
    match.Resign(a);
    Assert(match.Result.Winner == Marker.O && match.Result.Reason == WinReason.Resignation, "Expected O resignation win.");
}

static void MultiplayerJoin()
{
    var match = ActiveMatch(GameMode.Multiplayer);
    Assert(match.Status == MatchStatus.Waiting, "Expected waiting status.");
    match.Join(new MatchPlayer(Guid.NewGuid(), "Opponent", Marker.O));
    Assert(match.Status == MatchStatus.Active, "Expected active status.");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void Throws(Action action)
{
    try { action(); }
    catch (DomainException) { return; }
    throw new Exception("Expected domain rule to reject the action.");
}
