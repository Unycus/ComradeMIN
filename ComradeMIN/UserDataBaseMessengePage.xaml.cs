using System;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ComradeMIN
{
    public partial class UserDataBaseMessengePage : Page
    {
        private string connectionString = "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=Void;Integrated Security=True";
        private int currentUserID;
        private int currentChatID = 0;

        public UserDataBaseMessengePage()
        {
            InitializeComponent();
            currentUserID = 1; // По умолчанию

            if (Fellows == null || Reports == null || ID_search == null ||
                Text_for_Comrade == null || Comrade_information == null)
            {
                MessageBox.Show("Ошибка загрузки элементов интерфейса");
                return;
            }

            LoadUserChats();
            Text_for_Comrade.KeyDown += Text_for_Comrade_KeyDown;
        }
        public UserDataBaseMessengePage(int userID) : this()
        {
            currentUserID = userID;
        }

        // Загрузка чатов пользователя
        private async void LoadUserChats()
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                using (SqlCommand command = new SqlCommand("GetUserChats", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@IDFellow", currentUserID);

                    await connection.OpenAsync();
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            Fellows.Children.Clear();

                            while (reader.Read())
                            {
                                int chatID = reader.GetInt32(0);
                                string chatName = reader.IsDBNull(1) ? "Неизвестный" : reader.GetString(1);
                                AddChatButton(chatID, chatName);
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки чатов: {ex.Message}");
            }
        }

        // Поиск пользователя по ID и создание чата
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
                // Проверяем существование пользователя
                string userName = "";
                using (SqlConnection connection = new SqlConnection(connectionString))
                using (SqlCommand command = new SqlCommand("SELECT NickFellow FROM Fellows WHERE IDFellow = @UserID", connection))
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

                // Создаем приватный чат
                using (SqlConnection connection = new SqlConnection(connectionString))
                using (SqlCommand command = new SqlCommand("CreatePrivateChat", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@CurrentUserID", currentUserID);
                    command.Parameters.AddWithValue("@TargetUserID", targetUserID);

                    SqlParameter outputParam = new SqlParameter("@NewChatID", SqlDbType.Int)
                    {
                        Direction = ParameterDirection.Output
                    };
                    command.Parameters.Add(outputParam);

                    await connection.OpenAsync();
                    await command.ExecuteNonQueryAsync();

                    // Проверяем, что чат был создан
                    if (outputParam.Value == DBNull.Value)
                    {
                        MessageBox.Show("Не удалось создать чат");
                        return;
                    }

                    int newChatID = (int)outputParam.Value;

                    await Dispatcher.InvokeAsync(() =>
                    {
                        AddChatButton(newChatID, userName);
                        ID_search.Text = "";
                        MessageBox.Show($"Приватный чат с {userName} создан!");
                    });
                }
            }
            catch (SqlException sqlEx)
            {
                MessageBox.Show($"Ошибка базы данных при создании чата: {sqlEx.Message}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка создания чата: {ex.Message}");
            }
        }

        // Добавление кнопки чата
        private void AddChatButton(int chatID, string chatName)
        {
            // Проверяем, не существует ли уже кнопки с таким чатом
            foreach (Button existingButton in Fellows.Children.OfType<Button>())
            {
                if ((int)existingButton.Tag == chatID)
                {
                    return; // Кнопка уже существует
                }
            }

            Button chatButton = new Button
            {
                Content = chatName,
                Tag = chatID,
                Height = 40,
                Margin = new Thickness(5),
                Background = Brushes.White,
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Gray,
                Cursor = Cursors.Hand
            };

            chatButton.Click += async (s, e) =>
            {
                currentChatID = chatID;
                Comrade_information.Content = $"Связь: {chatName}";
                await LoadChatMessages(chatID);
            };

            Fellows.Children.Add(chatButton);
        }

        // Загрузка сообщений чата (упрощенная версия без прокрутки)
        private async Task LoadChatMessages(int chatID)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                using (SqlCommand command = new SqlCommand("GetChatMessages", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@IDChat", chatID);

                    await connection.OpenAsync();
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            Reports.Children.Clear();

                            while (reader.Read())
                            {
                                string messageText = reader.GetString(1);
                                DateTime createdDate = reader.GetDateTime(5);
                                string userNick = reader.GetString(7);
                                int messageAuthorID = reader.GetInt32(6);

                                Border messageContainer = new Border
                                {
                                    Background = messageAuthorID == currentUserID ? Brushes.LightGreen : Brushes.LightBlue,
                                    Margin = new Thickness(5),
                                    Padding = new Thickness(10),
                                    CornerRadius = new CornerRadius(10),
                                    BorderBrush = Brushes.Gray,
                                    BorderThickness = new Thickness(1),
                                    HorizontalAlignment = messageAuthorID == currentUserID ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                                    MaxWidth = 400
                                };

                                TextBlock messageTextBlock = new TextBlock
                                {
                                    Text = $"{userNick} ({createdDate:HH:mm}): {messageText}",
                                    FontSize = 12,
                                    TextWrapping = TextWrapping.Wrap
                                };

                                messageContainer.Child = messageTextBlock;
                                Reports.Children.Add(messageContainer);
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки сообщений: {ex.Message}");
            }
        }

        // Отправка сообщения
        private async void SendMessage()
        {
            if (currentChatID == 0)
            {
                MessageBox.Show("Выберите чат для отправки сообщения");
                return;
            }

            if (string.IsNullOrWhiteSpace(Text_for_Comrade.Text))
            {
                MessageBox.Show("Введите сообщение");
                return;
            }

            string messageText = Text_for_Comrade.Text;

            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                using (SqlCommand command = new SqlCommand("SendMessage", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.AddWithValue("@IDChat", currentChatID);
                    command.Parameters.AddWithValue("@IDFellow", currentUserID);
                    command.Parameters.AddWithValue("@MessageText", messageText);

                    await connection.OpenAsync();
                    await command.ExecuteNonQueryAsync();

                    await Dispatcher.InvokeAsync(() =>
                    {
                        Text_for_Comrade.Text = "";
                        LoadChatMessages(currentChatID);
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка отправки сообщения: {ex.Message}");
            }
        }

        // Обработчик нажатия Enter
        private void Text_for_Comrade_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
            {
                SendMessage();
                e.Handled = true;
            }
        }

        // Анимационные обработчики
        private void Enter_to_search_MouseEnter(object sender, MouseEventArgs e)
        {
            AnimateButtonScale(sender, 1.1);
        }

        private void Enter_to_search_MouseLeave(object sender, MouseEventArgs e)
        {
            AnimateButtonScale(sender, 1.0);
        }

        private void Enter_to_Options_MouseEnter(object sender, MouseEventArgs e)
        {
            AnimateButtonScale(sender, 1.1);
        }

        private void Enter_to_Options_MouseLeave(object sender, MouseEventArgs e)
        {
            AnimateButtonScale(sender, 1.0);
        }

        // Универсальный метод анимации
        private void AnimateButtonScale(object sender, double scale)
        {
            if (sender is Button button)
            {
                var template = button.Template;
                Rectangle oval = null;

                if (button.Name == "Enter_to_search")
                {
                    oval = template.FindName("Oval_ID_search", button) as Rectangle;
                }
                else if (button.Name == "Enter_to_Options")
                {
                    oval = template.FindName("Oval_Options", button) as Rectangle;
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

        private void Enter_to_Options_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Функция настроек в разработке");
        }
    }
}