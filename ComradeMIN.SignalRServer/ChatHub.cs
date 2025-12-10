using Microsoft.AspNetCore.SignalR;

public class ChatHub : Hub
{
    public async Task JoinChatGroup(int chatId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");
        await Clients.Group($"chat_{chatId}").SendAsync("UserJoined", Context.ConnectionId);
    }

    public async Task LeaveChatGroup(int chatId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat_{chatId}");
    }

    public async Task SendMessage(int chatId, int userId, string message, int messageId)
    {
        await Clients.Group($"chat_{chatId}").SendAsync("ReceiveMessage", chatId, userId, message, messageId);
    }

    public async Task NotifyChatListUpdate(int userId)
    {
        await Clients.User(userId.ToString()).SendAsync("RefreshChats");
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}