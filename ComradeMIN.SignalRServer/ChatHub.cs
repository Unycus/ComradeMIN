using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Claims;

public class ChatHub : Hub
{
    private readonly ILogger<ChatHub> _logger;
    private static readonly Dictionary<string, UserConnection> _connections = new();

    public ChatHub(ILogger<ChatHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        try
        {
            var userId = GetUserIdFromContext();
            var connectionId = Context.ConnectionId;

            _connections[connectionId] = new UserConnection
            {
                UserId = userId,
                ConnectionId = connectionId,
                ConnectedAt = DateTime.UtcNow
            };

            _logger.LogInformation("User {UserId} connected with connection ID {ConnectionId}",
                userId, connectionId);

            await base.OnConnectedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnConnectedAsync");
            throw;
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            var userId = GetUserIdFromContext();
            var connectionId = Context.ConnectionId;

            _connections.Remove(connectionId);

            _logger.LogInformation("User {UserId} disconnected from connection ID {ConnectionId}",
                userId, connectionId);

            if (exception != null)
            {
                _logger.LogError(exception, "Disconnection exception for user {UserId}", userId);
            }

            await base.OnDisconnectedAsync(exception);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnDisconnectedAsync");
            throw;
        }
    }

    [HubMethodName("JoinChatGroup")]
    public async Task JoinChatGroup(int chatId)
    {
        var userId = GetUserIdFromContext();
        var groupName = GetGroupName(chatId);

        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        _logger.LogDebug("User {UserId} joined chat group {ChatId}", userId, chatId);

        await Clients.Group(groupName).SendAsync("UserJoined", new
        {
            UserId = userId,
            ChatId = chatId,
            Timestamp = DateTime.UtcNow
        });
    }

    [HubMethodName("LeaveChatGroup")]
    public async Task LeaveChatGroup(int chatId)
    {
        var userId = GetUserIdFromContext();
        var groupName = GetGroupName(chatId);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

        _logger.LogDebug("User {UserId} left chat group {ChatId}", userId, chatId);

        await Clients.Group(groupName).SendAsync("UserLeft", new
        {
            UserId = userId,
            ChatId = chatId,
            Timestamp = DateTime.UtcNow
        });
    }

    [HubMethodName("SendMessage")]
    public async Task SendMessage(int chatId, int userId, string message, int messageId)
    {
        try
        {
            var callerUserId = GetUserIdFromContext();
            if (callerUserId != userId)
            {
                _logger.LogWarning("User ID mismatch: caller={CallerId}, param={ParamId}",
                    callerUserId, userId);
                throw new HubException("User ID mismatch");
            }

            var groupName = GetGroupName(chatId);

            _logger.LogDebug("User {UserId} sending message {MessageId} to chat {ChatId}",
                userId, messageId, chatId);

            await Clients.GroupExcept(groupName, Context.ConnectionId)
                .SendAsync("ReceiveMessage", chatId, userId, message, messageId);

            await Clients.Caller.SendAsync("MessageSent", new
            {
                MessageId = messageId,
                ChatId = chatId,
                Timestamp = DateTime.UtcNow
            });

            await Clients.Group(groupName).SendAsync("UpdateChats");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SendMessage");
            throw;
        }
    }

    [HubMethodName("SendFile")]
    public async Task SendFile(int chatId, int userId, string fileName, int messageId)
    {
        try
        {
            var callerUserId = GetUserIdFromContext();
            if (callerUserId != userId)
            {
                throw new HubException("User ID mismatch");
            }

            var groupName = GetGroupName(chatId);

            _logger.LogDebug("User {UserId} sending file {FileName} to chat {ChatId}",
                userId, fileName, chatId);

            await Clients.GroupExcept(groupName, Context.ConnectionId)
                .SendAsync("ReceiveFile", chatId, userId, fileName, messageId);

            await Clients.Caller.SendAsync("FileSent", new
            {
                MessageId = messageId,
                FileName = fileName,
                Timestamp = DateTime.UtcNow
            });

            await Clients.Group(groupName).SendAsync("UpdateChats");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SendFile");
            throw;
        }
    }

    [HubMethodName("NotifyChatsUpdated")]
    public async Task NotifyChatsUpdated(int userId)
    {
        try
        {
            var userConnections = _connections.Where(c => c.Value.UserId == userId)
                                              .Select(c => c.Key)
                                              .ToList();

            foreach (var connectionId in userConnections)
            {
                await Clients.Client(connectionId).SendAsync("UpdateChats");
            }

            _logger.LogDebug("Sent chat update notification to user {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in NotifyChatsUpdated");
            throw;
        }
    }

    private int GetUserIdFromContext()
    {
        try
        {
            var httpContext = Context.GetHttpContext();
            if (httpContext?.Request?.Headers != null)
            {
                if (httpContext.Request.Headers.TryGetValue("X-User-Id", out var userIdHeader))
                {
                    if (int.TryParse(userIdHeader, out int userId))
                    {
                        return userId;
                    }
                }

                if (httpContext.Request.Query.TryGetValue("userId", out var queryUserId))
                {
                    if (int.TryParse(queryUserId, out int userId))
                    {
                        return userId;
                    }
                }
            }

            throw new HubException("User ID not found in request");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user ID from context");
            throw new HubException("Authentication error");
        }
    }

    private string GetGroupName(int chatId) => $"chat_{chatId}";

    private class UserConnection
    {
        public int UserId { get; set; }
        public string ConnectionId { get; set; } = string.Empty;
        public DateTime ConnectedAt { get; set; }
    }
}