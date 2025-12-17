using Microsoft.Extensions.Configuration;
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
        private string originalUserName;

        public OptionsPage(int userID)
        {
            InitializeComponent();
            currentUserID = userID;

            LoadDefaultAvatarImage();

            Loaded += async (s, e) => await LoadUserDataAsync();
        }

        private void LoadDefaultAvatarImage()
        {
            try
            {
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

                Debug.WriteLine("Файл default_avatar.png не найден в папке Images");
                defaultAvatarImage = new byte[0];
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка загрузки изображения по умолчанию: {ex.Message}");
                defaultAvatarImage = new byte[0];
            }
        }

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
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    UserIdText.Text = reader["UserId"].ToString();
                                    UserNameTextBox.Text = reader["UserName"].ToString();
                                    originalUserName = reader["UserName"].ToString();

                                    if (!reader.IsDBNull(reader.GetOrdinal("Email")))
                                        EmailTextBox.Text = reader["Email"].ToString();

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
                                        StatusComboBox.SelectedIndex = 0;
                                    }

                                    CreatedDateText.Text = reader.GetDateTime(reader.GetOrdinal("CreatedDate"))
                                        .ToString("dd.MM.yyyy HH:mm");

                                    if (!reader.IsDBNull(reader.GetOrdinal("LastLoginDate")))
                                        LastLoginText.Text = reader.GetDateTime(reader.GetOrdinal("LastLoginDate"))
                                            .ToString("dd.MM.yyyy HH:mm");
                                    else
                                        LastLoginText.Text = "Никогда";

                                    if (!reader.IsDBNull(reader.GetOrdinal("ProfileImage")))
                                    {
                                        currentProfileImage = (byte[])reader["ProfileImage"];
                                        LoadProfileImage(currentProfileImage);
                                    }
                                    else
                                    {
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

        private async Task<bool> IsUserNameUniqueAsync(string userName)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                        SELECT COUNT(*) 
                        FROM Users 
                        WHERE UserName = @UserName AND UserId != @UserId";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserName", userName);
                        command.Parameters.AddWithValue("@UserId", currentUserID);

                        int userCount = (int)await command.ExecuteScalarAsync();
                        return userCount == 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка проверки уникальности имени: {ex.Message}");
                return false;
            }
        }

        private void LoadProfileImage(byte[] imageData)
        {
            try
            {
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

        private void LoadDefaultAvatar()
        {
            try
            {
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

                    if (fileInfo.Length > 2 * 1024 * 1024)
                    {
                        MessageBox.Show("Размер изображения не должен превышать 2MB");
                        return;
                    }

                    currentProfileImage = File.ReadAllBytes(filePath);
                    isAvatarChanged = true;

                    LoadProfileImage(currentProfileImage);

                    AvatarSizeInfo.Text = $"Размер файла: {(fileInfo.Length / 1024.0):F1} KB";
                    AvatarSizeInfo.Foreground = System.Windows.Media.Brushes.Green;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка загрузки изображения: {ex.Message}");
                }
            }
        }

        private void RemoveImageBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Установить аватар по умолчанию?",
                "Подтверждение",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                currentProfileImage = defaultAvatarImage;
                isAvatarChanged = true;
                LoadDefaultAvatar();
                AvatarSizeInfo.Text = "Установлен аватар по умолчанию";
                AvatarSizeInfo.Foreground = System.Windows.Media.Brushes.Blue;
            }
        }

        private async void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
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

            string newUserName = UserNameTextBox.Text.Trim();
            bool userNameChanged = newUserName != originalUserName;

            if (userNameChanged)
            {
                bool isUnique = await IsUserNameUniqueAsync(newUserName);
                if (!isUnique)
                {
                    ShowErrorMessage("Это имя пользователя уже занято. Выберите другое.", UserNameTextBox);
                    return;
                }
            }

            bool changePassword = !string.IsNullOrEmpty(NewPasswordBox.Password) ||
                                 !string.IsNullOrEmpty(ConfirmPasswordBox.Password);

            if (changePassword)
            {
                if (!await ValidatePasswordChange())
                    return;
            }

            SaveBtn.IsEnabled = false;
            SaveBtn.Content = "СОХРАНЕНИЕ...";

            try
            {
                bool success = await SaveUserDataAsync();

                if (success && changePassword)
                {
                    await SaveNewPasswordAsync();
                }

                if (success)
                {
                    if (userNameChanged)
                    {
                        originalUserName = newUserName;
                    }

                    ShowSuccessMessage("Данные успешно сохранены!");

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

        private async Task<bool> SaveUserDataAsync()
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

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

                            if (string.IsNullOrWhiteSpace(EmailTextBox.Text))
                                command.Parameters.AddWithValue("@Email", DBNull.Value);
                            else
                                command.Parameters.AddWithValue("@Email", EmailTextBox.Text.Trim());

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
                if (sqlEx.Number == 2601 || sqlEx.Number == 2627)
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

        private async Task<bool> ValidatePasswordChange()
        {
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

            if (NewPasswordBox.Password.Length < 6)
            {
                ShowErrorMessage("Новый пароль должен содержать не менее 6 символов", NewPasswordBox);
                return false;
            }

            if (NewPasswordBox.Password != ConfirmPasswordBox.Password)
            {
                ShowErrorMessage("Новые пароли не совпадают", ConfirmPasswordBox);
                return false;
            }

            if (!await ValidateCurrentPasswordAsync())
            {
                ShowErrorMessage("Текущий пароль неверен", CurrentPasswordBox);
                return false;
            }

            return true;
        }

        private string GetPepper()
        {
            try
            {
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("AppSettings.json", optional: true, reloadOnChange: false)
                    .Build();

                return configuration["Security:Pepper"] ?? "DYNAMIC_RANDOM_PEPPER_KEY_256BIT_CHANGE_IN_PRODUCTION";
            }
            catch
            {
                return "DYNAMIC_RANDOM_PEPPER_KEY_256BIT_CHANGE_IN_PRODUCTION";
            }
        }

        private string HashPasswordWithPepper(string password)
        {
            string pepper = GetPepper();

            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] salt = new byte[32];
                rng.GetBytes(salt);

                using (var pbkdf2 = new Rfc2898DeriveBytes(
                    password + pepper,
                    salt,
                    100000,
                    HashAlgorithmName.SHA512))
                {
                    byte[] hash = pbkdf2.GetBytes(64);

                    byte[] hashBytes = new byte[96];
                    Buffer.BlockCopy(salt, 0, hashBytes, 0, 32);
                    Buffer.BlockCopy(hash, 0, hashBytes, 32, 64);

                    return Convert.ToBase64String(hashBytes);
                }
            }
        }

        private bool VerifyPasswordWithPepper(string password, string storedHash)
        {
            try
            {
                byte[] hashBytes = Convert.FromBase64String(storedHash);

                byte[] salt = new byte[32];
                Buffer.BlockCopy(hashBytes, 0, salt, 0, 32);

                string pepper = GetPepper();

                using (var pbkdf2 = new Rfc2898DeriveBytes(
                    password + pepper,
                    salt,
                    100000,
                    HashAlgorithmName.SHA512))
                {
                    byte[] testHash = pbkdf2.GetBytes(64);

                    for (int i = 0; i < 64; i++)
                    {
                        if (testHash[i] != hashBytes[i + 32])
                            return false;
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

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

                            return VerifyPasswordWithPepper(CurrentPasswordBox.Password, storedHash);
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
                        string newHash = HashPasswordWithPepper(NewPasswordBox.Password);
                        command.Parameters.AddWithValue("@PasswordHash", newHash);

                        await command.ExecuteNonQueryAsync();

                        CurrentPasswordBox.Password = "";
                        NewPasswordBox.Password = "";
                        ConfirmPasswordBox.Password = "";

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

        private void ShowErrorMessage(string message, Control focusControl = null)
        {
            SaveMessage.Text = message;
            SaveMessage.Foreground = System.Windows.Media.Brushes.Red;
            SaveMessage.Visibility = Visibility.Visible;

            if (focusControl != null)
                focusControl.Focus();
        }

        private void ShowSuccessMessage(string message)
        {
            SaveMessage.Text = message;
            SaveMessage.Foreground = System.Windows.Media.Brushes.Green;
            SaveMessage.Visibility = Visibility.Visible;
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService.CanGoBack)
                NavigationService.GoBack();
        }
    }
}