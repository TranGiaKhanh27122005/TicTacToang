using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using TicTacToang.Api.Hubs;
using TicTacToang.Application.Abstractions;
using TicTacToang.Application.Contracts;
using TicTacToang.Application.Services;
using TicTacToang.Domain.Common;
using TicTacToang.Domain.Matches;
using TicTacToang.Domain.Players;
using TicTacToang.Infrastructure.Persistence;
using TicTacToang.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);
var databaseProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connectionString = BuildConnectionString(builder.Configuration, builder.Environment.ContentRootPath, databaseProvider);
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
{
    if (databaseProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase) ||
        databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
    {
        options.UseNpgsql(connectionString);
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});

var jwtKey = builder.Configuration["Jwt:Key"] ?? "TicTacToang-development-jwt-key-change-me-please";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "TicTacToang";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "TicTacToang.Client";
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("admin"));
});
builder.Services.AddSingleton<IApplicationStore, SqliteApplicationStore>();
builder.Services.AddSingleton<IPasswordService, Pbkdf2PasswordService>();
builder.Services.AddSingleton<PlayerService>();
builder.Services.AddSingleton<GomokuAi>();
builder.Services.AddSingleton<MatchService>();
builder.Services.AddSingleton<RoomService>();
builder.Services.AddSingleton<SocialService>();
builder.Services.AddSingleton<AdminDashboardService>();
builder.Services.AddSignalR();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials()));

var app = builder.Build();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (DomainException exception)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { message = exception.Message });
    }
});

app.MapGet("/api/health", () => new { status = "ok", timestamp = DateTimeOffset.UtcNow });

var auth = app.MapGroup("/api/auth");
auth.MapPost("/register", async (RegisterRequest request, PlayerService service) => Results.Created("/api/profile", await service.RegisterAsync(request)));
auth.MapPost("/login", async (LoginRequest request, PlayerService service) =>
{
    var result = await service.LoginAsync(request);
    return Results.Ok(result with { Token = CreateJwt(result.Player, jwtIssuer, jwtAudience, signingKey) });
});
auth.MapPost("/logout", () => Results.Ok(new { success = true, message = "Logged out." }));
auth.MapPost("/change-password", async (ChangePasswordRequest request, PlayerService service) =>
{
    await service.ChangePasswordAsync(request.PlayerId, request.CurrentPassword, request.NewPassword);
    return Results.Ok(new { success = true });
}).RequireAuthorization();

var profile = app.MapGroup("/api/profile").RequireAuthorization();
profile.MapGet("/{playerId:guid}", (Guid playerId, PlayerService service) => service.Get(playerId));
profile.MapPut("/{playerId:guid}", async (Guid playerId, UpdateProfileRequest request, PlayerService service) =>
    await service.UpdateProfileAsync(playerId, request));
profile.MapPost("/{playerId:guid}/subscription", async (Guid playerId, PlayerService service) =>
    await service.ActivateSubscriptionAsync(playerId));

var games = app.MapGroup("/api/games").RequireAuthorization();
games.MapGet("/", (MatchService service) => service.List());
games.MapGet("/user/{playerId:guid}/history", (Guid playerId, MatchService service) => service.History(playerId));
games.MapGet("/{id}", (string id, MatchService service) => service.Get(id));
games.MapGet("/{id}/replay", (string id, MatchService service) => service.Get(id));
games.MapPost("/", async (CreateMatchRequest request, MatchService service) => Results.Created("/api/games", await service.CreateAsync(request)));
games.MapPost("/{id}/join/{playerId:guid}", async (string id, Guid playerId, MatchService service) => await service.JoinAsync(id, playerId));
games.MapPost("/{id}/move", async (string id, PlayMoveRequest request, MatchService service) => await service.PlayAsync(id, request));
games.MapPost("/{id}/resign/{playerId:guid}", async (string id, Guid playerId, MatchService service) => await service.ResignAsync(id, playerId));

var rooms = app.MapGroup("/api/gameroom").RequireAuthorization();
rooms.MapGet("/", (RoomService service) => service.List());
rooms.MapPost("/{hostId:guid}", async (Guid hostId, CreateRoomRequest request, RoomService service) =>
    Results.Created("/api/gameroom", await service.CreateAsync(hostId, request)));
rooms.MapPost("/{roomId:guid}/player/{playerId:guid}", async (Guid roomId, Guid playerId, RoomService service) => await service.JoinAsync(roomId, playerId));
rooms.MapPost("/{roomId:guid}/ai/{hostId:guid}/{difficulty}", async (Guid roomId, Guid hostId, AiDifficulty difficulty, RoomService service) =>
    await service.AddAiAsync(roomId, hostId, difficulty));
rooms.MapPost("/{roomId:guid}/chat/{senderId:guid}", async (Guid roomId, Guid senderId, ChatRequest request, RoomService service) =>
{
    await service.PostChatAsync(roomId, senderId, request.Text);
    return Results.Ok();
});
rooms.MapPost("/{roomId:guid}/start/{hostId:guid}", async (Guid roomId, Guid hostId, RoomService service) => await service.StartAsync(roomId, hostId));
rooms.MapDelete("/{roomId:guid}/{hostId:guid}", async (Guid roomId, Guid hostId, RoomService service) =>
{
    await service.DeleteAsync(roomId, hostId);
    return Results.NoContent();
});

var social = app.MapGroup("/api/social").RequireAuthorization();
social.MapGet("/{playerId:guid}/requests", (Guid playerId, SocialService service) => service.Requests(playerId));
social.MapGet("/{playerId:guid}/friends", (Guid playerId, SocialService service) => service.Friends(playerId));
social.MapGet("/{playerId:guid}/invites", (Guid playerId, SocialService service) => service.Invites(playerId));
social.MapPost("/friend-requests", async (FriendRequestInput request, SocialService service) => await service.SendRequestAsync(request.RequesterId, request.RecipientId));
social.MapPost("/friend-requests/{id:guid}/accept/{recipientId:guid}", async (Guid id, Guid recipientId, SocialService service) =>
{
    await service.AcceptRequestAsync(id, recipientId);
    return Results.Ok();
});
social.MapPost("/room-invites", async (RoomInviteInput request, SocialService service) => await service.InviteToRoomAsync(request.SenderId, request.RecipientId, request.RoomId));

var admin = app.MapGroup("/api/admin").RequireAuthorization("AdminOnly");
admin.MapGet("/users", (PlayerService service) => service.GetAll());
admin.MapGet("/dashboard", (AdminDashboardService service) => service.GetStats());
admin.MapGet("/suspicious-matches", (AdminDashboardService service) => service.GetSuspiciousMatches());
admin.MapGet("/ai-analytics", (AdminDashboardService service) => service.GetAiAnalytics());
admin.MapGet("/users/search", (string? search, string? role, string? status, string? premium, PlayerService service) =>
    service.Search(new AdminUserQuery(search, role, status, premium)));
admin.MapPatch("/users/{id:guid}", async (Guid id, AdminPlayerInput request, PlayerService service) =>
    await service.SetAccountStatusAsync(id, request.Active ? AccountStatus.Active : AccountStatus.Inactive, request.Premium));
admin.MapGet("/games", (MatchService service) => service.List());
admin.MapPost("/games/{id}/abort", async (string id, MatchService service) => await service.AbortAsync(id));

app.MapHub<GameRoomHub>("/hubs/gameroom");
app.Run();

static string BuildConnectionString(IConfiguration configuration, string contentRootPath, string provider)
{
    var configuredConnectionString = configuration.GetConnectionString("DefaultConnection")
        ?? configuration["DATABASE_URL"]
        ?? "Data Source=Data/tictactoang.db";

    if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
    {
        return NormalizePostgresConnectionString(configuredConnectionString);
    }

    var connectionStringBuilder = new SqliteConnectionStringBuilder(configuredConnectionString);
    if (!Path.IsPathRooted(connectionStringBuilder.DataSource))
    {
        connectionStringBuilder.DataSource = Path.GetFullPath(Path.Combine(contentRootPath, "..", "..", connectionStringBuilder.DataSource));
    }
    Directory.CreateDirectory(Path.GetDirectoryName(connectionStringBuilder.DataSource)!);
    return connectionStringBuilder.ToString();
}

static string NormalizePostgresConnectionString(string connectionString)
{
    if (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
        !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        return connectionString;
    }

    var uri = new Uri(connectionString);
    var userInfo = uri.UserInfo.Split(':', 2);
    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Database = uri.AbsolutePath.TrimStart('/'),
        Username = Uri.UnescapeDataString(userInfo[0]),
        Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
        SslMode = SslMode.Require
    };
    return builder.ToString();
}

static string CreateJwt(PlayerView player, string issuer, string audience, SecurityKey signingKey)
{
    var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, player.Id.ToString()),
        new Claim(JwtRegisteredClaimNames.UniqueName, player.Username),
        new Claim(ClaimTypes.NameIdentifier, player.Id.ToString()),
        new Claim(ClaimTypes.Name, player.Name),
        new Claim(ClaimTypes.Email, player.Email),
        new Claim(ClaimTypes.Role, player.Role)
    };
    var token = new JwtSecurityToken(
        issuer: issuer,
        audience: audience,
        claims: claims,
        expires: DateTime.UtcNow.AddHours(8),
        signingCredentials: credentials);
    return new JwtSecurityTokenHandler().WriteToken(token);
}

public sealed record ChangePasswordRequest(Guid PlayerId, string CurrentPassword, string NewPassword);
public sealed record ChatRequest(string Text);
public sealed record FriendRequestInput(Guid RequesterId, Guid RecipientId);
public sealed record RoomInviteInput(Guid SenderId, Guid RecipientId, Guid RoomId);
public sealed record AdminPlayerInput(bool Active, bool? Premium);
