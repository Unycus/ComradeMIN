using System;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ComradeMIN
{
    public partial class LoginPage : Page
    {
        private string connectionString = "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=Void;Integrated Security=True";
        private string currentPassword = ""; // Для хранения пароля

        public LoginPage()
        {
            InitializeComponent();
        }

        private void AnimateOvalScale(string ovalName, double targetScale, int durationMs, Button nameOfbutton)
        {
            if (nameOfbutton.Template.FindName(ovalName, nameOfbutton) is Rectangle oval)
            {
                if (oval.RenderTransform.IsFrozen)
                    oval.RenderTransform = oval.RenderTransform.Clone();

                if (oval.RenderTransform is ScaleTransform scale)
                {
                    var animX = new DoubleAnimation(targetScale, TimeSpan.FromMilliseconds(durationMs))
                    { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                    var animY = new DoubleAnimation(targetScale, TimeSpan.FromMilliseconds(durationMs))
                    { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };

                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
                }
            }
        }

        // Метод для проверки логина и пароля в базе данных
        private async Task<int?> AuthenticateUser(string username, string password)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    string query = "SELECT IDFellow FROM Fellows WHERE NickFellow = @Username AND PasswordFellow = @Password";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@Username", username);
                        command.Parameters.AddWithValue("@Password", password);

                        var result = await command.ExecuteScalarAsync();
                        if (result != null)
                        {
                            return Convert.ToInt32(result);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка базы данных: {ex.Message}");
            }

            return null;
        }

        private async void Enter_Click(object sender, RoutedEventArgs e)
        {
            string username = Login_input.Text;
            string password = currentPassword; // Используем сохраненный пароль

            // Анимация нажатия
            if (Enter.Template.FindName("Oval", Enter) is Rectangle oval1)
            {
                if (oval1.Fill.IsFrozen)
                    oval1.Fill = oval1.Fill.Clone();

                if (oval1.Fill is SolidColorBrush brush)
                {
                    var colorAnim = new ColorAnimation
                    {
                        To = Colors.DeepSkyBlue,
                        Duration = TimeSpan.FromMilliseconds(300),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(0.3));

            // Проверка введенных данных
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ShowErrorAnimation();
                MessageBox.Show("Введите логин и пароль");
                return;
            }

            // Аутентификация пользователя
            int? userID = await AuthenticateUser(username, password);

            if (userID.HasValue)
            {
                // Успешная аутентификация - переходим на страницу мессенджера
                NavigationService.Navigate(new UserDataBaseMessengePage(userID.Value));
            }
            else
            {
                ShowErrorAnimation();
                MessageBox.Show("Неверный логин или пароль");
            }
        }

        private void ShowErrorAnimation()
        {
            if (Enter.Template.FindName("Oval", Enter) is Rectangle oval)
            {
                if (oval.Fill.IsFrozen)
                    oval.Fill = oval.Fill.Clone();

                var brush = oval.Fill as SolidColorBrush;
                if (brush == null) return;

                var anim = new ColorAnimation
                {
                    From = Colors.Red,
                    To = Color.FromRgb(224, 224, 224),
                    Duration = TimeSpan.FromSeconds(1),
                    BeginTime = TimeSpan.FromSeconds(0.05)
                };

                brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
            }
        }

        private void Enter_MouseEnter(object sender, MouseEventArgs e)
        {
            AnimateOvalScale("Oval", 1.1, 150, Enter);
        }

        private void Enter_MouseLeave(object sender, MouseEventArgs e)
        {
            AnimateOvalScale("Oval", 1.0, 150, Enter);
        }

        private void Enter_to_register_MouseEnter(object sender, MouseEventArgs e)
        {
            AnimateOvalScale("Oval2", 1.1, 150, Enter_to_register);
        }

        private void Enter_to_register_MouseLeave(object sender, MouseEventArgs e)
        {
            AnimateOvalScale("Oval2", 1.0, 150, Enter_to_register);
        }

        private async void Enter_to_register_Click(object sender, RoutedEventArgs e)
        {
            if (Enter_to_register.Template.FindName("Oval2", Enter_to_register) is Rectangle oval)
            {
                if (oval.Fill.IsFrozen)
                    oval.Fill = oval.Fill.Clone();

                if (oval.Fill is SolidColorBrush brush)
                {
                    var colorAnim = new ColorAnimation
                    {
                        To = Colors.DeepSkyBlue,
                        Duration = TimeSpan.FromMilliseconds(300),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(0.3));
            NavigationService.Navigate(new RegisterPage());
        }

        private void glazik_Click(object sender, RoutedEventArgs e)
        {
            if (glazik.Content.ToString() == "🔑")
            {
                // Показываем пароль
                Password_input.Visibility = Visibility.Visible;
                TextBox hiddenTextBox = FindName("Login_input_copy1") as TextBox;
                if (hiddenTextBox != null)
                {
                    hiddenTextBox.Visibility = Visibility.Collapsed;
                }
                glazik.Content = "🔐";
            }
            else
            {
                // Скрываем пароль
                Password_input.Visibility = Visibility.Collapsed;
                TextBox hiddenTextBox = FindName("Login_input_copy1") as TextBox;
                if (hiddenTextBox != null)
                {
                    hiddenTextBox.Visibility = Visibility.Visible;
                    hiddenTextBox.Text = new string('*', currentPassword.Length);
                }
                glazik.Content = "🔑";
            }
        }

        // Обработчик изменения текста в поле пароля
        private void Password_input_TextChanged(object sender, TextChangedEventArgs e)
        {
            currentPassword = Password_input.Text;

            // Обновляем скрытое поле с звездочками
            TextBox hiddenTextBox = FindName("Login_input_copy1") as TextBox;
            if (hiddenTextBox != null && Password_input.Visibility == Visibility.Collapsed)
            {
                hiddenTextBox.Text = new string('*', currentPassword.Length);
            }
        }
    }
}