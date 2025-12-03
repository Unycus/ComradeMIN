using System;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ComradeMIN
{
    public partial class LoginPage : Page
    {
        private DatabaseService _databaseService;
        private bool isPasswordVisible = false;

        public LoginPage()
        {
            InitializeComponent();
            _databaseService = new DatabaseService();
        }

        private async void Enter_Click(object sender, RoutedEventArgs e)
        {
            string username = Login_input.Text.Trim();
            string password = Password_input.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Введите логин и пароль");
                return;
            }

            // Получаем ID пользователя при успешном входе
            int? userId = await _databaseService.ValidateUserAndGetId(username, password);

            if (userId.HasValue)
            {
                MessageBox.Show("Вход выполнен успешно!");
                // Переходим на страницу чатов с реальным UserId
                // ИСПРАВЛЕНО: используем userId.Value для преобразования int? в int
                NavigationService.Navigate(new UserDataBaseMessengePage(userId.Value));
            }
            else
            {
                MessageBox.Show("Неверный логин или пароль");
            }
        }

        private void Enter_to_register_Click(object sender, RoutedEventArgs e)
        {
            NavigationService.Navigate(new RegisterPage());
        }

        // Метод для показа/скрытия пароля
        private void glazik_Click(object sender, RoutedEventArgs e)
        {
            if (!isPasswordVisible)
            {
                // Показываем TextBox, скрываем PasswordBox
                PasswordTextBox.Text = Password_input.Password;
                PasswordTextBox.Visibility = Visibility.Visible;
                Password_input.Visibility = Visibility.Collapsed;
                glazik.Content = "🔒";
                isPasswordVisible = true;
            }
            else
            {
                // Показываем PasswordBox, скрываем TextBox
                Password_input.Password = PasswordTextBox.Text;
                Password_input.Visibility = Visibility.Visible;
                PasswordTextBox.Visibility = Visibility.Collapsed;
                glazik.Content = "👁";
                isPasswordVisible = false;
            }
        }

        // Анимационные методы (добавьте если их нет)
        private void Enter_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            AnimateOvalScale("Oval", 1.1, 150, Enter);
        }

        private void Enter_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            AnimateOvalScale("Oval", 1.0, 150, Enter);
        }

        private void Enter_to_register_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            AnimateOvalScale("Oval2", 1.1, 150, Enter_to_register);
        }

        private void Enter_to_register_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            AnimateOvalScale("Oval2", 1.0, 150, Enter_to_register);
        }

        private void AnimateOvalScale(string ovalName, double targetScale, int durationMs, Button button)
        {
            if (button.Template.FindName(ovalName, button) is Rectangle oval)
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
    }
}