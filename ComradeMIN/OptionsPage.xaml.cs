using Microsoft.Win32;
using System;
using System.Data.SqlClient;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace ComradeMIN
{
    public partial class OptionsPage : Page
    {
        private string connectionString = "Data Source=DESKTOP-LK756J0\\SQLEXPRESS;Initial Catalog=Void;Integrated Security=True";
        private int currentUserID;
        private byte[] currentProfileImage;

        public OptionsPage(int userID)
        {
            InitializeComponent();
            currentUserID = userID;
            LoadUserData();
        }

        private void LoadUserData()
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    string query = @"
                        SELECT UserId, UserName, Status, ProfileImage, Email, CreatedDate, LastLoginDate 
                        FROM Users 
                        WHERE UserId = @UserId";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserId", currentUserID);

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                // Заполняем поля данными
                                UserIdTextBox.Text = reader["UserId"].ToString();
                                UserNameTextBox.Text = reader["UserName"].ToString();
                                EmailTextBox.Text = reader.IsDBNull(reader.GetOrdinal("Email")) ? "" : reader["Email"].ToString();

                                // Статус
                                string status = reader["Status"].ToString();
                                foreach (ComboBoxItem item in StatusComboBox.Items)
                                {
                                    if (item.Content.ToString() == status)
                                    {
                                        StatusComboBox.SelectedItem = item;
                                        break;
                                    }
                                }

                                // Даты
                                CreatedDateText.Text = Convert.ToDateTime(reader["CreatedDate"]).ToString("dd.MM.yyyy HH:mm");
                                LastLoginText.Text = reader.IsDBNull(reader.GetOrdinal("LastLoginDate")) ?
                                    "Никогда" : Convert.ToDateTime(reader["LastLoginDate"]).ToString("dd.MM.yyyy HH:mm");

                                // Аватар
                                if (!reader.IsDBNull(reader.GetOrdinal("ProfileImage")))
                                {
                                    currentProfileImage = (byte[])reader["ProfileImage"];
                                    LoadProfileImage(currentProfileImage);
                                }
                                else
                                {
                                    LoadDefaultAvatar();
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}");
            }
        }

        private void LoadProfileImage(byte[] imageData)
        {
            try
            {
                using (MemoryStream stream = new MemoryStream(imageData))
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = stream;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    ProfileImageControl.Source = bitmap;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки изображения: {ex.Message}");
                LoadDefaultAvatar();
            }
        }

        private void LoadDefaultAvatar()
        {
            // Просто очищаем изображение
            ProfileImageControl.Source = null;
        }

        private void ChangeImageBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Title = "Выберите изображение профиля",
                Filter = "Изображения (*.jpg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp",
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

                    // Показываем preview
                    LoadProfileImage(currentProfileImage);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка загрузки изображения: {ex.Message}");
                }
            }
        }

        private void RemoveImageBtn_Click(object sender, RoutedEventArgs e)
        {
            currentProfileImage = null;
            LoadDefaultAvatar();
        }

        private async void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(UserNameTextBox.Text))
            {
                MessageBox.Show("Введите имя пользователя");
                return;
            }

            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                        UPDATE Users 
                        SET UserName = @UserName, 
                            Status = @Status, 
                            ProfileImage = @ProfileImage,
                            Email = @Email
                        WHERE UserId = @UserId";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserId", currentUserID);
                        command.Parameters.AddWithValue("@UserName", UserNameTextBox.Text.Trim());
                        command.Parameters.AddWithValue("@Status", ((ComboBoxItem)StatusComboBox.SelectedItem).Content.ToString());
                        command.Parameters.AddWithValue("@Email", string.IsNullOrWhiteSpace(EmailTextBox.Text) ? (object)DBNull.Value : EmailTextBox.Text.Trim());

                        if (currentProfileImage != null)
                            command.Parameters.AddWithValue("@ProfileImage", currentProfileImage);
                        else
                            command.Parameters.AddWithValue("@ProfileImage", DBNull.Value);

                        int rowsAffected = await command.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                        {
                            MessageBox.Show("Данные успешно сохранены!");

                            // Возвращаемся на предыдущую страницу
                            if (NavigationService.CanGoBack)
                                NavigationService.GoBack();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения данных: {ex.Message}");
            }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService.CanGoBack)
                NavigationService.GoBack();
        }
    }
}