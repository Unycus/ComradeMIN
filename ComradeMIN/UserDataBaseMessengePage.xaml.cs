using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using IOPath = System.IO.Path;

namespace ComradeMIN
{
    public partial class UserDataBaseMessengePage : Page
    {
        private string connectionString;
        private int currentUserID;
        private int currentChatID = 0;
        private bool isAutoScrolling = true;
        private bool _isUserAtBottom = true;
        private bool _isLoadingSequentially = false;
        private int _lastLoadedMessageId = 0;
        private SignalRManager _signalRManager;
        private string _signalRUrl = "http://26.19.50.66:5000/chatHub";
        private System.Windows.Threading.DispatcherTimer _chatUpdateTimer;

        private CacheManager _cacheManager;

        private Dictionary<int, List<CachedMessage>> _chatMessages = new();
        private Dictionary<int, int> _unreadCounts = new();
        private Dictionary<int, Border> _chatButtons = new();
        private Dictionary<int, ChatInfo> _chatInfos = new();

        private readonly SemaphoreSlim _loadLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        private bool _isInitialized = false;
        private IConfiguration _configuration;

        private Dictionary<int, CachedFile> _fileCacheStatus = new Dictionary<int, CachedFile>();

        private class ChatInfo
        {
            public int ChatId { get; set; }
            public string ChatName { get; set; }
            public string LastMessage { get; set; }
            public int UnreadCount { get; set; }
            public DateTime LastMessageDate { get; set; }
            public string ComradeName { get; set; }
            public byte[] ComradeAvatar { get; set; }
            public string ComradeStatus { get; set; }
            public DateTime? ComradeLastLogin { get; set; }
        }

        public UserDataBaseMessengePage(int userID)
        {
            InitializeComponent();
            currentUserID = userID;

            Debug.WriteLine($"Создана страница чатов для пользователя ID: {currentUserID}");

            if (Fellows == null || Reports == null)
            {
                MessageBox.Show("Ошибка загрузки элементов интерфейса");
                return;
            }

            try
            {
                _configuration = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("AppSettings.json", optional: true, reloadOnChange: false)
                    .Build();

                connectionString = _configuration.GetConnectionString("VoidConnection");
                _signalRUrl = _configuration["SignalR:Url"] ?? "http://26.19.50.66:5000/chatHub";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка загрузки конфигурации: {ex.Message}");
                connectionString = "Server=tcp:26.19.50.66\\SQLEXPRESS,1433;" +
                                  "Database=Void;" +
                                  "User Id=VoidUser;" +
                                  "Password=VoidUser123;" +
                                  "Connection Timeout=30;" +
                                  "TrustServerCertificate=True;";
            }

            MessagesScrollViewer.ScrollChanged += MessagesScrollViewer_ScrollChanged;
            Text_for_Comrade.KeyDown += Text_for_Comrade_KeyDown;

            this.Loaded += Page_Loaded;
            this.Unloaded += Page_Unloaded;
        }

        private async Task ShowDatabaseErrorAsync(SqlException sqlEx)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                string userMessage = sqlEx.Number switch
                {
                    -2 => "Сервер не отвечает. Проверьте подключение к VPN.",
                    18456 => "Ошибка авторизации. Проверьте логин и пароль.",
                    4060 => "Невозможно подключиться к базе данных. Проверьте настройки.",
                    _ => $"Ошибка базы данных: {sqlEx.Message}"
                };

                MessageBox.Show(userMessage, "Ошибка подключения",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Page_Loaded вызван");

            if (_isInitialized && _signalRManager != null)
            {
                Debug.WriteLine("Страница уже инициализирована, обновляем чаты");
                await LoadUserChatsAsync();
                return;
            }

            await InitializePageAsync();
        }

        private async Task InitializePageAsync()
        {
            if (_isInitialized) return;

            try
            {
                Debug.WriteLine($"Инициализация страницы для пользователя {currentUserID}");

                _cacheManager = new CacheManager(currentUserID);

                _signalRManager = new SignalRManager(currentUserID);
                _signalRManager.OnMessageReceived += OnMessageReceived;
                _signalRManager.OnFileReceived += OnFileReceived;
                _signalRManager.OnChatsUpdated += OnChatsUpdated;

                bool connected = await _signalRManager.ConnectAsync();
                if (!connected)
                {
                    Debug.WriteLine("Не удалось подключиться к SignalR");
                }

                await LoadUserChatsAsync();

                StartPeriodicChatUpdate();

                await Dispatcher.InvokeAsync(() =>
                {
                    Text_for_Comrade.Focus();
                });

                _isInitialized = true;
                Debug.WriteLine("Страница успешно инициализирована");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка инициализации страницы: {ex.Message}");
            }
        }

        private async void OnChatsUpdated()
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                Debug.WriteLine("Обновление списка чатов (триггер от SignalR)");
                await LoadUserChatsAsync();
            });
        }

        private void StartPeriodicChatUpdate()
        {
            _chatUpdateTimer = new System.Windows.Threading.DispatcherTimer();
            _chatUpdateTimer.Interval = TimeSpan.FromSeconds(30);
            _chatUpdateTimer.Tick += async (s, e) =>
            {
                if (_isInitialized && _signalRManager != null)
                {
                    Debug.WriteLine("Периодическое обновление списка чатов");
                    await LoadUserChatsAsync();
                }
            };
            _chatUpdateTimer.Start();
        }

        private async void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Page_Unloaded вызван");

            if (_chatUpdateTimer != null)
            {
                _chatUpdateTimer.Stop();
                _chatUpdateTimer = null;
            }

            if (_isInitialized && _signalRManager != null)
            {
                if (currentChatID != 0)
                {
                    await _signalRManager.LeaveChatGroupAsync(currentChatID);
                }
                Debug.WriteLine("Выход из группы чата выполнен");
            }
        }

        private async Task LoadUserChatsAsync()
        {
            try
            {
                Debug.WriteLine($"=== АСИНХРОННАЯ ЗАГРУЗКА ЧАТОВ ДЛЯ ПОЛЬЗОВАТЕЛЯ ID: {currentUserID} ===");

                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                        SELECT 
                            c.ChatId,
                            u.UserId AS ComradeId,
                            u.UserName AS ComradeName,
                            ISNULL(u.MiniAvatarImage, u.ProfileImage) AS ComradeAvatar,
                            u.Status AS ComradeStatus,
                            u.LastLoginDate AS ComradeLastLogin,
                            ISNULL((SELECT TOP 1 MessageText FROM Messages WHERE ChatId = c.ChatId ORDER BY SendDate DESC), 'Нет сообщений') AS LastMessage,
                            ISNULL((SELECT COUNT(*) FROM Messages m WHERE m.ChatId = c.ChatId AND m.IsRead = 0 AND m.UserId != @UserId), 0) AS UnreadCount,
                            ISNULL((SELECT TOP 1 SendDate FROM Messages WHERE ChatId = c.ChatId ORDER BY SendDate DESC), c.CreatedDate) AS LastMessageDate
                        FROM Chats c
                        INNER JOIN UserChats uc ON c.ChatId = uc.ChatId
                        INNER JOIN Users u ON uc.UserId = u.UserId
                        WHERE uc.ChatId IN (
                            SELECT ChatId 
                            FROM UserChats 
                            WHERE UserId = @UserId
                        ) AND uc.UserId != @UserId
                        ORDER BY LastMessageDate DESC";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserId", currentUserID);

                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            var chatList = new List<ChatInfo>();

                            while (await reader.ReadAsync())
                            {
                                int chatID = reader.GetInt32(0);
                                string comradeName = reader["ComradeName"].ToString();
                                byte[] comradeAvatar = reader.IsDBNull(3) ? null : (byte[])reader[3];
                                string comradeStatus = reader.IsDBNull(4) ? "offline" : reader["ComradeStatus"].ToString();
                                DateTime? comradeLastLogin = reader.IsDBNull(5) ? (DateTime?)null : reader.GetDateTime(5);
                                string lastMessage = reader["LastMessage"].ToString();
                                int unreadCount = reader.GetInt32(7);
                                DateTime lastMessageDate = reader.GetDateTime(8);

                                var chatInfo = new ChatInfo
                                {
                                    ChatId = chatID,
                                    ChatName = comradeName,
                                    ComradeName = comradeName,
                                    ComradeAvatar = comradeAvatar,
                                    ComradeStatus = comradeStatus,
                                    ComradeLastLogin = comradeLastLogin,
                                    LastMessage = lastMessage,
                                    UnreadCount = unreadCount,
                                    LastMessageDate = lastMessageDate
                                };

                                chatList.Add(chatInfo);
                            }

                            await Dispatcher.InvokeAsync(() =>
                            {
                                var existingButtons = new Dictionary<int, Border>();
                                foreach (var child in Fellows.Children)
                                {
                                    if (child is Border border && border.Tag is int chatId)
                                    {
                                        existingButtons[chatId] = border;
                                    }
                                }

                                foreach (var chat in chatList)
                                {
                                    if (existingButtons.ContainsKey(chat.ChatId))
                                    {
                                        UpdateChatButton(
                                            chat.ChatId,
                                            chat.ComradeName,
                                            chat.LastMessage,
                                            chat.UnreadCount,
                                            chat.ComradeAvatar,
                                            existingButtons[chat.ChatId]);
                                        existingButtons.Remove(chat.ChatId);
                                    }
                                    else
                                    {
                                        AddChatButton(
                                            chat.ChatId,
                                            chat.ComradeName,
                                            chat.LastMessage,
                                            chat.UnreadCount,
                                            chat.ComradeAvatar);
                                    }

                                    _chatInfos[chat.ChatId] = chat;
                                    _unreadCounts[chat.ChatId] = chat.UnreadCount;
                                }

                                foreach (var chatId in existingButtons.Keys)
                                {
                                    Fellows.Children.Remove(existingButtons[chatId]);
                                    _chatButtons.Remove(chatId);
                                    _chatInfos.Remove(chatId);
                                    _unreadCounts.Remove(chatId);
                                }

                                if (chatList.Count == 0)
                                {
                                    Fellows.Children.Clear();
                                    TextBlock noChatsText = new TextBlock
                                    {
                                        Text = $"У пользователя ID {currentUserID} нет чатов\nНайдите пользователя по ID",
                                        TextWrapping = TextWrapping.Wrap,
                                        TextAlignment = TextAlignment.Center,
                                        Margin = new Thickness(10),
                                        Foreground = Brushes.Gray
                                    };
                                    Fellows.Children.Add(noChatsText);
                                }
                            });
                        }
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                Debug.WriteLine($"ОШИБКА в LoadUserChatsAsync: {sqlEx.Message}");
                await ShowDatabaseErrorAsync(sqlEx);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ОШИБКА в LoadUserChatsAsync: {ex.Message}");
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Ошибка загрузки чатов: {ex.Message}");
                });
            }
        }

        private async void OnMessageReceived(int chatId, int userId, string message, int messageId)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                Debug.WriteLine($"Получено сообщение {messageId} в чате {chatId} от пользователя {userId}");

                await LoadUserChatsAsync();

                if (currentChatID == chatId)
                {
                    await AddNewMessageAsync(messageId);

                    if (userId != currentUserID)
                    {
                        await MarkMessagesAsRead();
                    }
                }
            });
        }

        private async void OnFileReceived(int chatId, int userId, string fileName, int messageId)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                Debug.WriteLine($"Получен файл {fileName} в чате {chatId} от пользователя {userId}");

                await LoadUserChatsAsync();

                if (currentChatID == chatId)
                {
                    await AddNewMessageAsync(messageId);

                    if (userId != currentUserID)
                    {
                        await MarkMessagesAsRead();
                    }
                }
            });
        }

        private void UpdateChatButton(int chatId, string comradeName, string lastMessage, int unreadCount, byte[] comradeAvatar, Border existingButton)
        {
            if (existingButton == null) return;

            var container = existingButton.Child as Grid;
            if (container != null)
            {
                StackPanel contentPanel = null;
                foreach (var child in container.Children)
                {
                    if (child is StackPanel panel)
                    {
                        contentPanel = panel;
                        break;
                    }
                    else if (child is Grid innerGrid)
                    {
                        foreach (var innerChild in innerGrid.Children)
                        {
                            if (innerChild is StackPanel innerPanel)
                            {
                                contentPanel = innerPanel;
                                break;
                            }
                        }
                    }
                }

                if (contentPanel != null && contentPanel.Children.Count >= 2)
                {
                    if (contentPanel.Children[0] is TextBlock nameText)
                    {
                        nameText.Text = comradeName;
                    }

                    if (contentPanel.Children[1] is TextBlock lastMessageText)
                    {
                        lastMessageText.Text = lastMessage.Length > 30 ? lastMessage.Substring(0, 30) + "..." : lastMessage;
                    }
                }

                UpdateChatUnreadCount(chatId, unreadCount, existingButton);
            }

            _chatButtons[chatId] = existingButton;
        }

        private void UpdateChatUnreadCount(int chatId, int unreadCount, Border chatButton = null)
        {
            if (chatButton == null && !_chatButtons.TryGetValue(chatId, out chatButton))
            {
                return;
            }

            var container = chatButton.Child as Grid;
            if (container == null)
            {
                var originalContent = chatButton.Child;
                chatButton.Child = null;

                container = new Grid();
                container.Children.Add(originalContent);
                chatButton.Child = container;
            }

            Border existingBadge = null;
            foreach (var child in container.Children)
            {
                if (child is Border border && border.Tag?.ToString() == "unread_badge")
                {
                    existingBadge = border;
                    break;
                }
            }

            if (unreadCount > 0)
            {
                if (existingBadge == null)
                {
                    existingBadge = new Border
                    {
                        Tag = "unread_badge",
                        Background = Brushes.Red,
                        CornerRadius = new CornerRadius(8),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, -5, -5, 0),
                        Padding = new Thickness(4),
                        Child = new TextBlock
                        {
                            Text = unreadCount > 99 ? "99+" : unreadCount.ToString(),
                            Foreground = Brushes.White,
                            FontSize = 9,
                            FontWeight = FontWeights.Bold
                        }
                    };
                    container.Children.Add(existingBadge);
                }
                else
                {
                    var textBlock = existingBadge.Child as TextBlock;
                    if (textBlock != null)
                    {
                        textBlock.Text = unreadCount > 99 ? "99+" : unreadCount.ToString();
                    }
                }
            }
            else if (existingBadge != null)
            {
                container.Children.Remove(existingBadge);
            }
        }

        private async void MessagesScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            _isUserAtBottom = e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 10;

            if (_isUserAtBottom && e.ExtentHeightChange > 0)
            {
                isAutoScrolling = true;
                MessagesScrollViewer.ScrollToBottom();
                isAutoScrolling = false;
            }
        }

        private async Task MarkMessagesAsRead()
        {
            if (currentChatID == 0) return;

            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                using (SqlCommand command = new SqlCommand("MarkMessagesAsRead", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@ChatId", currentChatID);
                    command.Parameters.AddWithValue("@UserId", currentUserID);

                    await connection.OpenAsync();
                    int rowsAffected = await command.ExecuteNonQueryAsync();

                    Debug.WriteLine($"Отмечено как прочитанных: {rowsAffected} сообщений");

                    if (rowsAffected > 0)
                    {
                        await UpdateUnreadCount(currentChatID, false);

                        await LoadUserChatsAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка при отметке сообщений как прочитанных: {ex.Message}");
            }
        }

        private async Task LoadChatMessages(int chatID, bool isOpening = false)
        {
            await _loadLock.WaitAsync();

            try
            {
                if (_isLoadingSequentially) return;

                _isLoadingSequentially = true;
                _lastLoadedMessageId = 0;

                await Dispatcher.InvokeAsync(() => Reports.Children.Clear());

                await Dispatcher.InvokeAsync(() =>
                {
                    string chatName = _chatInfos.GetValueOrDefault(chatID)?.ChatName ?? $"ID: {chatID}";

                    LoadComradeInfo(chatID);
                });

                await LoadMessagesSequentially(chatID, isOpening);

                if (isOpening)
                {
                    isAutoScrolling = true;
                    MessagesScrollViewer.ScrollToBottom();
                    isAutoScrolling = false;
                    await MarkMessagesAsRead();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка загрузки сообщений: {ex.Message}");
            }
            finally
            {
                _isLoadingSequentially = false;
                _loadLock.Release();
            }
        }

        private async Task<List<CachedMessage>> GetLastMessages(int chatId, int count)
        {
            var messages = new List<CachedMessage>();

            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    using (SqlCommand command = new SqlCommand("GetChatMessages", connection))
                    {
                        command.CommandType = CommandType.StoredProcedure;
                        command.Parameters.AddWithValue("@ChatId", chatId);

                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            int loadedCount = 0;
                            while (await reader.ReadAsync() && loadedCount < count)
                            {
                                var message = new CachedMessage
                                {
                                    MessageId = reader.GetInt32(0),
                                    ChatId = reader.GetInt32(1),
                                    UserId = reader.GetInt32(2),
                                    UserName = reader.GetString(3),
                                    MessageText = reader.GetString(4),
                                    SendDate = reader.GetDateTime(5),
                                    IsRead = reader.GetBoolean(6)
                                };

                                messages.Add(message);
                                loadedCount++;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка загрузки последних сообщений: {ex.Message}");
            }

            return messages;
        }

        private async Task LoadMessagesSequentially(int chatID, bool isOpening)
        {
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (Reports.Children.Count > 0)
                        Reports.Children.Clear();
                });

                var messages = await GetLastMessages(chatID, 200);

                if (messages.Count == 0)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        Reports.Children.Add(new TextBlock
                        {
                            Text = "Нет сообщений",
                            Foreground = Brushes.Gray,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Margin = new Thickness(10)
                        });
                    });
                    return;
                }

                var messageIds = messages.Select(m => m.MessageId).ToList();
                var allFiles = await GetFilesForMessageIds(messageIds);

                foreach (var message in messages)
                {
                    if (allFiles.ContainsKey(message.MessageId))
                    {
                        message.Files = allFiles[message.MessageId];
                    }
                }

                var sortedMessages = messages.OrderByDescending(m => m.MessageId).ToList();

                _lastLoadedMessageId = sortedMessages.Last().MessageId;

                for (int i = sortedMessages.Count - 1; i >= 0; i--)
                {
                    var message = sortedMessages[i];
                    await DisplaySingleMessage(message);

                    if (isOpening)
                        await Task.Delay(10);
                }

                if (!_chatMessages.ContainsKey(chatID))
                    _chatMessages[chatID] = new List<CachedMessage>();

                _chatMessages[chatID].AddRange(sortedMessages);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка последовательной загрузки: {ex.Message}");
            }
        }

        private async Task<Dictionary<int, List<CachedFile>>> GetFilesForMessageIds(List<int> messageIds)
        {
            var result = new Dictionary<int, List<CachedFile>>();

            if (messageIds.Count == 0) return result;

            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    string idList = string.Join(",", messageIds);

                    string query = $@"
                        SELECT 
                            MessageId,
                            FileId,
                            FileName,
                            FileType,
                            FileData,
                            FileSize
                        FROM MessageFiles 
                        WHERE MessageId IN ({idList})
                        ORDER BY MessageId";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            int messageId = reader.GetInt32(0);

                            var file = new CachedFile
                            {
                                FileId = reader.GetInt32(1),
                                FileName = reader.GetString(2),
                                FileType = reader.GetString(3),
                                FileData = (byte[])reader.GetValue(4),
                                FileSize = reader.GetInt32(5)
                            };

                            string fileHash = CalculateFileHash(file.FileData);
                            file.FileHash = fileHash;

                            if (_fileCacheStatus.ContainsKey(file.FileId))
                            {
                                var cachedFile = _fileCacheStatus[file.FileId];
                                file.CacheStatus = cachedFile.CacheStatus;
                                file.CachedFilePath = cachedFile.CachedFilePath;
                            }
                            else
                            {
                                var cachedInfo = _cacheManager.GetCachedFile(fileHash);
                                if (cachedInfo != null && File.Exists(cachedInfo.FilePath))
                                {
                                    file.CacheStatus = FileCacheStatus.Cached;
                                    file.CachedFilePath = cachedInfo.FilePath;
                                }
                            }

                            _fileCacheStatus[file.FileId] = file;

                            if (!result.ContainsKey(messageId))
                                result[messageId] = new List<CachedFile>();

                            result[messageId].Add(file);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка загрузки файлов для ID сообщений: {ex.Message}");
            }

            return result;
        }

        private async Task DisplaySingleMessage(CachedMessage message)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                CreateMessageUI(
                    message.MessageId,
                    message.MessageText,
                    message.SendDate,
                    message.UserName,
                    message.UserId,
                    message.Files
                );
            });
        }

        private async Task<List<CachedFile>> GetMessageFilesFromDatabase(int messageId)
        {
            var files = await GetFilesForMessageIds(new List<int> { messageId });
            return files.ContainsKey(messageId) ? files[messageId] : new List<CachedFile>();
        }

        private async Task AddNewMessageAsync(int messageId)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                using (SqlCommand command = new SqlCommand(@"
                    SELECT m.MessageId, m.ChatId, m.UserId, u.UserName, 
                           m.MessageText, m.SendDate, m.IsRead
                    FROM Messages m
                    INNER JOIN Users u ON m.UserId = u.UserId
                    WHERE m.MessageId = @MessageId", connection))
                {
                    command.Parameters.AddWithValue("@MessageId", messageId);
                    await connection.OpenAsync();

                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            var message = new CachedMessage
                            {
                                MessageId = reader.GetInt32(0),
                                ChatId = reader.GetInt32(1),
                                UserId = reader.GetInt32(2),
                                UserName = reader.GetString(3),
                                MessageText = reader.GetString(4),
                                SendDate = reader.GetDateTime(5),
                                IsRead = reader.GetBoolean(6)
                            };

                            message.Files = await GetMessageFilesFromDatabase(message.MessageId);

                            await Dispatcher.InvokeAsync(() =>
                            {
                                CreateMessageUI(
                                    message.MessageId,
                                    message.MessageText,
                                    message.SendDate,
                                    message.UserName,
                                    message.UserId,
                                    message.Files
                                );

                                if (message.UserId == currentUserID || _isUserAtBottom)
                                {
                                    isAutoScrolling = true;
                                    MessagesScrollViewer.ScrollToBottom();
                                    isAutoScrolling = false;
                                }

                                LoadUserChatsAsync();
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка добавления сообщения: {ex.Message}");
            }
        }

        private void CreateMessageUI(int messageId, string messageText, DateTime createdDate, string userNick, int messageAuthorID, List<CachedFile> files)
        {
            try
            {
                foreach (var child in Reports.Children)
                {
                    if (child is StackPanel panel && panel.Tag is int existingId && existingId == messageId)
                    {
                        return;
                    }
                }

                StackPanel messagePanel = new StackPanel
                {
                    Margin = new Thickness(5),
                    HorizontalAlignment = messageAuthorID == currentUserID ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                    MaxWidth = 400,
                    Tag = messageId
                };

                Border messageContainer = new Border
                {
                    Background = messageAuthorID == currentUserID ? Brushes.LightGoldenrodYellow : Brushes.Cornsilk,
                    Padding = new Thickness(10),
                    CornerRadius = new CornerRadius(10),
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1)
                };

                StackPanel contentPanel = new StackPanel();

                TextBlock headerText = new TextBlock
                {
                    Text = $"{userNick} ({createdDate:HH:mm})",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 5)
                };
                contentPanel.Children.Add(headerText);

                if (!string.IsNullOrEmpty(messageText) && messageText != "[Файл]")
                {
                    TextBlock messageTextBlock = new TextBlock
                    {
                        Text = messageText,
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, files.Count > 0 ? 5 : 0)
                    };
                    contentPanel.Children.Add(messageTextBlock);
                }

                foreach (var file in files)
                {
                    var fileControl = CreateFileControl(file);
                    contentPanel.Children.Add(fileControl);
                }

                messageContainer.Child = contentPanel;
                messagePanel.Children.Add(messageContainer);
                Reports.Children.Add(messagePanel);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка создания UI сообщения: {ex.Message}");
            }
        }

        private UIElement CreateFileControl(CachedFile file)
        {
            Border fileBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = Brushes.DarkGray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 2, 0, 2),
                Cursor = Cursors.Hand,
                Tag = file
            };

            StackPanel filePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            if (file.CacheStatus == FileCacheStatus.Cached && !string.IsNullOrEmpty(file.CachedFilePath))
            {
                TextBlock cachedIcon = new TextBlock
                {
                    Text = "✅ ",
                    FontSize = 16,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                filePanel.Children.Add(cachedIcon);

                fileBorder.MouseLeftButtonDown += (s, e) =>
                {
                    OpenCachedFile(file.CachedFilePath);
                };
            }
            else if (file.CacheStatus == FileCacheStatus.Downloading)
            {
                TextBlock downloadingIcon = new TextBlock
                {
                    Text = "⏳ ",
                    FontSize = 16,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                filePanel.Children.Add(downloadingIcon);

                fileBorder.Opacity = 0.7;
                fileBorder.Cursor = Cursors.Wait;
            }
            else
            {
                TextBlock downloadIcon = new TextBlock
                {
                    Text = "⬇️ ",
                    FontSize = 16,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                filePanel.Children.Add(downloadIcon);

                fileBorder.MouseLeftButtonDown += async (s, e) =>
                {
                    await DownloadAndCacheFile(file);
                };
            }

            StackPanel infoPanel = new StackPanel();

            TextBlock fileNameText = new TextBlock
            {
                Text = file.FileName,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };

            TextBlock fileSizeText = new TextBlock
            {
                Text = FormatFileSize(file.FileSize),
                FontSize = 9,
                Foreground = Brushes.Gray
            };

            infoPanel.Children.Add(fileNameText);
            infoPanel.Children.Add(fileSizeText);

            filePanel.Children.Add(infoPanel);
            fileBorder.Child = filePanel;

            return fileBorder;
        }

        private string GetFileIcon(string fileType)
        {
            return fileType switch
            {
                "image" => "🖼️",
                "text" => "📄",
                _ => "📎"
            };
        }

        private string FormatFileSize(int fileSize)
        {
            if (fileSize < 1024)
                return $"{fileSize} B";
            else if (fileSize < 1024 * 1024)
                return $"{fileSize / 1024} KB";
            else
                return $"{(double)fileSize / (1024 * 1024):F1} MB";
        }

        private string CalculateFileHash(byte[] fileData)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(fileData);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private async Task DownloadAndCacheFile(CachedFile file)
        {
            try
            {
                file.CacheStatus = FileCacheStatus.Downloading;

                await UpdateFileControlUI(file);

                string fileHash = file.FileHash;
                await _cacheManager.CacheFileAsync(fileHash, file.FileName, file.FileType, file.FileData);

                var cachedInfo = _cacheManager.GetCachedFile(fileHash);
                if (cachedInfo != null)
                {
                    file.CacheStatus = FileCacheStatus.Cached;
                    file.CachedFilePath = cachedInfo.FilePath;

                    _fileCacheStatus[file.FileId] = file;

                    await UpdateFileControlUI(file);

                    OpenCachedFile(cachedInfo.FilePath);
                }
            }
            catch (Exception ex)
            {
                file.CacheStatus = FileCacheStatus.Error;
                await UpdateFileControlUI(file);
                MessageBox.Show($"Ошибка при скачивании файла: {ex.Message}");
            }
        }

        private async Task UpdateFileControlUI(CachedFile file)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                var fileControl = FindFileControl(file.FileId);
                if (fileControl != null)
                {
                    var parent = VisualTreeHelper.GetParent(fileControl) as Panel;
                    if (parent != null)
                    {
                        int index = parent.Children.IndexOf(fileControl);
                        parent.Children.RemoveAt(index);
                        parent.Children.Insert(index, CreateFileControl(file));
                    }
                }
            });
        }

        private UIElement FindFileControl(int fileId)
        {
            foreach (var child in Reports.Children)
            {
                if (child is StackPanel messagePanel)
                {
                    var fileControl = FindFileControlInChildren(messagePanel, fileId);
                    if (fileControl != null)
                        return fileControl;
                }
            }
            return null;
        }

        private UIElement FindFileControlInChildren(DependencyObject parent, int fileId)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Border border && border.Tag is CachedFile file && file.FileId == fileId)
                    return border;

                var result = FindFileControlInChildren(child, fileId);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void OpenCachedFile(string filePath)
        {
            try
            {
                var result = MessageBox.Show(
                    $"Открыть файл?\n{IOPath.GetFileName(filePath)}",
                    "Открытие файла",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при открытии файла: {ex.Message}");
            }
        }

        private async void SendMessage()
        {
            if (!_sendLock.WaitAsync(0).Result)
            {
                Debug.WriteLine("Предотвращена повторная отправка сообщения");
                return;
            }

            try
            {
                if (currentChatID == 0)
                {
                    MessageBox.Show("Выберите чат для отправки сообщения");
                    Text_for_Comrade.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(Text_for_Comrade.Text))
                {
                    MessageBox.Show("Введите сообщение");
                    Text_for_Comrade.Focus();
                    return;
                }

                string messageText = Text_for_Comrade.Text;

                int newMessageId = 0;
                using (SqlConnection connection = new SqlConnection(connectionString))
                using (SqlCommand command = new SqlCommand("SendMessage", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@ChatId", currentChatID);
                    command.Parameters.AddWithValue("@UserId", currentUserID);
                    command.Parameters.AddWithValue("@MessageText", messageText);

                    SqlParameter outputParam = new SqlParameter("@NewMessageId", SqlDbType.Int)
                    {
                        Direction = ParameterDirection.Output
                    };
                    command.Parameters.Add(outputParam);

                    await connection.OpenAsync();
                    await command.ExecuteNonQueryAsync();

                    if (outputParam.Value != DBNull.Value)
                    {
                        newMessageId = (int)outputParam.Value;
                    }
                }

                await AddMessageToUI(newMessageId, messageText, true);

                await _signalRManager.SendMessageAsync("SendMessage", currentChatID, currentUserID, messageText, newMessageId);

                await LoadUserChatsAsync();

                await Dispatcher.InvokeAsync(() =>
                {
                    Text_for_Comrade.Text = "";
                    Text_for_Comrade.Focus();
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка отправки сообщения: {ex.Message}");
                Text_for_Comrade.Focus();
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private void AddChatButton(int chatID, string comradeName, string lastMessage, int unreadCount, byte[] comradeAvatar)
        {
            Border chatBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(5, 5, 5, 5),
                CornerRadius = new CornerRadius(5),
                Tag = chatID,
                Height = 60,
                Cursor = Cursors.Hand
            };

            Grid containerGrid = new Grid();

            StackPanel contentPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(5)
            };

            Border avatarBorder = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            try
            {
                if (comradeAvatar != null && comradeAvatar.Length > 0)
                {
                    using (MemoryStream stream = new MemoryStream(comradeAvatar))
                    {
                        BitmapImage bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = stream;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();

                        Image avatarImage = new Image
                        {
                            Source = bitmap,
                            Stretch = Stretch.UniformToFill
                        };

                        EllipseGeometry ellipse = new EllipseGeometry(new Point(20, 20), 20, 20);
                        avatarImage.Clip = ellipse;

                        avatarBorder.Child = avatarImage;
                    }
                }
                else
                {
                    TextBlock placeholder = new TextBlock
                    {
                        Text = "👤",
                        FontSize = 20,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    avatarBorder.Child = placeholder;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка загрузки мини-аватара: {ex.Message}");
                TextBlock placeholder = new TextBlock
                {
                    Text = "👤",
                    FontSize = 20,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                avatarBorder.Child = placeholder;
            }

            contentPanel.Children.Add(avatarBorder);

            StackPanel textPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock nameText = new TextBlock
            {
                Text = comradeName,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Black
            };

            TextBlock lastMessageText = new TextBlock
            {
                Text = lastMessage.Length > 25 ? lastMessage.Substring(0, 25) + "..." : lastMessage,
                FontSize = 10,
                Foreground = Brushes.Black,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            };

            textPanel.Children.Add(nameText);
            textPanel.Children.Add(lastMessageText);
            contentPanel.Children.Add(textPanel);

            containerGrid.Children.Add(contentPanel);

            if (unreadCount > 0)
            {
                Border badge = new Border
                {
                    Background = Brushes.Red,
                    CornerRadius = new CornerRadius(8),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, -5, -5, 0),
                    Padding = new Thickness(4),
                    Tag = "unread_badge",
                    Child = new TextBlock
                    {
                        Text = unreadCount > 99 ? "99+" : unreadCount.ToString(),
                        Foreground = Brushes.White,
                        FontSize = 9,
                        FontWeight = FontWeights.Bold
                    }
                };
                containerGrid.Children.Add(badge);
            }

            chatBorder.Child = containerGrid;

            _chatButtons[chatID] = chatBorder;

            chatBorder.MouseLeftButtonDown += async (s, e) =>
            {
                if (!_isInitialized)
                {
                    MessageBox.Show("Страница еще не инициализирована. Подождите...");
                    return;
                }

                if (currentChatID != 0 && _signalRManager != null)
                {
                    await _signalRManager.LeaveChatGroupAsync(currentChatID);
                }

                currentChatID = chatID;

                if (_signalRManager != null)
                {
                    await _signalRManager.JoinChatGroupAsync(chatID);
                }

                LoadComradeInfo(chatID);

                await LoadChatMessages(chatID, true);

                await UpdateUnreadCount(chatID, false);

                await MarkMessagesAsRead();
            };

            chatBorder.MouseEnter += (s, e) =>
            {
                chatBorder.Background = Brushes.Goldenrod;
            };

            chatBorder.MouseLeave += (s, e) =>
            {
                chatBorder.Background = Brushes.White;
            };

            Fellows.Children.Add(chatBorder);
        }

        private void Text_for_Comrade_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (!Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
                {
                    SendMessage();
                    e.Handled = true;
                }
            }
        }

        private void Enter_to_search_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
            {
                MessageBox.Show("Страница еще не инициализирована. Подождите...");
                return;
            }

            if (string.IsNullOrWhiteSpace(ID_search.Text))
            {
                MessageBox.Show("Введите ID пользователя");
                return;
            }

            if (!int.TryParse(ID_search.Text, out int targetUserID))
            {
                MessageBox.Show("ID должен быть числом");
                return;
            }

            if (targetUserID == currentUserID)
            {
                MessageBox.Show("Нельзя создать чат с самим собой");
                return;
            }

            _ = CreateChatWithUser(targetUserID);
        }

        private async Task CreateChatWithUser(int targetUserID)
        {
            try
            {
                string userName = "";
                using (SqlConnection connection = new SqlConnection(connectionString))
                using (SqlCommand command = new SqlCommand("SELECT UserName FROM Users WHERE UserId = @UserID", connection))
                {
                    command.Parameters.AddWithValue("@UserID", targetUserID);
                    await connection.OpenAsync();
                    var result = await command.ExecuteScalarAsync();

                    if (result == null)
                    {
                        MessageBox.Show("Пользователь не найден");
                        return;
                    }
                    userName = result.ToString();
                }

                using (SqlConnection connection = new SqlConnection(connectionString))
                using (SqlCommand command = new SqlCommand("CreatePrivateChat", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@CurrentUserID", currentUserID);
                    command.Parameters.AddWithValue("@TargetUserID", targetUserID);

                    await connection.OpenAsync();
                    var result = await command.ExecuteScalarAsync();

                    if (result == null || result == DBNull.Value)
                    {
                        MessageBox.Show("Не удалось создать чат");
                        return;
                    }

                    int newChatID = Convert.ToInt32(result);

                    await Dispatcher.InvokeAsync(() =>
                    {
                        AddChatButton(newChatID, userName, "Новое сообщение", 0, null);
                        ID_search.Text = "";
                        MessageBox.Show($"Приватный чат с {userName} создан!");

                        LoadUserChatsAsync();
                    });
                }
            }
            catch (SqlException sqlEx)
            {
                await ShowDatabaseErrorAsync(sqlEx);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка создания чата: {ex.Message}");
            }
        }

        private async Task AddMessageToUI(int messageId, string messageText, bool isMyMessage)
        {
            try
            {
                var tempMessage = new CachedMessage
                {
                    MessageId = messageId,
                    ChatId = currentChatID,
                    UserId = currentUserID,
                    UserName = isMyMessage ? "Вы" : "Собеседник",
                    MessageText = messageText,
                    SendDate = DateTime.Now,
                    IsRead = true
                };

                await DisplaySingleMessage(tempMessage);

                if (isMyMessage || _isUserAtBottom)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        isAutoScrolling = true;
                        MessagesScrollViewer.ScrollToBottom();
                        isAutoScrolling = false;
                    });
                }

                if (!_chatMessages.ContainsKey(currentChatID))
                    _chatMessages[currentChatID] = new List<CachedMessage>();

                _chatMessages[currentChatID].Add(tempMessage);

                LoadUserChatsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка добавления сообщения в UI: {ex.Message}");
            }
        }

        private async Task AddMessageWithFileToUI(int messageId, string messageText, string fileName)
        {
            try
            {
                var fileData = await GetFileData(messageId, fileName);

                if (fileData != null)
                {
                    var tempMessage = new CachedMessage
                    {
                        MessageId = messageId,
                        ChatId = currentChatID,
                        UserId = currentUserID,
                        UserName = "Вы",
                        MessageText = messageText,
                        SendDate = DateTime.Now,
                        IsRead = true,
                        Files = new List<CachedFile>
                        {
                            new CachedFile
                            {
                                FileId = messageId,
                                FileName = fileName,
                                FileType = GetFileTypeByExtension(fileName),
                                FileData = fileData,
                                FileSize = fileData.Length
                            }
                        }
                    };

                    await DisplaySingleMessage(tempMessage);

                    await Dispatcher.InvokeAsync(() =>
                    {
                        isAutoScrolling = true;
                        MessagesScrollViewer.ScrollToBottom();
                        isAutoScrolling = false;
                    });

                    if (!_chatMessages.ContainsKey(currentChatID))
                        _chatMessages[currentChatID] = new List<CachedMessage>();

                    _chatMessages[currentChatID].Add(tempMessage);

                    LoadUserChatsAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка добавления сообщения с файлом в UI: {ex.Message}");
            }
        }

        private async Task<byte[]> GetFileData(int messageId, string fileName)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                using (SqlCommand command = new SqlCommand(@"
            SELECT FileData FROM MessageFiles 
            WHERE MessageId = @MessageId AND FileName = @FileName", connection))
                {
                    command.Parameters.AddWithValue("@MessageId", messageId);
                    command.Parameters.AddWithValue("@FileName", fileName);

                    await connection.OpenAsync();
                    var result = await command.ExecuteScalarAsync();

                    return result != DBNull.Value ? (byte[])result : null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка получения данных файла: {ex.Message}");
                return null;
            }
        }

        private string GetFileTypeByExtension(string fileName)
        {
            string extension = IOPath.GetExtension(fileName).ToLower();

            return extension switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" => "image",
                ".txt" or ".doc" or ".docx" or ".pdf" => "text",
                _ => "other"
            };
        }

        private async Task<bool> SendMessageWithFile(string messageText, string fileName, string fileType, byte[] fileData, int fileSize)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    int newMessageId = 0;
                    using (SqlCommand command = new SqlCommand("SendMessage", connection))
                    {
                        command.CommandType = CommandType.StoredProcedure;
                        command.Parameters.AddWithValue("@ChatId", currentChatID);
                        command.Parameters.AddWithValue("@UserId", currentUserID);
                        command.Parameters.AddWithValue("@MessageText", messageText);

                        SqlParameter outputParam = new SqlParameter("@NewMessageId", SqlDbType.Int)
                        {
                            Direction = ParameterDirection.Output
                        };
                        command.Parameters.Add(outputParam);

                        await command.ExecuteNonQueryAsync();

                        if (outputParam.Value != DBNull.Value)
                        {
                            newMessageId = (int)outputParam.Value;
                        }
                    }

                    if (newMessageId > 0)
                    {
                        using (SqlCommand fileCommand = new SqlCommand("SaveMessageFile", connection))
                        {
                            fileCommand.CommandType = CommandType.StoredProcedure;
                            fileCommand.Parameters.AddWithValue("@MessageId", newMessageId);
                            fileCommand.Parameters.AddWithValue("@FileName", fileName);
                            fileCommand.Parameters.AddWithValue("@FileType", fileType);
                            fileCommand.Parameters.AddWithValue("@FileData", fileData);
                            fileCommand.Parameters.AddWithValue("@FileSize", fileSize);

                            await fileCommand.ExecuteNonQueryAsync();

                            await AddMessageWithFileToUI(newMessageId, messageText, fileName);

                            await _signalRManager.SendMessageAsync("SendFile", currentChatID, currentUserID, fileName, newMessageId);

                            LoadUserChatsAsync();

                            return true;
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка при отправке файла: {ex.Message}");
                return false;
            }
        }

        private async Task UpdateUnreadCount(int chatId, bool increment = false)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    if (increment)
                    {
                        using (SqlCommand command = new SqlCommand(@"
                    UPDATE UserChats 
                    SET UnreadCount = ISNULL(UnreadCount, 0) + 1 
                    WHERE ChatId = @ChatId AND UserId != @UserId", connection))
                        {
                            command.Parameters.AddWithValue("@ChatId", chatId);
                            command.Parameters.AddWithValue("@UserId", currentUserID);
                            await command.ExecuteNonQueryAsync();
                        }
                    }
                    else
                    {
                        using (SqlCommand command = new SqlCommand(@"
                    UPDATE UserChats 
                    SET UnreadCount = 0 
                    WHERE ChatId = @ChatId AND UserId = @UserId", connection))
                        {
                            command.Parameters.AddWithValue("@ChatId", chatId);
                            command.Parameters.AddWithValue("@UserId", currentUserID);
                            await command.ExecuteNonQueryAsync();
                        }
                    }

                    using (SqlCommand command = new SqlCommand(@"
                SELECT UnreadCount FROM UserChats 
                WHERE ChatId = @ChatId AND UserId = @UserId", connection))
                    {
                        command.Parameters.AddWithValue("@ChatId", chatId);
                        command.Parameters.AddWithValue("@UserId", currentUserID);

                        var result = await command.ExecuteScalarAsync();
                        int unreadCount = result != DBNull.Value ? Convert.ToInt32(result) : 0;

                        _unreadCounts[chatId] = unreadCount;

                        UpdateChatUnreadCount(chatId, unreadCount);

                        if (_chatInfos.ContainsKey(chatId))
                        {
                            _chatInfos[chatId].UnreadCount = unreadCount;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка обновления счетчика: {ex.Message}");
            }
        }

        private async Task LoadComradeInfo(int chatId)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                        SELECT 
                            u.UserName,
                            ISNULL(u.MiniAvatarImage, u.ProfileImage) AS Avatar,
                            u.Status,
                            u.LastLoginDate
                        FROM UserChats uc
                        INNER JOIN Users u ON uc.UserId = u.UserId
                        WHERE uc.ChatId = @ChatId AND uc.UserId != @CurrentUserId";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@ChatId", chatId);
                        command.Parameters.AddWithValue("@CurrentUserId", currentUserID);

                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                string comradeName = reader["UserName"].ToString();
                                byte[] comradeAvatar = reader.IsDBNull(reader.GetOrdinal("Avatar"))
                                    ? null
                                    : (byte[])reader["Avatar"];
                                string comradeStatus = reader.IsDBNull(reader.GetOrdinal("Status"))
                                    ? "offline"
                                    : reader["Status"].ToString();
                                DateTime? comradeLastLogin = reader.IsDBNull(reader.GetOrdinal("LastLoginDate"))
                                    ? (DateTime?)null
                                    : reader.GetDateTime(reader.GetOrdinal("LastLoginDate"));

                                await Dispatcher.InvokeAsync(() =>
                                {
                                    UpdateComradeInformation(comradeName, comradeAvatar, comradeStatus, comradeLastLogin);
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка загрузки информации о собеседнике: {ex.Message}");
            }
        }

        private void UpdateComradeInformation(string comradeName, byte[] comradeAvatar, string comradeStatus, DateTime? comradeLastLogin)
        {
            Grid infoGrid = new Grid();
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            infoGrid.Margin = new Thickness(5);

            Border avatarBorder = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(5)
            };

            try
            {
                if (comradeAvatar != null && comradeAvatar.Length > 0)
                {
                    using (MemoryStream stream = new MemoryStream(comradeAvatar))
                    {
                        BitmapImage bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = stream;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();

                        Image avatarImage = new Image
                        {
                            Source = bitmap,
                            Stretch = Stretch.UniformToFill
                        };

                        EllipseGeometry ellipse = new EllipseGeometry(new Point(20, 20), 20, 20);
                        avatarImage.Clip = ellipse;

                        avatarBorder.Child = avatarImage;
                    }
                }
                else
                {
                    TextBlock placeholder = new TextBlock
                    {
                        Text = "👤",
                        FontSize = 24,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    avatarBorder.Child = placeholder;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка загрузки аватара: {ex.Message}");
                TextBlock placeholder = new TextBlock
                {
                    Text = "👤",
                    FontSize = 24,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                avatarBorder.Child = placeholder;
            }

            Grid.SetColumn(avatarBorder, 0);
            infoGrid.Children.Add(avatarBorder);

            StackPanel textPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock nameText = new TextBlock
            {
                Text = comradeName,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black
            };

            StackPanel statusPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 5, 0, 0)
            };

            TextBlock statusText = new TextBlock
            {
                Text = GetStatusText(comradeStatus),
                FontSize = 12,
                Foreground = GetStatusColor(comradeStatus),
                Margin = new Thickness(0, 0, 10, 0)
            };

            TextBlock lastLoginText = new TextBlock
            {
                Text = GetLastLoginText(comradeLastLogin),
                FontSize = 12,
                Foreground = Brushes.Gray
            };

            statusPanel.Children.Add(statusText);
            statusPanel.Children.Add(lastLoginText);

            textPanel.Children.Add(nameText);
            textPanel.Children.Add(statusPanel);

            Grid.SetColumn(textPanel, 1);
            infoGrid.Children.Add(textPanel);

            Comrade_information.Content = infoGrid;
        }

        private string GetStatusText(string status)
        {
            return status?.ToLower() switch
            {
                "online" => "✅ В сети",
                "dnd" => "⛔ Не беспокоить",
                "away" => "⌛ Отошел",
                "offline" => "⚫ Не в сети",
                _ => "⚫ Не в сети"
            };
        }

        private System.Windows.Media.Brush GetStatusColor(string status)
        {
            return status?.ToLower() switch
            {
                "online" => System.Windows.Media.Brushes.Green,
                "dnd" => System.Windows.Media.Brushes.Red,
                "away" => System.Windows.Media.Brushes.Orange,
                "offline" => System.Windows.Media.Brushes.Gray,
                _ => System.Windows.Media.Brushes.Gray
            };
        }

        private string GetLastLoginText(DateTime? lastLogin)
        {
            if (!lastLogin.HasValue || lastLogin.Value == DateTime.MinValue)
                return "Никогда не заходил(а)";

            TimeSpan timeSinceLogin = DateTime.Now - lastLogin.Value;

            if (timeSinceLogin.TotalMinutes < 1)
                return "был(а) только что";
            else if (timeSinceLogin.TotalHours < 1)
                return $"был(а) {(int)timeSinceLogin.TotalMinutes} мин назад";
            else if (timeSinceLogin.TotalDays < 1)
                return $"был(а) {(int)timeSinceLogin.TotalHours} ч назад";
            else if (timeSinceLogin.TotalDays < 7)
                return $"был(а) {(int)timeSinceLogin.TotalDays} дн назад";
            else
                return $"был(а) {(int)(timeSinceLogin.TotalDays / 7)} нед назад";
        }

        private void Enter_to_search_MouseEnter(object sender, MouseEventArgs e) => AnimateButtonScale("Enter_to_search", 1.1);
        private void Enter_to_search_MouseLeave(object sender, MouseEventArgs e) => AnimateButtonScale("Enter_to_search", 1.0);
        private void Enter_to_Options_MouseEnter(object sender, MouseEventArgs e) => AnimateButtonScale("Enter_to_Options", 1.1);
        private void Enter_to_Options_MouseLeave(object sender, MouseEventArgs e) => AnimateButtonScale("Enter_to_Options", 1.0);
        private void Enter_text_MouseEnter(object sender, MouseEventArgs e) => AnimateButtonScale("Enter_Text", 1.1);
        private void Enter_text_MouseLeave(object sender, MouseEventArgs e) => AnimateButtonScale("Enter_Text", 1.0);

        private void AnimateButtonScale(string buttonName, double scale)
        {
            Button button = null;
            switch (buttonName)
            {
                case "Enter_Text": button = Enter_Text; break;
                case "Enter_to_search": button = Enter_to_search; break;
                case "Enter_to_Options": button = Enter_to_Options; break;
            }

            if (button != null)
            {
                var template = button.Template;
                Rectangle oval = null;
                switch (buttonName)
                {
                    case "Enter_Text": oval = template.FindName("Oval_Text", button) as Rectangle; break;
                    case "Enter_to_search": oval = template.FindName("Oval_ID_search", button) as Rectangle; break;
                    case "Enter_to_Options": oval = template.FindName("Oval_Options", button) as Rectangle; break;
                }

                if (oval != null)
                {
                    var animation = new DoubleAnimation(scale, TimeSpan.FromMilliseconds(200));
                    oval.RenderTransform = new ScaleTransform();
                    oval.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
                    oval.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
                }
            }
        }

        private async void FileText_Click(object sender, RoutedEventArgs e)
        {
            if (currentChatID == 0)
            {
                MessageBox.Show("Выберите чат для отправки файла");
                return;
            }

            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Title = "Выберите файл для отправки",
                Filter = "Все файлы (*.*)|*.*|Изображения (*.jpg;*.png;*.gif;*.bmp)|*.jpg;*.png;*.gif;*.bmp|Текстовые файлы (*.txt)|*.txt",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                string fileName = IOPath.GetFileName(filePath);

                FileInfo fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > 10 * 1024 * 1024)
                {
                    MessageBox.Show("Файл слишком большой. Максимальный размер: 10MB");
                    return;
                }

                MessageBox.Show($"Начинается отправка файла: {fileName}");
                await SendFileAsync(filePath, fileName);
            }
        }

        private async Task SendFileAsync(string filePath, string fileName)
        {
            try
            {
                byte[] fileData = await File.ReadAllBytesAsync(filePath);
                string fileType = GetFileType(filePath);
                string displayText = $"[Файл: {fileName}]";

                bool success = await SendMessageWithFile(displayText, fileName, fileType, fileData, fileData.Length);

                if (success)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show($"Файл '{fileName}' успешно отправлен!");
                    });
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Ошибка при отправке файла: {ex.Message}");
                });
            }
        }

        private string GetFileType(string filePath)
        {
            string extension = IOPath.GetExtension(filePath).ToLower();
            return extension switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" => "image",
                ".txt" or ".doc" or ".docx" or ".pdf" => "text",
                _ => "other"
            };
        }

        private void FileText_MouseEnter(object sender, MouseEventArgs e) { }
        private void FileText_MouseLeave(object sender, MouseEventArgs e) { }

        private async void Enter_to_Options_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Переход в настройки");

            int userId = currentUserID;

            OptionsPage optionsPage = new OptionsPage(userId);
            this.NavigationService.Navigate(optionsPage);

            Debug.WriteLine("Навигация на OptionsPage выполнена");
        }

        private void Enter_Text_Click(object sender, RoutedEventArgs e)
        {
            if (!Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
            {
                SendMessage();
            }
            else
            {
                Text_for_Comrade.Text += Environment.NewLine;
                Text_for_Comrade.CaretIndex = Text_for_Comrade.Text.Length;
                Text_for_Comrade.Focus();
            }
        }

        private void Text_for_Comrade_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
        }
    }
}