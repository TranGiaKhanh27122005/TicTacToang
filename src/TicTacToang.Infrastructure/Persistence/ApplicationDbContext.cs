using Microsoft.EntityFrameworkCore;

namespace TicTacToang.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationStateRecord> ApplicationStates => Set<ApplicationStateRecord>();
    public DbSet<PlayerRecord> Players => Set<PlayerRecord>();
    public DbSet<MatchRecord> Matches => Set<MatchRecord>();
    public DbSet<GameRoomRecord> Rooms => Set<GameRoomRecord>();
    public DbSet<FriendRequestRecord> FriendRequests => Set<FriendRequestRecord>();
    public DbSet<FriendshipRecord> Friendships => Set<FriendshipRecord>();
    public DbSet<RoomInviteRecord> RoomInvites => Set<RoomInviteRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationStateRecord>(entity =>
        {
            entity.ToTable("ApplicationStates");
            entity.HasKey(state => state.Id);
            entity.Property(state => state.Payload).IsRequired();
            entity.Property(state => state.UpdatedAt).IsRequired();
        });

        modelBuilder.Entity<PlayerRecord>(entity =>
        {
            entity.ToTable("Players");
            entity.HasKey(player => player.Id);
            entity.HasIndex(player => player.Username).IsUnique();
            entity.HasIndex(player => player.Email).IsUnique();
            entity.Property(player => player.Payload).IsRequired();
        });

        modelBuilder.Entity<MatchRecord>(entity =>
        {
            entity.ToTable("Matches");
            entity.HasKey(match => match.Id);
            entity.HasIndex(match => match.StartedAt);
            entity.Property(match => match.Payload).IsRequired();
        });

        modelBuilder.Entity<GameRoomRecord>(entity =>
        {
            entity.ToTable("Rooms");
            entity.HasKey(room => room.Id);
            entity.HasIndex(room => room.RoomCode).IsUnique();
            entity.Property(room => room.Payload).IsRequired();
        });

        modelBuilder.Entity<FriendRequestRecord>(entity =>
        {
            entity.ToTable("FriendRequests");
            entity.HasKey(request => request.Id);
            entity.HasIndex(request => request.RecipientId);
            entity.Property(request => request.Payload).IsRequired();
        });

        modelBuilder.Entity<FriendshipRecord>(entity =>
        {
            entity.ToTable("Friendships");
            entity.HasKey(friendship => friendship.Id);
            entity.HasIndex(friendship => friendship.UserA);
            entity.HasIndex(friendship => friendship.UserB);
            entity.Property(friendship => friendship.Payload).IsRequired();
        });

        modelBuilder.Entity<RoomInviteRecord>(entity =>
        {
            entity.ToTable("RoomInvites");
            entity.HasKey(invite => invite.Id);
            entity.HasIndex(invite => invite.RecipientId);
            entity.Property(invite => invite.Payload).IsRequired();
        });
    }
}

public sealed class ApplicationStateRecord
{
    public int Id { get; set; }
    public string Payload { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PlayerRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string Country { get; set; } = "";
    public string Role { get; set; } = "";
    public string Status { get; set; } = "";
    public bool IsPremium { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public string Payload { get; set; } = "";
}

public sealed class MatchRecord
{
    public string Id { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Status { get; set; } = "";
    public int BoardSize { get; set; }
    public int MoveCount { get; set; }
    public Guid? PlayerXId { get; set; }
    public Guid? PlayerOId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string Payload { get; set; } = "";
}

public sealed class GameRoomRecord
{
    public Guid Id { get; set; }
    public int RoomCode { get; set; }
    public string Name { get; set; } = "";
    public Guid HostId { get; set; }
    public int Capacity { get; set; }
    public string Status { get; set; } = "";
    public int MemberCount { get; set; }
    public int BoardSize { get; set; }
    public string BoardStyle { get; set; } = "";
    public string Payload { get; set; } = "";
}

public sealed class FriendRequestRecord
{
    public Guid Id { get; set; }
    public Guid RequesterId { get; set; }
    public Guid RecipientId { get; set; }
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string Payload { get; set; } = "";
}

public sealed class FriendshipRecord
{
    public string Id { get; set; } = "";
    public Guid UserA { get; set; }
    public Guid UserB { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Payload { get; set; } = "";
}

public sealed class RoomInviteRecord
{
    public Guid Id { get; set; }
    public Guid SenderId { get; set; }
    public Guid RecipientId { get; set; }
    public Guid RoomId { get; set; }
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string Payload { get; set; } = "";
}
