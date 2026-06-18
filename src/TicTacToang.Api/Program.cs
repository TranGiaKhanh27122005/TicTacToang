using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
var configuredConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=Data/tictactoang.db";
var connectionStringBuilder = new SqliteConnectionStringBuilder(configuredConnectionString);
if (!Path.IsPathRooted(connectionStringBuilder.DataSource))
{
    connectionStringBuilder.DataSource = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", connectionStringBuilder.DataSource));
}
Directory.CreateDirectory(Path.GetDirectoryName(connectionStringBuilder.DataSource)!);
var connectionString = connectionStringBuilder.ToString();

builder.Services.AddDbContextFactory<ApplicationDbContext>(options => options.UseSqlite(connectionString));
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
auth.MapPost("/login", async (LoginRequest request, PlayerService service) => Results.Ok(await service.LoginAsync(request)));
auth.MapPost("/logout", () => Results.Ok(new { success = true, message = "Logged out." }));
auth.MapPost("/change-password", async (ChangePasswordRequest request, PlayerService service) =>
{
    await service.ChangePasswordAsync(request.PlayerId, request.CurrentPassword, request.NewPassword);
    return Results.Ok(new { success = true });
});

var profile = app.MapGroup("/api/profile");
profile.MapGet("/{playerId:guid}", (Guid playerId, PlayerService service) => service.Get(playerId));
profile.MapPut("/{playerId:guid}", async (Guid playerId, UpdateProfileRequest request, PlayerService service) =>
    await service.UpdateProfileAsync(playerId, request));
profile.MapPost("/{playerId:guid}/subscription", async (Guid playerId, PlayerService service) =>
    await service.ActivateSubscriptionAsync(playerId));

var games = app.MapGroup("/api/games");
games.MapGet("/", (MatchService service) => service.List());
games.MapGet("/user/{playerId:guid}/history", (Guid playerId, MatchService service) => service.History(playerId));
games.MapGet("/{id}", (string id, MatchService service) => service.Get(id));
games.MapGet("/{id}/replay", (string id, MatchService service) => service.Get(id));
games.MapPost("/", async (CreateMatchRequest request, MatchService service) => Results.Created("/api/games", await service.CreateAsync(request)));
games.MapPost("/{id}/join/{playerId:guid}", async (string id, Guid playerId, MatchService service) => await service.JoinAsync(id, playerId));
games.MapPost("/{id}/move", async (string id, PlayMoveRequest request, MatchService service) => await service.PlayAsync(id, request));
games.MapPost("/{id}/resign/{playerId:guid}", async (string id, Guid playerId, MatchService service) => await service.ResignAsync(id, playerId));

var rooms = app.MapGroup("/api/gameroom");
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

var social = app.MapGroup("/api/social");
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

var admin = app.MapGroup("/api/admin");
admin.MapGet("/users", (PlayerService service) => service.GetAll());
admin.MapGet("/dashboard", (AdminDashboardService service) => service.GetStats());
admin.MapGet("/users/search", (string? search, string? role, string? status, string? premium, PlayerService service) =>
    service.Search(new AdminUserQuery(search, role, status, premium)));
admin.MapPatch("/users/{id:guid}", async (Guid id, AdminPlayerInput request, PlayerService service) =>
    await service.SetAccountStatusAsync(id, request.Active ? AccountStatus.Active : AccountStatus.Inactive, request.Premium));
admin.MapGet("/games", (MatchService service) => service.List());
admin.MapPost("/games/{id}/abort", async (string id, MatchService service) => await service.AbortAsync(id));

app.MapHub<GameRoomHub>("/hubs/gameroom");
app.Run();

public sealed record ChangePasswordRequest(Guid PlayerId, string CurrentPassword, string NewPassword);
public sealed record ChatRequest(string Text);
public sealed record FriendRequestInput(Guid RequesterId, Guid RecipientId);
public sealed record RoomInviteInput(Guid SenderId, Guid RecipientId, Guid RoomId);
public sealed record AdminPlayerInput(bool Active, bool? Premium);
