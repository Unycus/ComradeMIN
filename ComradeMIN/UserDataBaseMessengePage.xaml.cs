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
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using IOPath = System.IO.Path;

namespace ComradeMIN
{
    public partial class UserDataBaseMessengePage : Page
    {
        private string connectionString = "Server=tcp:26.19.50.66\\SQLEXPRESS,1433;" +
                                          "Database=Void;" +
                                          "User Id=VoidUser;" +
                                          "Password=VoidUser123;" +
                                          "Connection Timeout=30;" +
                                          "TrustServerCertificate=True;";
        private int currentUserID;
        private int currentChatID = 0;
        private bool isAutoScrolling = true;
        private bool _isUserAtBottom = true;
        private bool _isLoadingSequentially = false;
        private int _lastLoadedMessageId = 0;
        private SignalRManager _signalRManager;
        private string _signalRUrl = "http://26.19.50.66:5000/chatHub";

        // Кэш-менеджер
        private CacheManager _cacheManager;

        // Хранилище сообщений
        private Dictionary<int, List<CachedMessage>> _chatMessages = new();
        private Dictionary<int, int> _unreadCounts = new();
        private Dictionary<int, Border> _chatButtons = new();
        private Dictionary<int, ChatInfo> _chatInfos = new();

        private class ChatInfo
        {
            public int ChatId { get; set; }
            public string ChatName { get; set; }
            public string LastMessage { get; set; }
            public int UnreadCount { get; set; }
            public DateTime LastMessageDate { get; set; }
        }

        public UserDataBaseMessengePage(int userID)
        {
            InitializeComponent();
            currentUserID = userID;

            // Инициализация кэш-менеджера
            _cacheManager = new CacheManager(currentUserID);

            // Инициализация SignalRManager (упрощенная)
            _signalRManager = new SignalRManager(_signalRUrl, currentUserID);
            _signalRManager.OnMessageReceived += OnMessageReceived;
            _signalRManager.OnFileReceived += OnFileReceived;

            Debug.WriteLine($"Создана страница чатов для пользователя ID: {currentUserID}");

            if (Fellows == null || Reports == null)
            {
                MessageBox.Show("Ошибка загрузки элементов интерфейса");
                return;
            }

            MessagesScrollViewer.ScrollChanged += MessagesScrollViewer_ScrollChanged;
            LoadUserChats();
            Text_for_Comrade.KeyDown += Text_for_Comrade_KeyDown;

            this.Loaded += async (s, e) =>
            {
                Text_for_Comrade.Focus();
                await _signalRManager.ConnectAsync();
            };

            this.Unloaded += async (s, e) => await CleanupSignalR();
        }

        private async void OnMessageReceived(int chatId, int userId, string message, int messageId)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                Debug.WriteLine($"Получено сообщение {messageId} в чате {chatId} от пользователя {userId}");

                if (currentChatID == chatId)
                {
                    await AddNewMessageAsync(messageId);

                    if (userId != currentUserID)
                    {
                        await MarkMessagesAsRead();
                    }
                }
                else
                {
                    // Обновляем счетчик непрочитанных в UI
                    await UpdateUnreadCount(chatId, true);
                }
            });
        }

        private async void OnFileReceived(int chatId, int userId, string fileName, int messageId)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                Debug.WriteLine($"Получен файл {fileName} в чате {chatId} от пользователя {userId}");

                if (currentChatID == chatId)
                {
                    await AddNewMessageAsync(messageId);

                    if (userId != currentUserID)
                    {
                        await MarkMessagesAsRead();
                    }
                }
                else
                {
                    // Обновляем счетчик непрочитанных в UI
                    await UpdateUnreadCount(chatId, true);
                }
            });
        }
        public class SignalRManager : IDisposable
        {
            private HubConnection _hubConnection;
            private string _url;
            private int _userId;

            public event Action<int, int, string, int> OnMessageReceived;
            public event Action<int, int, string, int> OnFileReceived;

            public SignalRManager(string url, int userId)
            {
                _url = url;
                _userId = userId;
            }

            public async Task<bool> ConnectAsync()
            {
                try
                {
                    _hubConnection = new HubConnectionBuilder()
                        .WithUrl(_url)
                        .WithAutomaticReconnect()
                        .Build();

                    // Настройка callback'ов
                    _hubConnection.On<int, int, string, int>("ReceiveMessage",
                        (chatId, userId, message, messageId) =>
                        {
                            OnMessageReceived?.Invoke(chatId, userId, message, messageId);
                        });

                    _hubConnection.On<int, int, string, int>("ReceiveFile",
                        (chatId, userId, fileName, messageId) =>
                        {
                            OnFileReceived?.Invoke(chatId, userId, fileName, messageId);
                        });

                    await _hubConnection.StartAsync();
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Ошибка подключения SignalR: {ex.Message}");
                    return false;
                }
            }

            public async Task SendMessageAsync(string method, params object[] args)
            {
                if (_hubConnection?.State == HubConnectionState.Connected)
                {
                    await _hubConnection.SendCoreAsync(method, args);
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
                _hubConnection?.DisposeAsync();
            }
        }

        // Обработчик прокрутки
        private async void MessagesScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // Проверяем, находится ли пользователь внизу
            _isUserAtBottom = e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 10;

            if (_isUserAtBottom && e.ExtentHeightChange > 0)
            {
                // Пользователь был внизу и добавились новые сообщения - прокручиваем
                isAutoScrolling = true;
                MessagesScrollViewer.ScrollToBottom();
                isAutoScrolling = false;
            }
        }

        // Метод отметки сообщений как прочитанных
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
                        // Сбрасываем счетчик непрочитанных
                        await UpdateUnreadCount(currentChatID, false);

                        // Обновляем список чатов
                        await Dispatcher.InvokeAsync(() =>
                        {
                            LoadUserChats();
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка при отметке сообщений как прочитанных: {ex.Message}");
            }
        }

        // Загрузка чатов пользователя
        private async void LoadUserChats()
        {
            try
            {
                Debug.WriteLine($"=== ЗАГРУЗКА ЧАТОВ ДЛЯ ПОЛЬЗОВАТЕЛЯ ID: {currentUserID} ===");

                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    using (SqlCommand command = new SqlCommand("GetUserChatsWithUnread", connection))
                    {
                        command.CommandType = CommandType.StoredProcedure;
                        command.Parameters.AddWithValue("@UserId", currentUserID);

                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            var chatList = new List<ChatInfo>();

                            if (reader.HasRows)
                            {
                                while (await reader.ReadAsync())
                                {
                                    int chatID = reader.GetInt32(0);
                                    string chatName = reader.IsDBNull(1) ? "Неизвестный чат" : reader.GetString(1);
                                    string lastMessage = reader.IsDBNull(2) ? "Нет сообщений" : reader.GetString(2);
                                    int unreadCount = reader.GetInt32(3);

                                    DateTime lastMessageDate = reader.IsDBNull(4)
                                        ? reader.GetDateTime(5)
                                        : reader.GetDateTime(4);

                                    var chatInfo = new ChatInfo
                                    {
                                        ChatId = chatID,
                                        ChatName = chatName,
                                        LastMessage = lastMessage,
                                        UnreadCount = unreadCount,
                                        LastMessageDate = lastMessageDate
                                    };

                                    chatList.Add(chatInfo);

                                    // Сохраняем информацию о чате
                                    _chatInfos[chatID] = chatInfo;

                                    // Сохраняем счетчик непрочитанных
                                    _unreadCounts[chatID] = unreadCount;
                                }
                            }

                            chatList = chatList.OrderByDescending(c => c.LastMessageDate).ToList();

                            await Dispatcher.InvokeAsync(() =>
                            {
                                Fellows.Children.Clear();
                                _chatButtons.Clear();

                                foreach (var chat in chatList)
                                {
                                    AddChatButton(chat.ChatId, chat.ChatName, chat.LastMessage, chat.UnreadCount);
                                }

                                if (chatList.Count == 0)
                                {
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
            catch (Exception ex)
            {
                Debug.WriteLine($"ОШИБКА в LoadUserChats: {ex.Message}");
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Ошибка загрузки чатов: {ex.Message}");
                });
            }
        }

        // Загрузка сообщений чата
        private async Task LoadChatMessages(int chatID, bool isOpening = false)
        {
            if (_isLoadingSequentially) return;

            _isLoadingSequentially = true;
            _lastLoadedMessageId = 0;

            try
            {
                // Очищаем чат перед загрузкой
                await Dispatcher.InvokeAsync(() => Reports.Children.Clear());

                // Сначала открываем пустой чат
                await Dispatcher.InvokeAsync(() => {
                    string chatName = _chatInfos.GetValueOrDefault(chatID)?.ChatName ?? $"ID: {chatID}";
                    Comrade_information.Content = $"Чат: {chatName}";

                    Reports.Children.Add(new TextBlock
                    {
                        Text = "Загрузка сообщений...",
                        Foreground = Brushes.Gray,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(10)
                    });
                });

                // Загружаем сообщения последовательно, начиная с последних
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

                    // Используем процедуру GetChatMessages
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
        private async Task UpdateMessageFilesInUI(CachedMessage message)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                // Находим сообщение по ID и обновляем файлы
                foreach (var child in Reports.Children)
                {
                    if (child is StackPanel panel && panel.Tag is int messageId && messageId == message.MessageId)
                    {
                        // Находим контейнер сообщения и добавляем файлы
                        if (VisualTreeHelper.GetChildrenCount(panel) > 0)
                        {
                            var border = VisualTreeHelper.GetChild(panel, 0) as Border;
                            if (border != null && border.Child is StackPanel contentPanel)
                            {
                                // Добавляем файлы в контейнер сообщения
                                foreach (var file in message.Files)
                                {
                                    var fileControl = CreateFileControl(new MessageFile
                                    {
                                        FileId = file.FileId,
                                        FileName = file.FileName,
                                        FileType = file.FileType,
                                        FileData = file.FileData,
                                        FileSize = file.FileSize
                                    });

                                    contentPanel.Children.Add(fileControl);
                                }
                            }
                        }
                        break;
                    }
                }
            });
        }

        // Метод последовательной загрузки сообщений (от новых к старым)
        private async Task LoadMessagesSequentially(int chatID, bool isOpening)
        {
            try
            {
                // Убираем сообщение о загрузке
                await Dispatcher.InvokeAsync(() => {
                    if (Reports.Children.Count > 0)
                        Reports.Children.Clear();
                });

                // Загружаем последние 20 сообщений (самые новые)
                var messages = await GetLastMessages(chatID, 20);

                if (messages.Count == 0)
                {
                    await Dispatcher.InvokeAsync(() => {
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

                // Сортируем по ID в порядке убывания (новые сначала)
                var sortedMessages = messages.OrderByDescending(m => m.MessageId).ToList();

                // Сохраняем ID самого старого сообщения для возможной подгрузки более старых
                _lastLoadedMessageId = sortedMessages.Last().MessageId;

                // Отображаем сообщения последовательно с задержкой (от новых к старым)
                for (int i = sortedMessages.Count - 1; i >= 0; i--)
                {
                    var message = sortedMessages[i];
                    await DisplaySingleMessage(message);

                    // Небольшая задержка для эффекта последовательной загрузки
                    if (isOpening)
                        await Task.Delay(50);
                }

                // Сохраняем сообщения в кэше
                if (!_chatMessages.ContainsKey(chatID))
                    _chatMessages[chatID] = new List<CachedMessage>();

                _chatMessages[chatID].AddRange(sortedMessages);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка последовательной загрузки: {ex.Message}");
            }
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
                    message.Files.Select(f => new MessageFile
                    {
                        FileId = f.FileId,
                        FileName = f.FileName,
                        FileType = f.FileType,
                        FileData = f.FileData,
                        FileSize = f.FileSize
                    }).ToList()
                );
            });
        }

        // Загрузка файлов сообщения
        private async Task<List<CachedFile>> GetMessageFilesFromDatabase(int messageId)
        {
            var files = new List<CachedFile>();

            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                using (SqlCommand command = new SqlCommand("GetMessageFiles", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@MessageId", messageId);

                    await connection.OpenAsync();
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var file = new CachedFile
                            {
                                FileId = reader.GetInt32(0),
                                FileName = reader.GetString(1),
                                FileType = reader.GetString(2),
                                FileData = (byte[])reader.GetValue(3),
                                FileSize = reader.GetInt32(4)
                            };

                            // Кэшируем только небольшие файлы
                            if (file.FileSize <= 5 * 1024 * 1024) // 5 MB
                            {
                                // Вычисляем хэш файла для идентификации в кэше
                                using (var sha256 = SHA256.Create())
                                {
                                    var hash = sha256.ComputeHash(file.FileData);
                                    file.FileHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                                }
                                files.Add(file);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка загрузки файлов: {ex.Message}");
            }

            return files;
        }

        // Метод добавления нового сообщения (без перезагрузки всего чата)
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
                                // Добавляем сообщение в UI без перезагрузки всего чата
                                CreateMessageUI(
                                    message.MessageId,
                                    message.MessageText,
                                    message.SendDate,
                                    message.UserName,
                                    message.UserId,
                                    message.Files.Select(f => new MessageFile
                                    {
                                        FileId = f.FileId,
                                        FileName = f.FileName,
                                        FileType = f.FileType,
                                        FileData = f.FileData,
                                        FileSize = f.FileSize
                                    }).ToList()
                                );

                                // Прокручиваем вниз только если сообщение от текущего пользователя
                                // или пользователь уже был внизу
                                if (message.UserId == currentUserID || _isUserAtBottom)
                                {
                                    isAutoScrolling = true;
                                    MessagesScrollViewer.ScrollToBottom();
                                    isAutoScrolling = false;
                                }

                                // Обновляем список чатов
                                LoadUserChats();
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

        // Создание UI для сообщения
        private void CreateMessageUI(int messageId, string messageText, DateTime createdDate, string userNick, int messageAuthorID, List<MessageFile> files)
        {
            try
            {
                // Проверяем, нет ли уже такого сообщения в UI
                foreach (var child in Reports.Children)
                {
                    if (child is StackPanel panel && panel.Tag is int existingId && existingId == messageId)
                    {
                        return; // Сообщение уже есть
                    }
                }

                StackPanel messagePanel = new StackPanel
                {
                    Margin = new Thickness(5),
                    HorizontalAlignment = messageAuthorID == currentUserID ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                    MaxWidth = 400,
                    Tag = messageId // Сохраняем ID сообщения в Tag
                };

                // Основной контейнер сообщения
                Border messageContainer = new Border
                {
                    Background = messageAuthorID == currentUserID ? Brushes.LightGreen : Brushes.LightBlue,
                    Padding = new Thickness(10),
                    CornerRadius = new CornerRadius(10),
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1)
                };

                StackPanel contentPanel = new StackPanel();

                // Заголовок с именем пользователя и временем
                TextBlock headerText = new TextBlock
                {
                    Text = $"{userNick} ({createdDate:HH:mm})",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 5)
                };
                contentPanel.Children.Add(headerText);

                // Текст сообщения
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

                // Файлы сообщения
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

        // Создание контрола для отображения файла
        private UIElement CreateFileControl(MessageFile file)
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
                Tag = file // Сохраняем объект файла в Tag
            };

            StackPanel filePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            // Вычисляем хэш файла
            string fileHash = CalculateFileHash(file.FileData);

            // Проверяем кэш
            var cachedInfo = _cacheManager.GetCachedFile(fileHash);

            if (cachedInfo != null && File.Exists(cachedInfo.FilePath))
            {
                // Файл уже в кэше - показываем как ссылку
                file.Status = MessageFile.FileStatus.Downloaded;
                file.CachedFilePath = cachedInfo.FilePath;

                TextBlock cachedIcon = new TextBlock
                {
                    Text = "✅ ",
                    FontSize = 16,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                filePanel.Children.Add(cachedIcon);

                // Обработчик для открытия файла
                fileBorder.MouseLeftButtonDown += (s, e) => OpenCachedFile(cachedInfo.FilePath);
            }
            else
            {
                // Файл не в кэше - кнопка для скачивания
                TextBlock downloadIcon = new TextBlock
                {
                    Text = "⬇️ ",
                    FontSize = 16,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                filePanel.Children.Add(downloadIcon);

                // Обработчик для скачивания
                fileBorder.MouseLeftButtonDown += async (s, e) => await DownloadAndCacheFile(file, fileHash);
            }

            StackPanel infoPanel = new StackPanel();

            // Имя файла
            TextBlock fileNameText = new TextBlock
            {
                Text = file.FileName,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };

            // Размер файла
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

        // Получение иконки для типа файла
        private string GetFileIcon(string fileType)
        {
            return fileType switch
            {
                "image" => "🖼️",
                "text" => "📄",
                _ => "📎"
            };
        }

        // Форматирование размера файла
        private string FormatFileSize(int fileSize)
        {
            if (fileSize < 1024)
                return $"{fileSize} B";
            else if (fileSize < 1024 * 1024)
                return $"{fileSize / 1024} KB";
            else
                return $"{(double)fileSize / (1024 * 1024):F1} MB";
        }

        // Скачивание/просмотр файла
        private async void DownloadFile(MessageFile file)
        {
            try
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    FileName = file.FileName,
                    Filter = "Все файлы (*.*)|*.*"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    File.WriteAllBytes(saveFileDialog.FileName, file.FileData);
                    MessageBox.Show($"Файл сохранен: {saveFileDialog.FileName}");

                    // Если это изображение, предлагаем открыть его
                    if (file.FileType == "image")
                    {
                        var result = MessageBox.Show("Открыть изображение?", "Файл сохранен",
                            MessageBoxButton.YesNo, MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = saveFileDialog.FileName,
                                UseShellExecute = true
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении файла: {ex.Message}");
            }
        }

        // Расчет хэша файла
        private string CalculateFileHash(byte[] fileData)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(fileData);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        // Скачивание и кэширование файла
        private async Task DownloadAndCacheFile(MessageFile file, string fileHash)
        {
            try
            {
                // Сохраняем в кэш
                await _cacheManager.CacheFileAsync(fileHash, file.FileName, file.FileType, file.FileData);

                // Обновляем UI
                await Dispatcher.InvokeAsync(() =>
                {
                    // Находим и обновляем контрол файла
                    var fileControl = FindFileControl(file.FileId);
                    if (fileControl != null)
                    {
                        // Заменяем на версию с кэшем
                        var parent = VisualTreeHelper.GetParent(fileControl) as Panel;
                        if (parent != null)
                        {
                            int index = parent.Children.IndexOf(fileControl);
                            parent.Children.RemoveAt(index);
                            parent.Children.Insert(index, CreateFileControl(file));
                        }
                    }
                });

                // Открываем файл
                OpenCachedFile(System.IO.Path.Combine(_cacheManager.GetCacheFolder(), fileHash + System.IO.Path.GetExtension(file.FileName)));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при скачивании файла: {ex.Message}");
            }
        }

        // Поиск контрола файла по ID
        private UIElement FindFileControl(int fileId)
        {
            // Поиск контрола файла по ID
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

        // Рекурсивный поиск контрола файла в дочерних элементах
        private UIElement FindFileControlInChildren(DependencyObject parent, int fileId)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Border border && border.Tag is MessageFile file && file.FileId == fileId)
                    return border;

                var result = FindFileControlInChildren(child, fileId);
                if (result != null)
                    return result;
            }
            return null;
        }

        // Открытие кэшированного файла
        private void OpenCachedFile(string filePath)
        {
            try
            {
                var result = MessageBox.Show(
                    $"Открыть файл?\nПуть: {System.IO.Path.GetFileName(filePath)}",
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

        private async Task CleanupSignalR()
        {
            if (_signalRManager != null)
            {
                if (currentChatID != 0)
                {
                    await _signalRManager.LeaveChatGroupAsync(currentChatID);
                }

                // Отписываемся от событий
                _signalRManager.OnMessageReceived -= OnMessageReceived;
                _signalRManager.OnFileReceived -= OnFileReceived;
                _signalRManager.Dispose();
            }
        }

        // Отправка сообщения
        private async void SendMessage()
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

            try
            {
                // Сохраняем в БД
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

                // Добавляем сообщение в UI сразу
                await AddMessageToUI(newMessageId, messageText, true);

                // Уведомляем через SignalR
                await _signalRManager.SendMessageAsync("SendMessage", currentChatID, currentUserID, messageText, newMessageId);

                // Обновляем список чатов для показа последнего сообщения
                LoadUserChats();

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
        }

        // Добавление кнопки чата
        private void AddChatButton(int chatID, string chatName, string lastMessage, int unreadCount)
        {
            Border chatBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(5, 5, 30, 5),
                CornerRadius = new CornerRadius(5),
                Tag = chatID
            };

            StackPanel contentPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(8)
            };

            TextBlock nameText = new TextBlock
            {
                Text = chatName,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };

            TextBlock lastMessageText = new TextBlock
            {
                Text = lastMessage.Length > 30 ? lastMessage.Substring(0, 30) + "..." : lastMessage,
                FontSize = 10,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            };

            contentPanel.Children.Add(nameText);
            contentPanel.Children.Add(lastMessageText);

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

                Grid containerGrid = new Grid();
                containerGrid.Children.Add(contentPanel);
                containerGrid.Children.Add(badge);
                chatBorder.Child = containerGrid;
            }
            else
            {
                chatBorder.Child = contentPanel;
            }

            // Сохраняем ссылку на кнопку чата
            _chatButtons[chatID] = chatBorder;

            chatBorder.MouseLeftButtonDown += async (s, e) =>
            {
                // Покидаем предыдущую группу чата
                if (currentChatID != 0)
                {
                    await _signalRManager.LeaveChatGroupAsync(currentChatID);
                }

                currentChatID = chatID;
                Comrade_information.Content = $"Чат: {chatName}";

                // Входим в группу нового чата
                await _signalRManager.JoinChatGroupAsync(chatID);

                await LoadChatMessages(chatID, true);

                // Сбрасываем счетчик непрочитанных
                await UpdateUnreadCount(chatID, false);

                // Отмечаем сообщения как прочитанные
                await MarkMessagesAsRead();
            };

            chatBorder.MouseEnter += (s, e) =>
            {
                chatBorder.Background = Brushes.LightBlue;
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
                        AddChatButton(newChatID, userName, "Новое сообщение", 0);
                        ID_search.Text = "";
                        MessageBox.Show($"Приватный чат с {userName} создан!");
                        LoadUserChats();
                    });
                }
            }
            catch (SqlException sqlEx)
            {
                MessageBox.Show($"{sqlEx.Message}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка создания чата: {ex.Message}");
            }
        }

        // Метод добавления сообщения в UI (для текстовых сообщений)
        private async Task AddMessageToUI(int messageId, string messageText, bool isMyMessage)
        {
            try
            {
                // Создаем временное сообщение в UI
                var tempMessage = new CachedMessage
                {
                    MessageId = messageId,
                    ChatId = currentChatID,
                    UserId = currentUserID,
                    UserName = isMyMessage ? "Вы" : "Собеседник", // Временное имя
                    MessageText = messageText,
                    SendDate = DateTime.Now,
                    IsRead = true
                };

                // Добавляем в UI
                await DisplaySingleMessage(tempMessage);

                // Если сообщение от нас, прокручиваем вниз
                if (isMyMessage || _isUserAtBottom)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        isAutoScrolling = true;
                        MessagesScrollViewer.ScrollToBottom();
                        isAutoScrolling = false;
                    });
                }

                // Обновляем кэш сообщений
                if (!_chatMessages.ContainsKey(currentChatID))
                    _chatMessages[currentChatID] = new List<CachedMessage>();

                _chatMessages[currentChatID].Add(tempMessage);

                // Обновляем список чатов (для показа последнего сообщения)
                LoadUserChats();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка добавления сообщения в UI: {ex.Message}");
            }
        }

        // Метод добавления сообщения с файлом в UI
        private async Task AddMessageWithFileToUI(int messageId, string messageText, string fileName)
        {
            try
            {
                // Загружаем информацию о файле из БД
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
                                FileId = messageId, // Временный ID
                                FileName = fileName,
                                FileType = GetFileTypeByExtension(fileName),
                                FileData = fileData,
                                FileSize = fileData.Length
                            }
                        }
                    };

                    // Добавляем в UI
                    await DisplaySingleMessage(tempMessage);

                    // Прокручиваем вниз
                    await Dispatcher.InvokeAsync(() =>
                    {
                        isAutoScrolling = true;
                        MessagesScrollViewer.ScrollToBottom();
                        isAutoScrolling = false;
                    });

                    // Обновляем кэш
                    if (!_chatMessages.ContainsKey(currentChatID))
                        _chatMessages[currentChatID] = new List<CachedMessage>();

                    _chatMessages[currentChatID].Add(tempMessage);

                    // Обновляем список чатов
                    LoadUserChats();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка добавления сообщения с файлом в UI: {ex.Message}");
            }
        }

        // Метод получения данных файла
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

        // Вспомогательный метод для определения типа файла
        private string GetFileTypeByExtension(string fileName)
        {
            string extension = System.IO.Path.GetExtension(fileName).ToLower();

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

                    // Отправляем сообщение
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

                            // Добавляем сообщение с файлом в UI
                            await AddMessageWithFileToUI(newMessageId, messageText, fileName);

                            // Уведомляем через SignalR
                            await _signalRManager.SendMessageAsync("SendFile", currentChatID, currentUserID, fileName, newMessageId);

                            // Обновляем список чатов
                            LoadUserChats();

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

        // Обновление счетчика непрочитанных
        private async Task UpdateUnreadCount(int chatId, bool increment = false)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    if (increment)
                    {
                        // Увеличиваем счетчик непрочитанных
                        using (SqlCommand command = new SqlCommand(@"
                    UPDATE UserChats 
                    SET UnreadCount = ISNULL(UnreadCount, 0) + 1 
                    WHERE ChatId = @ChatId AND UserId = @UserId", connection))
                        {
                            command.Parameters.AddWithValue("@ChatId", chatId);
                            command.Parameters.AddWithValue("@UserId", currentUserID);
                            await command.ExecuteNonQueryAsync();
                        }
                    }
                    else
                    {
                        // Сбрасываем счетчик
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

                    // Получаем обновленный счетчик
                    using (SqlCommand command = new SqlCommand(@"
                SELECT UnreadCount FROM UserChats 
                WHERE ChatId = @ChatId AND UserId = @UserId", connection))
                    {
                        command.Parameters.AddWithValue("@ChatId", chatId);
                        command.Parameters.AddWithValue("@UserId", currentUserID);

                        var result = await command.ExecuteScalarAsync();
                        int unreadCount = result != DBNull.Value ? Convert.ToInt32(result) : 0;

                        // Обновляем локальный кэш
                        _unreadCounts[chatId] = unreadCount;

                        // Обновляем UI
                        UpdateChatUnreadCount(chatId, unreadCount);

                        // Обновляем информацию о чате
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

        // Обновление счетчика в UI
        private void UpdateChatUnreadCount(int chatId, int unreadCount)
        {
            if (_chatButtons.ContainsKey(chatId))
            {
                var chatButton = _chatButtons[chatId];

                // Находим контейнер
                var container = chatButton.Child as Grid;
                if (container == null)
                {
                    // Если нет Grid, создаем его
                    var originalContent = chatButton.Child;
                    chatButton.Child = null;

                    container = new Grid();
                    container.Children.Add(originalContent);
                    chatButton.Child = container;
                }

                // Ищем существующий badge
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
                        // Создаем новый badge
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
                        // Обновляем существующий
                        var textBlock = existingBadge.Child as TextBlock;
                        if (textBlock != null)
                        {
                            textBlock.Text = unreadCount > 99 ? "99+" : unreadCount.ToString();
                        }
                    }
                }
                else if (existingBadge != null)
                {
                    // Удаляем badge
                    container.Children.Remove(existingBadge);
                }
            }
        }

        // Поиск badge в дочерних элементах
        private Border FindBadgeInChildren(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Border border && border.Background == Brushes.Red)
                    return border;

                var result = FindBadgeInChildren(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        // Остальные методы без изменений
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

        // Файловые операции
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
                string fileName = System.IO.Path.GetFileName(filePath);

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

        private void Enter_to_Options_Click(object sender, RoutedEventArgs e)
        {
            OptionsPage optionsPage = new OptionsPage(currentUserID);
            this.NavigationService.Navigate(optionsPage);
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
            // Пустая реализация
        }
    }
}