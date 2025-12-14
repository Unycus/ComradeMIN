using System;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Input; // Добавьте эту директиву

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

            // Подписываемся на события KeyDown
            Login_input.KeyDown += Input_KeyDown;
            Password_input.KeyDown += Input_KeyDown;
            PasswordTextBox.KeyDown += Input_KeyDown;
        }

        // Общий обработчик нажатия клавиш
        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Если нажат Enter - вызываем вход
                Enter_Click(sender, e);
                e.Handled = true; // Предотвращаем дальнейшую обработку
            }
        }

        private async void Enter_Click(object sender, RoutedEventArgs e)
        {
            string username = Login_input.Text.Trim();
            string password = isPasswordVisible ? PasswordTextBox.Text : Password_input.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Введите логин и пароль");
                return;
            }

            // Блокируем кнопку во время выполнения
            Enter.IsEnabled = false;

            try
            {
                // Получаем ID пользователя при успешном входе
                int? userId = await _databaseService.ValidateUserAndGetId(username, password);

                if (userId.HasValue)
                {
                    MessageBox.Show("Вход выполнен успешно!");
                    // Переходим на страницу чатов с реальным UserId
                    NavigationService.Navigate(new UserDataBaseMessengePage(userId.Value));
                }
                else
                {
                    MessageBox.Show("Неверный логин или пароль");
                    // Фокусируемся на поле пароля для повторного ввода
                    if (isPasswordVisible)
                        PasswordTextBox.Focus();
                    else
                        Password_input.Focus();
                }
            }
            finally
            {
                Enter.IsEnabled = true;
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

        // Анимационные методы
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