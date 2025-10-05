using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
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
    public partial class RegisterPage : Page
    {
        private DatabaseService _databaseService;

        public RegisterPage()
        {
            InitializeComponent();
            _databaseService = new DatabaseService();
        }

        private void Enter_Click(object sender, RoutedEventArgs e)
        {
            NavigationService.Navigate(new LoginPage());
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

        private void Back_registration_enter(object sender, MouseEventArgs e)
        {
            AnimateOvalScale("Oval_registration_back", 1.1, 150, Back);
        }

        private void Back_registration_leave(object sender, MouseEventArgs e)
        {
            AnimateOvalScale("Oval_registration_back", 1.0, 150, Back);
        }

        private async void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Back.Template.FindName("Oval_registration_back", Back) is Rectangle oval1)
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
                    double seconds = 0.250;
                    await Task.Delay(TimeSpan.FromSeconds(seconds));
                    NavigationService.Navigate(new LoginPage());
                }
            }
        }

        private async void Register_Click(object sender, RoutedEventArgs e)
        {
            string username = Login_input.Text.Trim();
            string password = Password_input.Text;
            bool success = true;
            int errorCode = 0;

            // Анимация нажатия
            if (Register.Template.FindName("Oval_registration", Register) is Rectangle oval1)
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

            // Валидация логина и пароля
            if (username.Length < 3)
            {
                success = false;
                errorCode = 1;
                MessageBox.Show("Логин должен содержать не менее 3 символов");
            }
            else if (password.Length < 6)
            {
                success = false;
                errorCode = 2;
                MessageBox.Show("Пароль должен содержать не менее 6 символов");
            }
            else if (password != Password_input2.Text)
            {
                success = false;
                errorCode = 3;
                MessageBox.Show("Пароли не совпадают");
            }

            // Регистрация в базе данных
            if (success)
            {
                bool registrationResult = await _databaseService.RegisterUser(username, password);
                if (registrationResult)
                {
                    MessageBox.Show("Регистрация прошла успешно!");
                    NavigationService.Navigate(new LoginPage());
                }
                else
                {
                    // Ошибка уже показана в DatabaseService
                    success = false;
                    errorCode = 4;
                }
            }

            // Анимация ошибки если нужно
            if (!success && errorCode != 4) // errorCode 4 - ошибка уже обработана в DatabaseService
            {
                ShowErrorAnimation();
            }
        }

        private void ShowErrorAnimation()
        {
            if (Register.Template.FindName("Oval_registration", Register) is Rectangle oval)
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

        private void Register_Enter(object sender, MouseEventArgs e)
        {
            AnimateOvalScale("Oval_registration", 1.1, 150, Register);
        }

        private void Register_Leave(object sender, MouseEventArgs e)
        {
            AnimateOvalScale("Oval_registration", 1.0, 150, Register);
        }
    }
}