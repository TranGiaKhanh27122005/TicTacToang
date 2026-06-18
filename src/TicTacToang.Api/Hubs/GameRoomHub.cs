using Microsoft.AspNetCore.SignalR;

namespace TicTacToang.Api.Hubs;

public sealed class GameRoomHub : Hub
{
    public Task JoinRoom(Guid roomId) => Groups.AddToGroupAsync(Context.ConnectionId, Room(roomId));
    public Task LeaveRoom(Guid roomId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, Room(roomId));
    public Task PublishChat(Guid roomId, object message) => Clients.Group(Room(roomId)).SendAsync("chat-message", message);
    public Task PublishMove(Guid roomId, object move) => Clients.OthersInGroup(Room(roomId)).SendAsync("game-move-applied", move);
    public Task PublishSettings(Guid roomId, object settings) => Clients.Group(Room(roomId)).SendAsync("settings-updated", settings);
    private static string Room(Guid roomId) => $"room:{roomId}";
}
