using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace ComradeMIN
{
    public class DatabaseService : IDisposable
    {
        private readonly string _connectionString;
        private readonly string _pepper;
        private bool _disposed = false;

        public DatabaseService()
        {
            try
            {
                // Загрузка конфигурации из AppSettings.json
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("AppSettings.json", optional: true, reloadOnChange: false)
                    .Build();

                _connectionString = configuration.GetConnectionString("VoidConnection");
                _pepper = configuration["Security:Pepper"] ?? "DEFAULT_PEPPER_CHANGE_ME";

                if (string.IsNullOrEmpty(_connectionString))
                {
                    throw new InvalidOperationException("Connection string not found in configuration");
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Failed to initialize DatabaseService: {ex.Message}", ex);
                throw;
            }
        }

        public async Task<bool> TestConnection()
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    using (var cmd = new SqlCommand("SELECT 1", connection))
                    {
                        var result = await cmd.ExecuteScalarAsync();
                        return result != null && Convert.ToInt32(result) == 1;
                    }
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
                LoggingService.LogDebug($"Attempting login for user: {username}");

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    const string query = @"
                        SELECT UserId, PasswordHash 
                        FROM Users 
                        WHERE UserName = @UserName";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserName", username);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                string storedHash = reader.GetString(1);

                                // Проверяем хэш с перцем
                                if (VerifyPasswordHash(password, storedHash))
                                {
                                    int userId = reader.GetInt32(0);
                                    LoggingService.LogInfo($"User {username} authenticated successfully");

                                    // Обновляем время последнего входа
                                    await UpdateLastLoginAsync(userId);

                                    return userId;
                                }
                                else
                                {
                                    LoggingService.LogWarning($"Failed login attempt for user: {username}");
                                }
                            }
                            else
                            {
                                LoggingService.LogWarning($"User not found: {username}");
                            }
                            return null;
                        }
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                LoggingService.LogError($"SQL error during login: {sqlEx.Message}", sqlEx);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ShowSqlErrorMessage(sqlEx);
                });
                return null;
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"General error during login: {ex.Message}", ex);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Ошибка при входе: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
                return null;
            }
        }

        private async Task UpdateLastLoginAsync(int userId)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    const string query = @"
                        UPDATE Users 
                        SET LastLoginDate = GETUTCDATE() 
                        WHERE UserId = @UserId";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);
                        await command.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Failed to update last login: {ex.Message}", ex);
            }
        }

        public async Task<int?> RegisterUserAndGetId(string username, string password)
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Проверяем уникальность имени пользователя
                    const string checkQuery = @"
                        SELECT COUNT(*) 
                        FROM Users 
                        WHERE UserName = @UserName";

                    using (var checkCommand = new SqlCommand(checkQuery, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@UserName", username);
                        int userCount = (int)await checkCommand.ExecuteScalarAsync();

                        if (userCount > 0)
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                MessageBox.Show("Пользователь с таким логином уже существует",
                                    "Ошибка регистрации", MessageBoxButton.OK, MessageBoxImage.Warning);
                            });
                            return null;
                        }
                    }

                    // Создаем пользователя с безопасным хэшем
                    const string insertQuery = @"
                        INSERT INTO Users (UserName, PasswordHash, CreatedDate, LastLoginDate) 
                        VALUES (@UserName, @PasswordHash, GETUTCDATE(), GETUTCDATE());
                        SELECT SCOPE_IDENTITY();";

                    using (var insertCommand = new SqlCommand(insertQuery, connection))
                    {
                        insertCommand.Parameters.AddWithValue("@UserName", username);

                        // Используем безопасное хэширование с перцем
                        string passwordHash = HashPasswordWithPepper(password);
                        insertCommand.Parameters.AddWithValue("@PasswordHash", passwordHash);

                        var newUserId = await insertCommand.ExecuteScalarAsync();

                        if (newUserId != null)
                        {
                            int userId = Convert.ToInt32(newUserId);
                            LoggingService.LogInfo($"New user registered: {username} (ID: {userId})");

                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                MessageBox.Show("Регистрация прошла успешно!", "Успех",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                            });

                            return userId;
                        }
                        else
                        {
                            throw new InvalidOperationException("Failed to get new user ID");
                        }
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                LoggingService.LogError($"SQL error during registration: {sqlEx.Message}", sqlEx);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ShowSqlErrorMessage(sqlEx);
                });
                return null;
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"General error during registration: {ex.Message}", ex);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Ошибка при регистрации: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
                return null;
            }
        }

        // Безопасное хэширование с перцем
        private string HashPasswordWithPepper(string password)
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                // Генерируем уникальную соль для каждого пользователя
                byte[] salt = new byte[32];
                rng.GetBytes(salt);

                // Создаем производный ключ с солью и перцем
                using (var pbkdf2 = new Rfc2898DeriveBytes(
                    password + _pepper,
                    salt,
                    100000,
                    HashAlgorithmName.SHA512))
                {
                    byte[] hash = pbkdf2.GetBytes(64);

                    // Сохраняем соль и хэш вместе
                    byte[] hashBytes = new byte[96]; // 32 (соль) + 64 (хэш)
                    Buffer.BlockCopy(salt, 0, hashBytes, 0, 32);
                    Buffer.BlockCopy(hash, 0, hashBytes, 32, 64);

                    return Convert.ToBase64String(hashBytes);
                }
            }
        }

        // Проверка пароля с перцем
        private bool VerifyPasswordHash(string password, string storedHash)
        {
            try
            {
                byte[] hashBytes = Convert.FromBase64String(storedHash);

                if (hashBytes.Length != 96)
                {
                    // Старый формат хэша (для обратной совместимости)
                    return VerifyLegacyHash(password, storedHash);
                }

                // Извлекаем соль
                byte[] salt = new byte[32];
                Buffer.BlockCopy(hashBytes, 0, salt, 0, 32);

                // Вычисляем хэш введенного пароля
                using (var pbkdf2 = new Rfc2898DeriveBytes(
                    password + _pepper,
                    salt,
                    100000,
                    HashAlgorithmName.SHA512))
                {
                    byte[] testHash = pbkdf2.GetBytes(64);

                    // Сравниваем хэши
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

        // Для обратной совместимости со старыми хэшами
        private bool VerifyLegacyHash(string password, string storedHash)
        {
            try
            {
                using (var sha256 = SHA256.Create())
                {
                    // Проверяем старый метод (без соли и перца)
                    byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
                    byte[] hash = sha256.ComputeHash(passwordBytes);
                    string computedHash = Convert.ToBase64String(hash);

                    if (computedHash == storedHash)
                    {
                        // Миграция на новый формат при следующем входе
                        return true;
                    }

                    // Проверяем старый метод с солью (если использовался)
                    string salt = "ComradeMIN_2025";
                    byte[] combinedBytes = new byte[salt.Length + passwordBytes.Length];
                    Encoding.UTF8.GetBytes(salt).CopyTo(combinedBytes, 0);
                    passwordBytes.CopyTo(combinedBytes, salt.Length);

                    hash = sha256.ComputeHash(combinedBytes);
                    computedHash = Convert.ToBase64String(hash);

                    return computedHash == storedHash;
                }
            }
            catch
            {
                return false;
            }
        }

        private void ShowSqlErrorMessage(SqlException sqlEx)
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
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                // Освобождение ресурсов, если есть
            }
        }

        ~DatabaseService()
        {
            Dispose(false);
        }
    }
}