using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace ComradeMIN
{
    public class DatabaseService
    {
        private string connectionString;

        public DatabaseService()
        {
            // Попробуйте разные варианты строк подключения
            // Узнайте IP сервера из Radmin VPN
            string serverIP = "26.19.50.66"; // Замените на реальный IP

            // Вариант 1: С таймаутом и протоколом TCP
            connectionString = $"Server=tcp:{serverIP}\\SQLEXPRESS,1433;" +
                              $"Database=Void;" +
                              $"User Id=VoidUser;" +
                              $"Password=VoidUser123;" +
                              $"Connection Timeout=30;" +
                              $"TrustServerCertificate=True;";
        }

        // Метод для тестирования подключения
        public async Task<bool> TestConnection()
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    // Простой запрос для проверки
                    SqlCommand cmd = new SqlCommand("SELECT 1", connection);
                    var result = await cmd.ExecuteScalarAsync();

                    return (int)result == 1;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка подключения: {ex.Message}");
                return false;
            }
        }

        public async Task<int?> ValidateUserAndGetId(string username, string password)
        {
            try
            {
                Debug.WriteLine($"Попытка подключения: {connectionString}");

                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    Debug.WriteLine("Подключение к БД успешно!");

                    string query = @"
                SELECT UserId, PasswordHash 
                FROM Users 
                WHERE UserName = @UserName";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserName", username);

                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                string storedHash = reader.GetString(1);
                                string inputHash = HashPassword(password);

                                // Сначала проверяем новым методом (с солью)
                                if (storedHash == inputHash)
                                {
                                    return reader.GetInt32(0);
                                }
                                else
                                {
                                    // Если не совпадает, пробуем старым методом (без соли)
                                    string oldHash = HashPasswordWithoutSalt(password);
                                    if (storedHash == oldHash)
                                    {
                                        int userId = reader.GetInt32(0);
                                        // Автоматически мигрируем на новый формат
                                        await UpdatePasswordHash(userId, HashPassword(password));
                                        return userId;
                                    }
                                    else
                                    {
                                        Debug.WriteLine("Пароль не совпадает");
                                    }
                                }
                            }
                            else
                            {
                                Debug.WriteLine("Пользователь не найден");
                            }
                            return null;
                        }
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                Debug.WriteLine($"SQL Ошибка: {sqlEx.Message}");
                Debug.WriteLine($"Номер ошибки: {sqlEx.Number}");
                Debug.WriteLine($"Источник: {sqlEx.Source}");
                Debug.WriteLine($"Стек: {sqlEx.StackTrace}");
                MessageBox.Show($"Ошибка при входе: {sqlEx.Message}\n\nПроверьте подключение к VPN и настройки сети.");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Общая ошибка: {ex.Message}");
                MessageBox.Show($"Ошибка при входе: {ex.Message}");
                return null;
            }
        }

        private async Task UpdatePasswordHash(int userId, string newHash)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    string query = "UPDATE Users SET PasswordHash = @NewHash WHERE UserId = @UserId";
                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@NewHash", newHash);
                        command.Parameters.AddWithValue("@UserId", userId);
                        await command.ExecuteNonQueryAsync();
                        Debug.WriteLine($"Пароль для пользователя {userId} обновлен на новый формат");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка обновления пароля: {ex.Message}");
                // Не прерываем выполнение - пользователь уже вошел
            }
        }

        public async Task<int?> RegisterUserAndGetId(string username, string password)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    // Проверяем, не существует ли уже пользователь с таким логином
                    string checkUserQuery = "SELECT COUNT(*) FROM Users WHERE UserName = @UserName";
                    using (SqlCommand checkCommand = new SqlCommand(checkUserQuery, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@UserName", username);
                        int userCount = (int)await checkCommand.ExecuteScalarAsync();

                        if (userCount > 0)
                        {
                            MessageBox.Show("Пользователь с таким логином уже существует");
                            return null;
                        }
                    }

                    // Создаем нового пользователя с новым форматом хэша
                    string insertUserQuery = @"
                    INSERT INTO Users (UserName, PasswordHash, CreatedDate) 
                    VALUES (@UserName, @PasswordHash, GETDATE());
                    SELECT SCOPE_IDENTITY();"; // Получаем ID нового пользователя

                    using (SqlCommand insertCommand = new SqlCommand(insertUserQuery, connection))
                    {
                        insertCommand.Parameters.AddWithValue("@UserName", username);
                        string passwordHash = HashPassword(password); // Используем новый метод
                        insertCommand.Parameters.AddWithValue("@PasswordHash", passwordHash);

                        var newUserId = await insertCommand.ExecuteScalarAsync();

                        if (newUserId != null)
                        {
                            MessageBox.Show("Регистрация прошла успешно!");
                            return Convert.ToInt32(newUserId);
                        }
                        else
                        {
                            MessageBox.Show("Ошибка при создании пользователя");
                            return null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при регистрации: {ex.Message}");
                return null;
            }
        }

        // НОВЫЙ МЕТОД: хэширование с солью
        private string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                // Соль для усиления безопасности
                string salt = "ComradeMIN_2025"; // TODO: Вынести в конфигурацию
                byte[] saltBytes = Encoding.UTF8.GetBytes(salt);
                byte[] passwordBytes = Encoding.UTF8.GetBytes(password);

                // Комбинируем соль и пароль
                byte[] combinedBytes = new byte[saltBytes.Length + passwordBytes.Length];
                Buffer.BlockCopy(saltBytes, 0, combinedBytes, 0, saltBytes.Length);
                Buffer.BlockCopy(passwordBytes, 0, combinedBytes, saltBytes.Length, passwordBytes.Length);

                byte[] hash = sha256.ComputeHash(combinedBytes);
                return Convert.ToBase64String(hash);
            }
        }

        // СТАРЫЙ МЕТОД: для обратной совместимости
        private string HashPasswordWithoutSalt(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(password);
                var hash = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }
    }
}