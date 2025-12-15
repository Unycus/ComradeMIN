using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

namespace ComradeMIN
{
    public class SignalRManager : IDisposable
    {
        private HubConnection _hubConnection;
        private string _url;
        private int _userId;
        private bool _isConnected = false;
        private bool _isDisposed = false;
        private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentQueue<(string method, object[] args)> _pendingMessages = new ConcurrentQueue<(string, object[])>();

        public event Action<int, int, string, int> OnMessageReceived;
        public event Action<int, int, string, int> OnFileReceived;

        public SignalRManager(string url, int userId)
        {
            _url = url;
            _userId = userId;
        }

        public async Task<bool> ConnectAsync()
        {
            await _connectionLock.WaitAsync();
            try
            {
                if (_isConnected && _hubConnection?.State == HubConnectionState.Connected)
                    return true;

                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(_url)
                    .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
                    .Build();

                SetupCallbacks();

                await _hubConnection.StartAsync();
                _isConnected = true;

                // Отправляем ожидающие сообщения
                await SendPendingMessages();

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка подключения SignalR: {ex.Message}");
                _isConnected = false;
                return false;
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private void SetupCallbacks()
        {
            _hubConnection.On<int, int, string, int>("ReceiveMessage", (chatId, userId, message, messageId) =>
            {
                OnMessageReceived?.Invoke(chatId, userId, message, messageId);
            });

            _hubConnection.On<int, int, string, int>("ReceiveFile", (chatId, userId, fileName, messageId) =>
            {
                OnFileReceived?.Invoke(chatId, userId, fileName, messageId);
            });

            _hubConnection.Closed += async (error) =>
            {
                _isConnected = false;
                Debug.WriteLine($"SignalR соединение закрыто: {error?.Message}");
                await Task.Delay(2000);
                await ConnectAsync();
            };

            _hubConnection.Reconnected += (connectionId) =>
            {
                _isConnected = true;
                Debug.WriteLine("SignalR переподключен");
                return Task.CompletedTask;
            };
        }

        public async Task SendMessageAsync(string method, params object[] args)
        {
            if (_isConnected && _hubConnection?.State == HubConnectionState.Connected)
            {
                try
                {
                    await _hubConnection.SendCoreAsync(method, args);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Ошибка отправки сообщения: {ex.Message}");
                    _pendingMessages.Enqueue((method, args));
                }
            }
            else
            {
                _pendingMessages.Enqueue((method, args));
                await ConnectAsync();
            }
        }

        private async Task SendPendingMessages()
        {
            if (_pendingMessages.IsEmpty) return;

            while (_pendingMessages.TryDequeue(out var message))
            {
                try
                {
                    if (_isConnected && _hubConnection?.State == HubConnectionState.Connected)
                    {
                        await _hubConnection.SendCoreAsync(message.method, message.args);
                    }
                    else
                    {
                        _pendingMessages.Enqueue(message);
                    }
                }
                catch
                {
                    _pendingMessages.Enqueue(message);
                }
            }
        }

        public async Task JoinChatGroupAsync(int chatId)
        {
            await SendMessageAsync("JoinChatGroup", chatId);
        }

        public async Task LeaveChatGroupAsync(int chatId)
        {
            await SendMessageAsync("LeaveChatGroup", chatId);
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;

            try
            {
                // Безопасное освобождение ресурсов
                _hubConnection?.DisposeAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка при освобождении SignalR: {ex.Message}");
            }
            finally
            {
                _connectionLock.Dispose();
            }
        }
    }
}