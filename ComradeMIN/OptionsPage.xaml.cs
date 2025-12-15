using Microsoft.Win32;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace ComradeMIN
{
    public partial class OptionsPage : Page
    {
        private string connectionString = "Server=tcp:26.19.50.66\\SQLEXPRESS,1433;" +
                                          "Database=Void;" +
                                          "User Id=VoidUser;" +
                                          "Password=VoidUser123;" +
                                          "Connection Timeout=30;" +
                                          "TrustServerCertificate=True;";

        private int currentUserID;
        private byte[] currentProfileImage;
        private bool isAvatarChanged = false;
        private byte[] defaultAvatarImage;
        private string originalUserName; // Сохраняем оригинальное имя для проверки

        public OptionsPage(int userID)
        {
            InitializeComponent();
            currentUserID = userID;

            // Загружаем изображение по умолчанию из папки Images
            LoadDefaultAvatarImage();

            // Загружаем данные при инициализации
            Loaded += async (s, e) => await LoadUserDataAsync();
        }

        // Загрузка изображения по умолчанию из папки Images
        private void LoadDefaultAvatarImage()
        {
            try
            {
                // Определяем возможные пути к файлу
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string[] possiblePaths = {
                    Path.Combine(baseDirectory, "Images", "default_avatar.png"),
                    Path.Combine(baseDirectory, "default_avatar.png"),
                    "Images/default_avatar.png",
                    "default_avatar.png"
                };

                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        defaultAvatarImage = File.ReadAllBytes(path);
                        Debug.WriteLine($"Загружено изображение по умолчанию: {path}");
                        return;
                    }
                }

                // Если файл не найден, используем пустой массив
                Debug.WriteLine("Файл default_avatar.png не найден в папке Images");
                defaultAvatarImage = new byte[0];
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка загрузки изображения по умолчанию: {ex.Message}");
                defaultAvatarImage = new byte[0];
            }
        }

        // Асинхронная загрузка данных пользователя
        private async Task LoadUserDataAsync()
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                        SELECT 
                            UserId, 
                            UserName, 
                            Status, 
                            ProfileImage, 
                            Email, 
                            CreatedDate, 
                            LastLoginDate 
                        FROM Users 
                        WHERE UserId = @UserId";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserId", currentUserID);

                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                // Заполняем поля данными в UI потоке
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    // Базовые данные
                                    UserIdText.Text = reader["UserId"].ToString();
                                    UserNameTextBox.Text = reader["UserName"].ToString();
                                    originalUserName = reader["UserName"].ToString(); // Сохраняем оригинальное имя

                                    // Email (может быть NULL)
                                    if (!reader.IsDBNull(reader.GetOrdinal("Email")))
                                        EmailTextBox.Text = reader["Email"].ToString();

                                    // Статус
                                    if (!reader.IsDBNull(reader.GetOrdinal("Status")))
                                    {
                                        string status = reader["Status"].ToString();
                                        foreach (ComboBoxItem item in StatusComboBox.Items)
                                        {
                                            if (item.Tag?.ToString() == status)
                                            {
                                                StatusComboBox.SelectedItem = item;
                                                break;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        StatusComboBox.SelectedIndex = 0; // "В сети" по умолчанию
                                    }

                                    // Даты
                                    CreatedDateText.Text = reader.GetDateTime(reader.GetOrdinal("CreatedDate"))
                                        .ToString("dd.MM.yyyy HH:mm");

                                    if (!reader.IsDBNull(reader.GetOrdinal("LastLoginDate")))
                                        LastLoginText.Text = reader.GetDateTime(reader.GetOrdinal("LastLoginDate"))
                                            .ToString("dd.MM.yyyy HH:mm");
                                    else
                                        LastLoginText.Text = "Никогда";

                                    // Аватар
                                    if (!reader.IsDBNull(reader.GetOrdinal("ProfileImage")))
                                    {
                                        currentProfileImage = (byte[])reader["ProfileImage"];
                                        LoadProfileImage(currentProfileImage);
                                    }
                                    else
                                    {
                                        // Используем изображение по умолчанию
                                        currentProfileImage = defaultAvatarImage;
                                        LoadDefaultAvatar();
                                    }
                                });
                            }
                            else
                            {
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    MessageBox.Show("Пользователь не найден");
                                    NavigationService.GoBack();
                                });
                            }
                        }
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Ошибка базы данных: {sqlEx.Message}\n\nПроверьте подключение к VPN.");
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Ошибка загрузки данных: {ex.Message}");
                });
            }
        }

        // Проверка уникальности имени пользователя
        private async Task<bool> IsUserNameUniqueAsync(string userName)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    // Проверяем, существует ли уже пользователь с таким именем (кроме текущего)
                    string query = @"
                        SELECT COUNT(*) 
                        FROM Users 
                        WHERE UserName = @UserName AND UserId != @UserId";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserName", userName);
                        command.Parameters.AddWithValue("@UserId", currentUserID);

                        int userCount = (int)await command.ExecuteScalarAsync();
                        return userCount == 0; // Если 0, то имя уникально
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка проверки уникальности имени: {ex.Message}");
                return false; // В случае ошибки считаем, что имя не уникально
            }
        }

        // Загрузка изображения профиля
        private void LoadProfileImage(byte[] imageData)
        {
            try
            {
                // Проверяем, есть ли данные
                if (imageData == null || imageData.Length == 0)
                {
                    LoadDefaultAvatar();
                    return;
                }

                using (MemoryStream stream = new MemoryStream(imageData))
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = stream;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();

                    ProfileImageControl.Source = bitmap;
                    DefaultAvatarText.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка загрузки изображения: {ex.Message}");
                LoadDefaultAvatar();
            }
        }

        // Загрузка стандартного аватара
        private void LoadDefaultAvatar()
        {
            try
            {
                // Пробуем загрузить изображение по умолчанию
                if (defaultAvatarImage != null && defaultAvatarImage.Length > 0)
                {
                    using (MemoryStream stream = new MemoryStream(defaultAvatarImage))
                    {
                        BitmapImage bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = stream;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();

                        ProfileImageControl.Source = bitmap;
                        DefaultAvatarText.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    // Если нет изображения по умолчанию, показываем иконку
                    ProfileImageControl.Source = null;
                    DefaultAvatarText.Visibility = Visibility.Visible;
                }
            }
            catch
            {
                ProfileImageControl.Source = null;
                DefaultAvatarText.Visibility = Visibility.Visible;
            }
        }

        // Кнопка смены аватара
        private void ChangeImageBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Title = "Выберите изображение профиля",
                Filter = "Изображения (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string filePath = openFileDialog.FileName;
                    FileInfo fileInfo = new FileInfo(filePath);

                    // Проверка размера файла (максимум 2MB)
                    if (fileInfo.Length > 2 * 1024 * 1024)
                    {
                        MessageBox.Show("Размер изображения не должен превышать 2MB");
                        return;
                    }

                    // Загружаем изображение в память
                    currentProfileImage = File.ReadAllBytes(filePath);
                    isAvatarChanged = true;

                    // Показываем preview
                    LoadProfileImage(currentProfileImage);

                    // Показываем информацию о размере
                    AvatarSizeInfo.Text = $"Размер файла: {(fileInfo.Length / 1024.0):F1} KB";
                    AvatarSizeInfo.Foreground = System.Windows.Media.Brushes.Green;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка загрузки изображения: {ex.Message}");
                }
            }
        }

        // Кнопка удаления аватара
        private void RemoveImageBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Установить аватар по умолчанию?",
                "Подтверждение",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Устанавливаем изображение по умолчанию
                currentProfileImage = defaultAvatarImage;
                isAvatarChanged = true;
                LoadDefaultAvatar();
                AvatarSizeInfo.Text = "Установлен аватар по умолчанию";
                AvatarSizeInfo.Foreground = System.Windows.Media.Brushes.Blue;
            }
        }

        // Основное сохранение данных
        private async void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            // Валидация данных
            if (string.IsNullOrWhiteSpace(UserNameTextBox.Text))
            {
                ShowErrorMessage("Введите имя пользователя", UserNameTextBox);
                return;
            }

            if (UserNameTextBox.Text.Length < 3)
            {
                ShowErrorMessage("Имя пользователя должно содержать не менее 3 символов", UserNameTextBox);
                return;
            }

            // Проверяем, изменилось ли имя пользователя
            string newUserName = UserNameTextBox.Text.Trim();
            bool userNameChanged = newUserName != originalUserName;

            if (userNameChanged)
            {
                // Проверяем уникальность нового имени
                bool isUnique = await IsUserNameUniqueAsync(newUserName);
                if (!isUnique)
                {
                    ShowErrorMessage("Это имя пользователя уже занято. Выберите другое.", UserNameTextBox);
                    return;
                }
            }

            // Проверка пароля, если пользователь пытается его изменить
            bool changePassword = !string.IsNullOrEmpty(NewPasswordBox.Password) ||
                                 !string.IsNullOrEmpty(ConfirmPasswordBox.Password);

            if (changePassword)
            {
                if (!await ValidatePasswordChange())
                    return;
            }

            // Отключаем кнопку на время сохранения
            SaveBtn.IsEnabled = false;
            SaveBtn.Content = "СОХРАНЕНИЕ...";

            try
            {
                // Сохраняем основные данные
                bool success = await SaveUserDataAsync();

                if (success && changePassword)
                {
                    // Сохраняем новый пароль
                    await SaveNewPasswordAsync();
                }

                if (success)
                {
                    // Обновляем оригинальное имя после успешного сохранения
                    if (userNameChanged)
                    {
                        originalUserName = newUserName;
                    }

                    ShowSuccessMessage("Данные успешно сохранены!");

                    // Возвращаемся через 1.5 секунды
                    await Task.Delay(1500);

                    if (NavigationService.CanGoBack)
                        NavigationService.GoBack();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения данных: {ex.Message}");
            }
            finally
            {
                SaveBtn.IsEnabled = true;
                SaveBtn.Content = "Сохранить";
            }
        }

        // Сохранение основных данных пользователя
        private async Task<bool> SaveUserDataAsync()
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    // Проверяем, изменился ли аватар
                    if (isAvatarChanged)
                    {
                        string query = @"
                            UPDATE Users 
                            SET UserName = @UserName, 
                                Status = @Status, 
                                Email = @Email,
                                ProfileImage = @ProfileImage
                            WHERE UserId = @UserId";

                        using (SqlCommand command = new SqlCommand(query, connection))
                        {
                            command.Parameters.AddWithValue("@UserId", currentUserID);
                            command.Parameters.AddWithValue("@UserName", UserNameTextBox.Text.Trim());

                            var selectedStatus = (ComboBoxItem)StatusComboBox.SelectedItem;
                            command.Parameters.AddWithValue("@Status", selectedStatus.Tag.ToString());

                            // Email (может быть NULL)
                            if (string.IsNullOrWhiteSpace(EmailTextBox.Text))
                                command.Parameters.AddWithValue("@Email", DBNull.Value);
                            else
                                command.Parameters.AddWithValue("@Email", EmailTextBox.Text.Trim());

                            // Аватар
                            if (currentProfileImage != null && currentProfileImage.Length > 0)
                                command.Parameters.AddWithValue("@ProfileImage", currentProfileImage);
                            else
                                command.Parameters.AddWithValue("@ProfileImage", DBNull.Value);

                            int rowsAffected = await command.ExecuteNonQueryAsync();
                            return rowsAffected > 0;
                        }
                    }
                    else
                    {
                        // Аватар не меняли, обновляем только остальные поля
                        string query = @"
                            UPDATE Users 
                            SET UserName = @UserName, 
                                Status = @Status, 
                                Email = @Email
                            WHERE UserId = @UserId";

                        using (SqlCommand command = new SqlCommand(query, connection))
                        {
                            command.Parameters.AddWithValue("@UserId", currentUserID);
                            command.Parameters.AddWithValue("@UserName", UserNameTextBox.Text.Trim());

                            var selectedStatus = (ComboBoxItem)StatusComboBox.SelectedItem;
                            command.Parameters.AddWithValue("@Status", selectedStatus.Tag.ToString());

                            // Email (может быть NULL)
                            if (string.IsNullOrWhiteSpace(EmailTextBox.Text))
                                command.Parameters.AddWithValue("@Email", DBNull.Value);
                            else
                                command.Parameters.AddWithValue("@Email", EmailTextBox.Text.Trim());

                            int rowsAffected = await command.ExecuteNonQueryAsync();
                            return rowsAffected > 0;
                        }
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                // Проверяем, не связано ли исключение с нарушением уникальности
                if (sqlEx.Number == 2601 || sqlEx.Number == 2627) // Ошибки нарушения уникальности
                {
                    MessageBox.Show("Это имя пользователя уже занято. Выберите другое.",
                        "Ошибка уникальности",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    Debug.WriteLine($"Ошибка сохранения данных: {sqlEx.Message}");
                    throw;
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка сохранения данных: {ex.Message}");
                throw;
            }
        }

        // Валидация смены пароля
        private async Task<bool> ValidatePasswordChange()
        {
            // Проверяем, что все поля заполнены
            if (string.IsNullOrEmpty(CurrentPasswordBox.Password))
            {
                ShowErrorMessage("Введите текущий пароль", CurrentPasswordBox);
                return false;
            }

            if (string.IsNullOrEmpty(NewPasswordBox.Password))
            {
                ShowErrorMessage("Введите новый пароль", NewPasswordBox);
                return false;
            }

            if (string.IsNullOrEmpty(ConfirmPasswordBox.Password))
            {
                ShowErrorMessage("Подтвердите новый пароль", ConfirmPasswordBox);
                return false;
            }

            // Проверяем длину нового пароля
            if (NewPasswordBox.Password.Length < 6)
            {
                ShowErrorMessage("Новый пароль должен содержать не менее 6 символов", NewPasswordBox);
                return false;
            }

            // Проверяем совпадение паролей
            if (NewPasswordBox.Password != ConfirmPasswordBox.Password)
            {
                ShowErrorMessage("Новые пароли не совпадают", ConfirmPasswordBox);
                return false;
            }

            // Проверяем текущий пароль
            if (!await ValidateCurrentPasswordAsync())
            {
                ShowErrorMessage("Текущий пароль неверен", CurrentPasswordBox);
                return false;
            }

            return true;
        }

        // Проверка текущего пароля
        private async Task<bool> ValidateCurrentPasswordAsync()
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                        SELECT PasswordHash 
                        FROM Users 
                        WHERE UserId = @UserId";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserId", currentUserID);
                        var result = await command.ExecuteScalarAsync();

                        if (result != null && result != DBNull.Value)
                        {
                            string storedHash = result.ToString();
                            string inputHash = HashPassword(CurrentPasswordBox.Password);

                            // Сначала проверяем новым методом (с солью)
                            if (storedHash == inputHash)
                                return true;

                            // Пробуем старым методом (без соли) для обратной совместимости
                            string oldHash = HashPasswordWithoutSalt(CurrentPasswordBox.Password);
                            return storedHash == oldHash;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка проверки пароля: {ex.Message}");
            }

            return false;
        }

        // Сохранение нового пароля
        private async Task SaveNewPasswordAsync()
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                        UPDATE Users 
                        SET PasswordHash = @PasswordHash 
                        WHERE UserId = @UserId";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserId", currentUserID);
                        string newHash = HashPassword(NewPasswordBox.Password);
                        command.Parameters.AddWithValue("@PasswordHash", newHash);

                        await command.ExecuteNonQueryAsync();

                        // Очищаем поля паролей
                        CurrentPasswordBox.Password = "";
                        NewPasswordBox.Password = "";
                        ConfirmPasswordBox.Password = "";

                        // Показываем сообщение
                        PasswordChangeMessage.Text = "Пароль успешно изменен";
                        PasswordChangeMessage.Foreground = System.Windows.Media.Brushes.Green;
                        PasswordChangeMessage.Visibility = Visibility.Visible;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка смены пароля: {ex.Message}");
                throw;
            }
        }

        // Хэширование пароля (новый метод с солью)
        private string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                string salt = "ComradeMIN_2025";
                byte[] saltBytes = Encoding.UTF8.GetBytes(salt);
                byte[] passwordBytes = Encoding.UTF8.GetBytes(password);

                byte[] combinedBytes = new byte[saltBytes.Length + passwordBytes.Length];
                Buffer.BlockCopy(saltBytes, 0, combinedBytes, 0, saltBytes.Length);
                Buffer.BlockCopy(passwordBytes, 0, combinedBytes, saltBytes.Length, passwordBytes.Length);

                byte[] hash = sha256.ComputeHash(combinedBytes);
                return Convert.ToBase64String(hash);
            }
        }

        // Хэширование пароля (старый метод без соли - для обратной совместимости)
        private string HashPasswordWithoutSalt(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(password);
                var hash = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }

        // Показать сообщение об ошибке
        private void ShowErrorMessage(string message, Control focusControl = null)
        {
            SaveMessage.Text = message;
            SaveMessage.Foreground = System.Windows.Media.Brushes.Red;
            SaveMessage.Visibility = Visibility.Visible;

            if (focusControl != null)
                focusControl.Focus();
        }

        // Показать сообщение об успехе
        private void ShowSuccessMessage(string message)
        {
            SaveMessage.Text = message;
            SaveMessage.Foreground = System.Windows.Media.Brushes.Green;
            SaveMessage.Visibility = Visibility.Visible;
        }

        // Кнопка отмены
        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService.CanGoBack)
                NavigationService.GoBack();
        }
    }
}