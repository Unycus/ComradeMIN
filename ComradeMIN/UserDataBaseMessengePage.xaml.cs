using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Win32;
using System;
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

namespace ComradeMIN
{
    public partial class UserDataBaseMessengePage : Page
    {
        private string connectionString = "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=Void;Integrated Security=True";
        private int currentUserID;
        private int currentChatID = 0;
        private bool isAutoScrolling = true;
        private HubConnection _hubConnection;
        private string _signalRUrl = "http://26.254.64.152:5000/chatHub"; // IP первого устройства
        private bool _isSignalRConnected = false;


        // Система реального времени
        private DispatcherTimer _updateTimer;
        private DateTime _lastUpdateTime;
        private bool _isPageActive = true;

        public UserDataBaseMessengePage(int userID)
        {
            InitializeComponent();
            currentUserID = userID;
            _lastUpdateTime = DateTime.Now;

            Debug.WriteLine($"Создана страница чатов для пользователя ID: {currentUserID}");

            if (Fellows == null || Reports == null)
            {
                MessageBox.Show("Ошибка загрузки элементов интерфейса");
                return;
            }

            // Инициализация SignalR
            InitializeSignalR();

            MessagesScrollViewer.ScrollChanged += MessagesScrollViewer_ScrollChanged;
            LoadUserChats();
            Text_for_Comrade.KeyDown += Text_for_Comrade_KeyDown;

            this.Loaded += (s, e) => Text_for_Comrade.Focus();
            this.Unloaded += async (s, e) => await CleanupSignalR();
        }

        // Инициализация системы реального времени
        private void InitializeRealTimeUpdates()
        {
            _updateTimer = new DispatcherTimer();
            _updateTimer.Interval = TimeSpan.FromSeconds(3); // Проверка каждые 3 секунды
            _updateTimer.Tick += async (s, e) => await CheckForUpdatesAsync();

            // Запускаем таймер когда страница активна
            this.Loaded += (s, e) =>
            {
                _isPageActive = true;
                _updateTimer.Start();
            };

            // Останавливаем когда страница неактивна
            this.Unloaded += (s, e) =>
            {
                _isPageActive = false;
                _updateTimer.Stop();
            };
        }

        // Проверка обновлений с оптимизацией
        private async Task CheckForUpdatesAsync()
        {
            if (!_isPageActive) return;

            try
            {
                bool hasUpdates = await CheckForDatabaseUpdatesAsync();
                if (hasUpdates)
                {
                    Debug.WriteLine("Обнаружены изменения в чатах - обновляю список");
                    await Dispatcher.InvokeAsync(() => LoadUserChats());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка при проверке обновлений: {ex.Message}");
            }
        }

        // Проверка изменений в базе данных
        private async Task<bool> CheckForDatabaseUpdatesAsync()
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                using (SqlCommand command = new SqlCommand("CheckChatsUpdates", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@UserId", currentUserID);
                    command.Parameters.AddWithValue("@LastCheckTime", _lastUpdateTime);

                    await connection.OpenAsync();
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            int changeCount = reader.GetInt32(0);
                            DateTime lastUpdateTime = reader.GetDateTime(1);

                            if (changeCount > 0)
                            {
                                _lastUpdateTime = lastUpdateTime;
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка проверки обновлений БД: {ex.Message}");
            }

            return false;
        }

        // Обработчик прокрутки (без изменений)
        private async void MessagesScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (isAutoScrolling) return;

            if (e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 10)
            {
                Debug.WriteLine("Пользователь прокрутил до конца чата");
                await MarkMessagesAsRead();
            }
        }

        // Метод отметки сообщений как прочитанных (без изменений)
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

        // Загрузка чатов пользователя с оптимизацией
        private async void LoadUserChats()
        {
            try
            {
                Debug.WriteLine($"=== ЗАГРУЗКА ЧАТОВ ДЛЯ ПОЛЬЗОВАТЕЛЯ ID: {currentUserID} ===");

                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    using (SqlCommand command = new SqlCommand("GetUserChats", connection))
                    {
                        command.CommandType = CommandType.StoredProcedure;
                        command.Parameters.AddWithValue("@UserId", currentUserID);

                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            // Временный список для сортировки
                            var chatList = new List<ChatInfo>();

                            if (reader.HasRows)
                            {
                                while (reader.Read())
                                {
                                    int chatID = reader.GetInt32(0);
                                    string chatName = reader.IsDBNull(1) ? "Неизвестный чат" : reader.GetString(1);
                                    string lastMessage = reader.IsDBNull(3) ? "Нет сообщений" : reader.GetString(3);
                                    int unreadCount = reader.IsDBNull(5) ? 0 : reader.GetInt32(5);

                                    // Получаем дату последнего сообщения
                                    DateTime lastMessageDate = reader.IsDBNull(4)
                                        ? reader.GetDateTime(2) // Используем дату создания чата, если нет сообщений
                                        : reader.GetDateTime(4);

                                    chatList.Add(new ChatInfo
                                    {
                                        ChatId = chatID,
                                        ChatName = chatName,
                                        LastMessage = lastMessage,
                                        UnreadCount = unreadCount,
                                        LastMessageDate = lastMessageDate
                                    });
                                }
                            }

                            // Сортируем список по дате последнего сообщения (от новых к старым)
                            chatList = chatList.OrderByDescending(c => c.LastMessageDate).ToList();

                            await Dispatcher.InvokeAsync(() =>
                            {
                                Fellows.Children.Clear();

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

                _lastUpdateTime = DateTime.Now;
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

        // Вспомогательный класс для хранения информации о чате
        private class ChatInfo
        {
            public int ChatId { get; set; }
            public string ChatName { get; set; }
            public string LastMessage { get; set; }
            public int UnreadCount { get; set; }
            public DateTime LastMessageDate { get; set; }
        }

        // Загрузка сообщений чата с оптимизацией
        // Загрузка сообщений чата с оптимизацией (ИСПРАВЛЕННАЯ ВЕРСИЯ)
        private async Task LoadChatMessages(int chatID)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                using (SqlCommand command = new SqlCommand("GetChatMessages", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@ChatId", chatID);

                    await connection.OpenAsync();
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        var messages = new List<(int messageId, string messageText, DateTime createdDate, string userNick, int messageAuthorID)>();

                        // Сначала читаем все сообщения
                        while (await reader.ReadAsync()) // Используем асинхронное чтение
                        {
                            messages.Add((
                                reader.GetInt32(0),
                                reader.GetString(4),
                                reader.GetDateTime(5),
                                reader.GetString(3),
                                reader.GetInt32(2)
                            ));
                        }

                        // Теперь в UI потоке создаем сообщения
                        await Dispatcher.InvokeAsync(async () =>
                        {
                            Reports.Children.Clear();

                            foreach (var message in messages)
                            {
                                // Загружаем файлы для каждого сообщения
                                var files = await LoadMessageFiles(message.messageId);

                                CreateMessageUI(
                                    message.messageId,
                                    message.messageText,
                                    message.createdDate,
                                    message.userNick,
                                    message.messageAuthorID,
                                    files
                                );
                            }

                            // Автоматическая прокрутка вниз
                            isAutoScrolling = true;
                            MessagesScrollViewer.ScrollToBottom();
                            isAutoScrolling = false;

                            // Отмечаем сообщения как прочитанные при открытии чата
                            _ = MarkMessagesAsRead();
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Ошибка загрузки сообщений: {ex.Message}");
                });
            }
        }

        // Загрузка файлов сообщения
        // Загрузка файлов сообщения (ИСПРАВЛЕННАЯ ВЕРСИЯ)
        private async Task<List<MessageFile>> LoadMessageFiles(int messageId)
        {
            var files = new List<MessageFile>();

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
                        while (await reader.ReadAsync()) // Используем асинхронное чтение
                        {
                            files.Add(new MessageFile
                            {
                                FileId = reader.GetInt32(0),
                                FileName = reader.GetString(1),
                                FileType = reader.GetString(2),
                                FileData = (byte[])reader.GetValue(3),
                                FileSize = reader.GetInt32(4)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка загрузки файлов сообщения: {ex.Message}");
            }

            return files;
        }

        // Создание UI для сообщения с файлами
        // Создание UI для сообщения с файлами (УПРОЩЕННАЯ ВЕРСИЯ)
        private void CreateMessageUI(int messageId, string messageText, DateTime createdDate, string userNick, int messageAuthorID, List<MessageFile> files)
        {
            try
            {
                StackPanel messagePanel = new StackPanel
                {
                    Margin = new Thickness(5),
                    HorizontalAlignment = messageAuthorID == currentUserID ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                    MaxWidth = 400
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
                Cursor = Cursors.Hand
            };

            StackPanel filePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            // Иконка файла в зависимости от типа
            TextBlock fileIcon = new TextBlock
            {
                Text = GetFileIcon(file.FileType),
                FontSize = 16,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

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

            filePanel.Children.Add(fileIcon);
            filePanel.Children.Add(infoPanel);

            // Обработчик клика для скачивания/просмотра файла
            fileBorder.MouseLeftButtonDown += (s, e) => DownloadFile(file);

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
        private void DownloadFile(MessageFile file)
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
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
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

        // Класс для хранения информации о файле
        private class MessageFile
        {
            public int FileId { get; set; }
            public string FileName { get; set; }
            public string FileType { get; set; }
            public byte[] FileData { get; set; }
            public int FileSize { get; set; }
        }

        // Отправка сообщения с обновлением состояния
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
                using (SqlConnection connection = new SqlConnection(connectionString))
                using (SqlCommand command = new SqlCommand("SendMessage", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@ChatId", currentChatID);
                    command.Parameters.AddWithValue("@UserId", currentUserID);
                    command.Parameters.AddWithValue("@MessageText", messageText);

                    await connection.OpenAsync();
                    await command.ExecuteNonQueryAsync();
                }

                // Уведомляем через SignalR
                if (_isSignalRConnected && _hubConnection.State == HubConnectionState.Connected)
                {
                    await _hubConnection.SendAsync("SendMessage", currentChatID, currentUserID, messageText);

                    // Также уведомляем об обновлении списка чатов
                    await _hubConnection.SendAsync("NotifyChatListUpdate", currentUserID);
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    Text_for_Comrade.Text = "";
                    Text_for_Comrade.Focus();

                    // Локально обновляем сообщения (не ждем SignalR для мгновенного отклика)
                    _ = LoadChatMessages(currentChatID);
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка отправки сообщения: {ex.Message}");
                Text_for_Comrade.Focus();
            }
        }

        // Остальные методы остаются без изменений
        private async void Enter_to_search_Click(object sender, RoutedEventArgs e)
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

        // Добавление кнопки чата (без изменений)
        private void AddChatButton(int chatID, string chatName, string lastMessage, int unreadCount)
        {
            foreach (Button existingButton in Fellows.Children.OfType<Button>())
            {
                if (existingButton.Tag != null && (int)existingButton.Tag == chatID)
                {
                    return;
                }
            }

            Border chatBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(5,5,30,5),
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
                    Child = new TextBlock
                    {
                        Text = unreadCount.ToString(),
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

            chatBorder.MouseLeftButtonDown += async (s, e) =>
            {
                // Покидаем предыдущую группу чата
                if (currentChatID != 0 && _isSignalRConnected)
                {
                    await _hubConnection.SendAsync("LeaveChatGroup", currentChatID);
                }

                currentChatID = chatID;
                Comrade_information.Content = $"Чат: {chatName}";

                // Входим в группу нового чата
                if (_isSignalRConnected && _hubConnection.State == HubConnectionState.Connected)
                {
                    await _hubConnection.SendAsync("JoinChatGroup", chatID);
                }

                await LoadChatMessages(chatID);

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

        // Остальные методы без изменений
        private void Text_for_Comrade_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (!Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
                {
                    SendMessage();
                    e.Handled = true;
                }
                // Если зажат Shift - разрешаем стандартное поведение (перенос строки)
            }
        }

        private void Enter_to_search_MouseEnter(object sender, MouseEventArgs e)
        {
            AnimateButtonScale("Enter_to_search", 1.1);
        }

        private void Enter_to_search_MouseLeave(object sender, MouseEventArgs e)
        {
            AnimateButtonScale("Enter_to_search", 1.1);
        }

        private void Enter_to_Options_MouseEnter(object sender, MouseEventArgs e)
        {
            AnimateButtonScale("Enter_to_Options", 1.1);
        }

        private void Enter_text_MouseEnter(object sender, MouseEventArgs e)
        {
            AnimateButtonScale("Enter_Text", 1.1);
        }

        private void Enter_text_MouseLeave(object sender, MouseEventArgs e)
        {
            AnimateButtonScale("Enter_Text", 1.1);
        }

        private void Enter_to_Options_MouseLeave(object sender, MouseEventArgs e)
        {
            AnimateButtonScale("Enter_to_Options", 1.1);
        }

        private void AnimateButtonScale(string buttonName, double scale)
        {
            Button button = null;

            switch (buttonName)
            {
                case "Enter_Text":
                    button = Enter_Text;
                    break;
                case "Enter_to_search":
                    button = Enter_to_search;
                    break;
                case "Enter_to_Options":
                    button = Enter_to_Options;
                    break;
                case "FileText":
                    // Для FileText это Border с кнопкой внутри
                    var border = FindName("FileText") as Border;
                    button = border?.Child as Button;
                    break;
            }

            if (button != null)
            {
                var template = button.Template;
                Rectangle oval = null;

                switch (buttonName)
                {
                    case "Enter_Text":
                        oval = template.FindName("Oval_Text", button) as Rectangle;
                        break;
                    case "Enter_to_search":
                        oval = template.FindName("Oval_ID_search", button) as Rectangle;
                        break;
                    case "Enter_to_Options":
                        oval = template.FindName("Oval_Options", button) as Rectangle;
                        break;
                    case "FileText":
                        oval = template.FindName("Oval_FileText", button) as Rectangle;
                        break;
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

        // Обработчик клика по кнопке файла
        private void FileText_Click(object sender, RoutedEventArgs e)
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

                // Проверяем размер файла (максимум 10MB)
                FileInfo fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > 10 * 1024 * 1024) // 10MB в байтах
                {
                    MessageBox.Show("Файл слишком большой. Максимальный размер: 10MB");
                    return;
                }

                // Отправляем файл
                _ = SendFileAsync(filePath, fileName);
            }
        }

        // Анимация для кнопки файла
        private void FileText_MouseEnter(object sender, MouseEventArgs e)
        {
            AnimateButtonScale("Oval_FileText", 1.1, FileText);
        }

        private void FileText_MouseLeave(object sender, MouseEventArgs e)
        {
            AnimateButtonScale("Oval_FileText", 1.1, FileText);
        }

        // Универсальный метод анимации для всех кнопок
        private void AnimateButtonScale(string ovalName, double scale, Button button)
        {
            var template = button.Template;
            Rectangle oval = template.FindName(ovalName, button) as Rectangle;

            if (oval != null)
            {
                var animation = new DoubleAnimation(scale, TimeSpan.FromMilliseconds(200));
                oval.RenderTransform = new ScaleTransform();
                oval.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
                oval.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
            }
        }

        // Асинхронная отправка файла
        // Асинхронная отправка файла (УПРОЩЕННАЯ ВЕРСИЯ)
        private async Task SendFileAsync(string filePath, string fileName)
        {
            try
            {
                // Читаем файл в массив байтов
                byte[] fileData = await File.ReadAllBytesAsync(filePath);

                // Определяем тип файла
                string fileType = GetFileType(filePath);

                // Упрощенный текст сообщения
                string displayText = $"[Файл: {fileName}]";

                // Отправляем файл
                bool success = await SendMessageWithFile(displayText, fileName, fileType, fileData, fileData.Length);

                if (success)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show($"Файл '{fileName}' успешно отправлен!");
                        // Обновляем сообщения в чате
                        _ = LoadChatMessages(currentChatID);
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

        // Определение типа файла
        private string GetFileType(string filePath)
        {
            string extension = System.IO.Path.GetExtension(filePath).ToLower();

            return extension switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" => "image",
                ".txt" or ".doc" or ".docx" or ".pdf" => "text",
                _ => "other"
            };
        }

        // Отправка сообщения с прикрепленным файлом
        // Отправка сообщения с прикрепленным файлом (УПРОЩЕННАЯ ВЕРСИЯ)
        private async Task<bool> SendMessageWithFile(string messageText, string fileName, string fileType, byte[] fileData, int fileSize)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    // Отправляем сообщение
                    int? messageId = null;
                    using (SqlCommand command = new SqlCommand("SendMessage", connection))
                    {
                        command.CommandType = CommandType.StoredProcedure;
                        command.Parameters.AddWithValue("@ChatId", currentChatID);
                        command.Parameters.AddWithValue("@UserId", currentUserID);
                        command.Parameters.AddWithValue("@MessageText", messageText);

                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            messageId = Convert.ToInt32(result);
                        }
                    }

                    // Если сообщение отправлено, сохраняем файл
                    if (messageId.HasValue)
                    {
                        using (SqlCommand fileCommand = new SqlCommand("SaveMessageFile", connection))
                        {
                            fileCommand.CommandType = CommandType.StoredProcedure;
                            fileCommand.Parameters.AddWithValue("@MessageId", messageId.Value);
                            fileCommand.Parameters.AddWithValue("@FileName", fileName);
                            fileCommand.Parameters.AddWithValue("@FileType", fileType);
                            fileCommand.Parameters.AddWithValue("@FileData", fileData);
                            fileCommand.Parameters.AddWithValue("@FileSize", fileSize);

                            await fileCommand.ExecuteNonQueryAsync();
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

        private void Enter_to_Options_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Функция настроек в разработке");
        }

        private void Enter_Text_Click(object sender, RoutedEventArgs e)
        {
            // Имитируем поведение Enter (без Shift)
            if (!Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
            {
                SendMessage();
            }
            else
            {
                // Если зажат Shift - добавляем перенос строки вместо отправки
                Text_for_Comrade.Text += Environment.NewLine;
                Text_for_Comrade.CaretIndex = Text_for_Comrade.Text.Length;
                Text_for_Comrade.Focus();
            }
        }

        private async void InitializeSignalR()
        {
            try
            {
                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(_signalRUrl)
                    .WithAutomaticReconnect()
                    .Build();

                // Обработчики событий от сервера
                _hubConnection.On<int, int, string>("ReceiveMessage", async (chatId, userId, message) =>
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        Debug.WriteLine($"Получено сообщение в чате {chatId} от пользователя {userId}");

                        // Если это текущий открытый чат - обновляем сообщения
                        if (currentChatID == chatId)
                        {
                            await LoadChatMessages(chatId);
                        }

                        // Всегда обновляем список чатов (для счетчика непрочитанных)
                        LoadUserChats();
                    });
                });

                _hubConnection.On("RefreshChats", () =>
                {
                    Dispatcher.Invoke(() => LoadUserChats());
                });

                _hubConnection.On<int, int>("MessageRead", (messageId, userId) =>
                {
                    // Можно обновить UI для отображения прочитанных сообщений
                    Debug.WriteLine($"Сообщение {messageId} прочитано пользователем {userId}");
                });

                _hubConnection.Reconnecting += error =>
                {
                    Debug.WriteLine($"SignalR переподключение: {error?.Message}");
                    return Task.CompletedTask;
                };

                _hubConnection.Reconnected += connectionId =>
                {
                    Debug.WriteLine("SignalR переподключен");
                    // При переподключении входим обратно в группу текущего чата
                    if (currentChatID != 0)
                    {
                        _ = _hubConnection.SendAsync("JoinChatGroup", currentChatID);
                    }
                    return Task.CompletedTask;
                };

                await _hubConnection.StartAsync();
                _isSignalRConnected = true;
                Debug.WriteLine("SignalR подключен успешно");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка подключения SignalR: {ex.Message}");
                _isSignalRConnected = false;

                // Fallback на таймер если SignalR недоступен
                InitializeRealTimeUpdates();
            }
        }

        private async Task CleanupSignalR()
        {
            if (_hubConnection != null)
            {
                if (currentChatID != 0)
                {
                    await _hubConnection.SendAsync("LeaveChatGroup", currentChatID);
                }
                await _hubConnection.StopAsync();
                await _hubConnection.DisposeAsync();
            }
        }

        private void Text_for_Comrade_TextChanged(object sender, RoutedEventArgs e)
        {
            
        }
    }
}