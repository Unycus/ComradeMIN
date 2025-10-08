using System;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace ComradeMIN
{
    public class DatabaseService
    {
        private string connectionString = "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=Void;Integrated Security=True";

        public async Task<bool> RegisterUser(string username, string password)
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
                            return false;
                        }
                    }

                    // Создаем нового пользователя
                    string insertUserQuery = @"
                        INSERT INTO Users (UserName, PasswordHash, CreatedDate) 
                        VALUES (@UserName, @PasswordHash, GETDATE())";

                    using (SqlCommand insertCommand = new SqlCommand(insertUserQuery, connection))
                    {
                        insertCommand.Parameters.AddWithValue("@UserName", username);
                        // Хэшируем пароль перед сохранением
                        string passwordHash = HashPassword(password);
                        insertCommand.Parameters.AddWithValue("@PasswordHash", passwordHash);

                        int rowsAffected = await insertCommand.ExecuteNonQueryAsync();
                        return rowsAffected > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при регистрации: {ex.Message}");
                return false;
            }
        }

        // Метод для проверки логина и пароля при входе
        public async Task<bool> ValidateUser(string username, string password)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                        SELECT PasswordHash 
                        FROM Users 
                        WHERE UserName = @UserName";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserName", username);

                        var result = await command.ExecuteScalarAsync();
                        if (result == null)
                        {
                            // Пользователь не найден
                            return false;
                        }

                        string storedHash = result.ToString();
                        string inputHash = HashPassword(password);

                        // Сравниваем хеши
                        return storedHash == inputHash;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при входе: {ex.Message}");
                return false;
            }
        }

        // Метод для получения ID пользователя (если нужно)
        public async Task<int?> GetUserId(string username)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    string query = "SELECT UserId FROM Users WHERE UserName = @UserName";
                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserName", username);

                        var result = await command.ExecuteScalarAsync();
                        return result != null ? Convert.ToInt32(result) : (int?)null;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при получении ID пользователя: {ex.Message}");
                return null;
            }
        }

        public async Task<int?> ValidateUserAndGetId(string username, string password)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

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

                                if (storedHash == inputHash)
                                {
                                    return reader.GetInt32(0); // Возвращаем UserId
                                }
                            }
                            return null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при входе: {ex.Message}");
                return null;
            }
        }

        public async Task<int?> RegisterUserAndGetId(string username, string password)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    // Проверяем, не существует ли уже пользователь
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

                    // Создаем нового пользователя
                    string insertUserQuery = @"
                INSERT INTO Users (UserName, PasswordHash, CreatedDate) 
                VALUES (@UserName, @PasswordHash, GETDATE());
                SELECT SCOPE_IDENTITY();"; // Получаем ID нового пользователя

                    using (SqlCommand insertCommand = new SqlCommand(insertUserQuery, connection))
                    {
                        insertCommand.Parameters.AddWithValue("@UserName", username);
                        string passwordHash = HashPassword(password);
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

        private string HashPassword(string password)
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