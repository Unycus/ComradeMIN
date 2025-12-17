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

                                if (VerifyPasswordHash(password, storedHash))
                                {
                                    int userId = reader.GetInt32(0);
                                    LoggingService.LogInfo($"User {username} authenticated successfully");

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
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

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

                    byte[] defaultAvatar = LoadDefaultAvatarImage();

                    string insertUserQuery = @"
                INSERT INTO Users (UserName, PasswordHash, ProfileImage, CreatedDate, LastLoginDate) 
                VALUES (@UserName, @PasswordHash, @ProfileImage, GETUTCDATE(), GETUTCDATE());
                SELECT SCOPE_IDENTITY();";

                    using (SqlCommand insertCommand = new SqlCommand(insertUserQuery, connection))
                    {
                        insertCommand.Parameters.AddWithValue("@UserName", username);
                        string passwordHash = HashPasswordWithPepper(password);
                        insertCommand.Parameters.AddWithValue("@PasswordHash", passwordHash);

                        if (defaultAvatar != null && defaultAvatar.Length > 0)
                            insertCommand.Parameters.AddWithValue("@ProfileImage", defaultAvatar);
                        else
                            insertCommand.Parameters.AddWithValue("@ProfileImage", DBNull.Value);

                        var newUserId = await insertCommand.ExecuteScalarAsync();

                        if (newUserId != null)
                        {
                            LoggingService.LogInfo($"Новый пользователь зарегистрирован: {username} (ID: {newUserId})");

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
        private byte[] LoadDefaultAvatarImage()
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
                        LoggingService.LogDebug($"Загружен аватар по умолчанию: {path}");
                        return File.ReadAllBytes(path);
                    }
                }

                LoggingService.LogWarning("Файл default_avatar.png не найден в папке Images");
                return new byte[0];
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Ошибка загрузки аватара по умолчанию: {ex.Message}", ex);
                return new byte[0];
            }
        }

        private string HashPasswordWithPepper(string password)
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] salt = new byte[32];
                rng.GetBytes(salt);

                using (var pbkdf2 = new Rfc2898DeriveBytes(
                    password + _pepper,
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

        private bool VerifyPasswordHash(string password, string storedHash)
        {
            try
            {
                byte[] hashBytes = Convert.FromBase64String(storedHash);

                if (hashBytes.Length != 96)
                {
                    return VerifyLegacyHash(password, storedHash);
                }

                byte[] salt = new byte[32];
                Buffer.BlockCopy(hashBytes, 0, salt, 0, 32);

                using (var pbkdf2 = new Rfc2898DeriveBytes(
                    password + _pepper,
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

        private bool VerifyLegacyHash(string password, string storedHash)
        {
            try
            {
                using (var sha256 = SHA256.Create())
                {
                    byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
                    byte[] hash = sha256.ComputeHash(passwordBytes);
                    string computedHash = Convert.ToBase64String(hash);

                    if (computedHash == storedHash)
                    {
                        return true;
                    }

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
            }
        }

        ~DatabaseService()
        {
            Dispose(false);
        }
    }
}