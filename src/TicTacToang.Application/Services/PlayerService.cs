using TicTacToang.Application.Abstractions;
using TicTacToang.Application.Contracts;
using TicTacToang.Domain.Common;
using TicTacToang.Domain.Players;

namespace TicTacToang.Application.Services;

public sealed class PlayerService(IApplicationStore store, IPasswordService passwords)
{
    private readonly Dictionary<string, Guid> _tokens = [];

    public IReadOnlyList<PlayerView> GetAll() => store.Players.Select(ToView).ToList();

    public IReadOnlyList<PlayerView> Search(AdminUserQuery query)
    {
        var users = store.Players.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            users = users.Where(player =>
                player.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                player.Username.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                player.Email.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Role) && !query.Role.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            users = users.Where(player => player.Role.ToString().Equals(query.Role, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Status) && !query.Status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            users = users.Where(player => player.Status.ToString().Equals(query.Status, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Premium) && !query.Premium.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var premium = query.Premium.Equals("premium", StringComparison.OrdinalIgnoreCase);
            users = users.Where(player => player.HasPremiumAccess(DateTimeOffset.UtcNow) == premium);
        }

        return users
            .OrderBy(player => player.Status)
            .ThenBy(player => player.Name)
            .Select(ToView)
            .ToList();
    }

    public PlayerView Get(Guid id) => ToView(Find(id));

    public async Task<PlayerView> RegisterAsync(RegisterRequest input)
    {
        ValidateIdentity(input.Name, input.Username, input.Email, input.Password);
        if (store.Players.Any(player => player.Username.Equals(input.Username, StringComparison.OrdinalIgnoreCase)
             || player.Email.Equals(input.Email, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException("Username or email is already registered.");
        }

        var player = new Player
        {
            Name = input.Name.Trim(),
            Username = input.Username.Trim(),
            Email = input.Email.Trim().ToLowerInvariant(),
            PasswordHash = passwords.Hash(input.Password),
            Country = input.Country.Trim()
        };
        store.Players.Add(player);
        await store.SaveAsync();
        return ToView(player);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest input)
    {
        var player = store.Players.FirstOrDefault(candidate =>
            candidate.Username.Equals(input.UsernameOrEmail, StringComparison.OrdinalIgnoreCase) ||
            candidate.Email.Equals(input.UsernameOrEmail, StringComparison.OrdinalIgnoreCase));
        if (player is null || !passwords.Verify(input.Password, player.PasswordHash))
        {
            throw new DomainException("Username/email or password is incorrect.");
        }
        player.RequireActive();
        player.LastLoginAt = DateTimeOffset.UtcNow;
        player.LastSeenAt = DateTimeOffset.UtcNow;
        var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        _tokens[token] = player.Id;
        await store.SaveAsync();
        return new AuthResult(ToView(player), token);
    }

    public PlayerView? ResolveToken(string? token) =>
        token is not null && _tokens.TryGetValue(token, out var id) ? Get(id) : null;

    public async Task<PlayerView> UpdateProfileAsync(Guid id, UpdateProfileRequest input)
    {
        var player = Find(id);
        if (store.Players.Any(candidate => candidate.Id != id &&
            (candidate.Username.Equals(input.Username, StringComparison.OrdinalIgnoreCase) ||
             candidate.Email.Equals(input.Email, StringComparison.OrdinalIgnoreCase))))
        {
            throw new DomainException("Username or email is already in use.");
        }

        player.Name = input.Name.Trim();
        player.Username = input.Username.Trim();
        player.Email = input.Email.Trim().ToLowerInvariant();
        player.Country = input.Country.Trim();
        player.Avatar = string.IsNullOrWhiteSpace(input.Avatar) ? "Mambo.png" : input.Avatar;
        await store.SaveAsync();
        return ToView(player);
    }

    public async Task ChangePasswordAsync(Guid id, string currentPassword, string newPassword)
    {
        var player = Find(id);
        if (!passwords.Verify(currentPassword, player.PasswordHash))
        {
            throw new DomainException("Current password is incorrect.");
        }
        if (newPassword.Length < 8)
        {
            throw new DomainException("New password must be at least 8 characters.");
        }
        player.PasswordHash = passwords.Hash(newPassword);
        await store.SaveAsync();
    }

    public async Task<PlayerView> ActivateSubscriptionAsync(Guid id)
    {
        var player = Find(id);
        player.ActivatePremium(DateTimeOffset.UtcNow.AddMonths(1));
        await store.SaveAsync();
        return ToView(player);
    }

    public async Task<PlayerView> SetAccountStatusAsync(Guid id, AccountStatus status, bool? premium = null)
    {
        var player = Find(id);
        player.Status = status;
        if (premium == true) player.ActivatePremium(DateTimeOffset.UtcNow.AddMonths(1));
        if (premium == false) player.CancelPremium();
        await store.SaveAsync();
        return ToView(player);
    }

    public Player Find(Guid id) => store.Players.FirstOrDefault(player => player.Id == id)
        ?? throw new DomainException("Player not found.");

    public static PlayerView ToView(Player player) => new(
        player.Id, player.Name, player.Username, player.Email, player.Country,
        player.Role.ToString().ToLowerInvariant(), player.Status == AccountStatus.Active,
        player.HasPremiumAccess(DateTimeOffset.UtcNow), player.SubscriptionEndDate, player.Avatar);

    private static void ValidateIdentity(string name, string username, string email, string password)
    {
        if (name.Trim().Length < 3 || username.Trim().Length < 3)
            throw new DomainException("Name and username must have at least 3 characters.");
        if (!email.Contains('@'))
            throw new DomainException("A valid email address is required.");
        if (password.Length < 8)
            throw new DomainException("Password must have at least 8 characters.");
    }
}
