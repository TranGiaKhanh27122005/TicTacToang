using System.Text.Json.Serialization;
using TicTacToang.Domain.Common;

namespace TicTacToang.Domain.Players;

public enum PlayerRole { Player, Moderator, Admin }
public enum AccountStatus { Active, Inactive }

public sealed class Player
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Username { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public required string Country { get; set; }
    public PlayerRole Role { get; set; } = PlayerRole.Player;
    public AccountStatus Status { get; set; } = AccountStatus.Active;
    public string Avatar { get; set; } = "Mambo.png";
    [JsonInclude] public bool IsPremium { get; private set; }
    [JsonInclude] public DateTimeOffset? SubscriptionEndDate { get; private set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public PlayerSettings Settings { get; set; } = new();

    public bool HasPremiumAccess(DateTimeOffset now) =>
        IsPremium && (!SubscriptionEndDate.HasValue || SubscriptionEndDate > now);

    public void ActivatePremium(DateTimeOffset through)
    {
        if (through <= DateTimeOffset.UtcNow)
        {
            throw new DomainException("Subscription end date must be in the future.");
        }

        IsPremium = true;
        SubscriptionEndDate = through;
    }

    public void CancelPremium()
    {
        IsPremium = false;
        SubscriptionEndDate = null;
    }

    public void RequireActive()
    {
        if (Status != AccountStatus.Active)
        {
            throw new DomainException("This account is inactive and cannot perform that action.");
        }
    }
}

public sealed class PlayerSettings
{
    public string Theme { get; set; } = "dark";
    public bool Notifications { get; set; } = true;
    public bool SoundEnabled { get; set; } = true;
    public string Language { get; set; } = "en";
    public bool TwoFactorEnabled { get; set; }
}
