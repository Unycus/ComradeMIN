using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

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

    // Измененный метод SendMessage - добавляем messageId
    public async Task SendMessage(int chatId, int userId, string message, int messageId)
    {
        await Clients.Group($"chat_{chatId}").SendAsync("ReceiveMessage", chatId, userId, message, messageId);
    }

    // Метод для отправки файла
    public async Task SendFile(int chatId, int userId, string fileName, int messageId)
    {
        await Clients.Group($"chat_{chatId}").SendAsync("ReceiveFile", chatId, userId, fileName, messageId);
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