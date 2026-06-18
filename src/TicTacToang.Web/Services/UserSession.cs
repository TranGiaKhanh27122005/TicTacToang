using TicTacToang.Application.Contracts;

namespace TicTacToang.Web.Services;

public sealed class UserSession
{
    public PlayerView? Player { get; private set; }
    public string? Token { get; private set; }
    public bool IsAuthenticated => Player is not null;
    public bool IsAdmin => Player?.Role == "admin";

    public void SignIn(AuthResult auth)
    {
        Player = auth.Player;
        Token = auth.Token;
    }

    public void Refresh(PlayerView player) => Player = player;

    public void SignOut()
    {
        Player = null;
        Token = null;
    }
}
