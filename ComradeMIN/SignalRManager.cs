using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ComradeMIN
{
    public class SignalRManager : IDisposable
    {
        private HubConnection _hubConnection;
        private readonly string _url;
        private readonly int _userId;
        private bool _isConnected = false;
        private bool _isDisposed = false;
        private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentQueue<(string method, object[] args)> _pendingMessages = new();
        private readonly CancellationTokenSource _globalCts = new CancellationTokenSource();
        private readonly string _connectionToken;

        public event Action<int, int, string, int> OnMessageReceived;
        public event Action<int, int, string, int> OnFileReceived;

        public SignalRManager(int userId)
        {
            _userId = userId;

            try
            {
                // Загрузка конфигурации
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("AppSettings.json", optional: true, reloadOnChange: false)
                    .Build();

                _url = configuration["SignalR:Url"] ?? "http://26.19.50.66:5000/chatHub";

                // Генерация токена подключения для безопасности
                _connectionToken = GenerateConnectionToken(userId);

                LoggingService.LogInfo($"SignalR Manager initialized for user {userId}");
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Failed to initialize SignalRManager: {ex.Message}", ex);
                throw;
            }
        }

        private string GenerateConnectionToken(int userId)
        {
            using (var sha256 = SHA256.Create())
            {
                string input = $"{userId}_{DateTime.UtcNow:yyyyMMdd}_{Guid.NewGuid()}";
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                return Convert.ToBase64String(hash);
            }
        }

        public async Task<bool> ConnectAsync()
        {
            await _connectionLock.WaitAsync();

            try
            {
                if (_isConnected && _hubConnection?.State == HubConnectionState.Connected)
                    return true;

                // Закрываем существующее подключение, если есть
                if (_hubConnection != null)
                {
                    await SafeDisconnectAsync();
                }

                LoggingService.LogDebug($"Attempting SignalR connection to {_url}");

                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(_url, options =>
                    {
                        options.Headers["X-User-Id"] = _userId.ToString();
                        options.Headers["X-Connection-Token"] = _connectionToken;
                        options.Headers["X-Client-Version"] = "1.0.0";
                    })
                    .WithAutomaticReconnect(new RetryPolicy())
                    .Build();

                SetupCallbacks();

                // Подключение с таймаутом
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _globalCts.Token, timeoutCts.Token);

                try
                {
                    await _hubConnection.StartAsync(linkedCts.Token);
                    _isConnected = true;

                    LoggingService.LogInfo($"SignalR connected successfully. Connection ID: {_hubConnection.ConnectionId}");

                    // Отправляем ожидающие сообщения
                    await SendPendingMessagesAsync();

                    return true;
                }
                catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
                {
                    LoggingService.LogWarning("SignalR connection timeout");
                    _isConnected = false;
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"SignalR connection error: {ex.Message}", ex);
                _isConnected = false;
                return false;
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private async Task SafeDisconnectAsync()
        {
            try
            {
                if (_hubConnection != null)
                {
                    await _hubConnection.StopAsync();
                    await _hubConnection.DisposeAsync();
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Error during SignalR disconnect: {ex.Message}", ex);
            }
            finally
            {
                _hubConnection = null;
                _isConnected = false;
            }
        }

        private void SetupCallbacks()
        {
            _hubConnection.On<int, int, string, int>("ReceiveMessage", (chatId, userId, message, messageId) =>
            {
                LoggingService.LogDebug($"Received message {messageId} in chat {chatId}");
                OnMessageReceived?.Invoke(chatId, userId, message, messageId);
            });

            _hubConnection.On<int, int, string, int>("ReceiveFile", (chatId, userId, fileName, messageId) =>
            {
                LoggingService.LogDebug($"Received file {fileName} in chat {chatId}");
                OnFileReceived?.Invoke(chatId, userId, fileName, messageId);
            });

            _hubConnection.Closed += async (error) =>
            {
                _isConnected = false;
                LoggingService.LogWarning($"SignalR connection closed: {error?.Message}");

                // Попытка переподключения через 5 секунд
                await Task.Delay(5000);
                await ConnectAsync();
            };

            _hubConnection.Reconnected += (connectionId) =>
            {
                _isConnected = true;
                LoggingService.LogInfo($"SignalR reconnected. New connection ID: {connectionId}");
                return Task.CompletedTask;
            };

            _hubConnection.Reconnecting += (error) =>
            {
                LoggingService.LogWarning($"SignalR reconnecting: {error?.Message}");
                return Task.CompletedTask;
            };
        }

        public async Task SendMessageAsync(string method, params object[] args)
        {
            if (_isConnected && _hubConnection?.State == HubConnectionState.Connected)
            {
                try
                {
                    using var sendCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await _hubConnection.SendCoreAsync(method, args, sendCts.Token);
                }
                catch (Exception ex)
                {
                    LoggingService.LogError($"Error sending SignalR message: {ex.Message}", ex);
                    _pendingMessages.Enqueue((method, args));
                }
            }
            else
            {
                _pendingMessages.Enqueue((method, args));

                // Попытка подключения, если не подключены
                if (!_isConnected)
                {
                    await ConnectAsync();
                }
            }
        }

        private async Task SendPendingMessagesAsync()
        {
            if (_pendingMessages.IsEmpty) return;

            LoggingService.LogDebug($"Sending {_pendingMessages.Count} pending messages");

            while (_pendingMessages.TryDequeue(out var message))
            {
                try
                {
                    if (_isConnected && _hubConnection?.State == HubConnectionState.Connected)
                    {
                        using var sendCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await _hubConnection.SendCoreAsync(message.method, message.args, sendCts.Token);
                    }
                    else
                    {
                        // Возвращаем в очередь, если не удалось отправить
                        _pendingMessages.Enqueue(message);
                        break;
                    }
                }
                catch
                {
                    _pendingMessages.Enqueue(message);
                    break;
                }
            }
        }

        public async Task JoinChatGroupAsync(int chatId)
        {
            try
            {
                await SendMessageAsync("JoinChatGroup", chatId);
                LoggingService.LogDebug($"User {_userId} joined chat group {chatId}");
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Error joining chat group: {ex.Message}", ex);
            }
        }

        public async Task LeaveChatGroupAsync(int chatId)
        {
            try
            {
                await SendMessageAsync("LeaveChatGroup", chatId);
                LoggingService.LogDebug($"User {_userId} left chat group {chatId}");
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Error leaving chat group: {ex.Message}", ex);
            }
        }

        // Политика повторного подключения
        private class RetryPolicy : IRetryPolicy
        {
            public TimeSpan? NextRetryDelay(RetryContext retryContext)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, retryContext.PreviousRetryCount)));
                LoggingService.LogDebug($"SignalR retry attempt {retryContext.PreviousRetryCount + 1}, waiting {delay.TotalSeconds}s");
                return delay;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;
            LoggingService.LogDebug("Disposing SignalRManager");

            try
            {
                _globalCts.Cancel();
                _globalCts.Dispose();

                _connectionLock.Dispose();

                // Асинхронное освобождение в синхронном методе
                Task.Run(async () => await SafeDisconnectAsync()).Wait(5000);
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Error disposing SignalRManager: {ex.Message}", ex);
            }
        }
    }
}