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
        private bool _isRegistering = false;

        public RegisterPage()
        {
            InitializeComponent();
            _databaseService = new DatabaseService();

            Login_input.KeyDown += Input_KeyDown;
            Password_input.KeyDown += Input_KeyDown;
            Password_input2.KeyDown += Input_KeyDown;
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (!_isRegistering)
                {
                    Register_Click(sender, e);
                    e.Handled = true;
                }
            }
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
            if (_isRegistering) return;

            _isRegistering = true;
            Register.IsEnabled = false;

            try
            {
                string username = Login_input.Text.Trim();
                string password = Password_input.Text;
                bool success = true;
                int errorCode = 0;

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

                if (username.Length < 3)
                {
                    success = false;
                    errorCode = 1;
                    MessageBox.Show("Логин должен содержать не менее 3 символов");
                    Login_input.Focus();
                }
                else if (password.Length < 6)
                {
                    success = false;
                    errorCode = 2;
                    MessageBox.Show("Пароль должен содержать не менее 6 символов");
                    Password_input.Focus();
                }
                else if (password != Password_input2.Text)
                {
                    success = false;
                    errorCode = 3;
                    MessageBox.Show("Пароли не совпадают");
                    Password_input2.Focus();
                }

                if (success)
                {
                    int? newUserId = await _databaseService.RegisterUserAndGetId(username, password);
                    if (newUserId.HasValue)
                    {
                        MessageBox.Show("Регистрация прошла успешно!");
                        NavigationService.Navigate(new UserDataBaseMessengePage(newUserId.Value));
                        return;
                    }
                    else
                    {
                        success = false;
                        errorCode = 4;
                        Login_input.Focus();
                    }
                }

                if (!success && errorCode != 4)
                {
                    ShowErrorAnimation();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Произошла ошибка: {ex.Message}");
            }
            finally
            {
                _isRegistering = false;
                Register.IsEnabled = true;
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