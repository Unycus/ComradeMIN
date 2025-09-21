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
    /// <summary>
    /// Логика взаимодействия для RegisterPage.xaml
    /// </summary>
    public partial class RegisterPage : Page
    {
        public RegisterPage()
        {
            InitializeComponent();
        }

        private void Enter_Click(object sender, RoutedEventArgs e)
        {
            NavigationService.Navigate(new LoginPage());
        }


        private void AnimateOvalScale(string ovalName, double targetScale, int durationMs, Button nameOfbutton) // Универсальное изменение масштаба (овала) кнопки
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
            // Анимация 
            AnimateOvalScale("Oval_registration_back", 1.1, 150, Back); // Увеличить до 1.1 за 150 мс
        }

        private void Back_registration_leave(object sender, MouseEventArgs e)
        {
            // Анимация 
            AnimateOvalScale("Oval_registration_back", 1.0, 150, Back); // Уменьшить до 1.0 за 150 мс
        }

        private async void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Back.Template.FindName("Oval_registration_back", Back) is Rectangle oval1)
            {
                // Если кисть заморожена, создаём её копию
                if (oval1.Fill.IsFrozen)
                    oval1.Fill = oval1.Fill.Clone();

                // Получаем кисть
                if (oval1.Fill is SolidColorBrush brush)
                {
                    // Анимация цвета от текущего к DeepSkyBlue
                    var colorAnim = new ColorAnimation
                    {
                        To = Colors.DeepSkyBlue,
                        Duration = TimeSpan.FromMilliseconds(300), // 0.2 секунды
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    // Запускаем анимацию
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
                    double seconds = 0.250; // <-- здесь задаёшь нужное количество секунд задержки

                    // Ждём указанное время, не блокируя UI
                    await Task.Delay(TimeSpan.FromSeconds(seconds));
                    NavigationService.Navigate(new LoginPage());
                }
            } // Синия при нажатии
        }

        private async void Register_Click(object sender, RoutedEventArgs e)
        {
            string username = Login_input.Text;
            string password = Password_input.Text;
            bool Succes = true;

            if (Register.Template.FindName("Oval_registration", Register) is Rectangle oval1)
            {
                // Если кисть заморожена, создаём её копию
                if (oval1.Fill.IsFrozen)
                    oval1.Fill = oval1.Fill.Clone();

                // Получаем кисть
                if (oval1.Fill is SolidColorBrush brush)
                {
                    // Анимация цвета от текущего к DeepSkyBlue
                    var colorAnim = new ColorAnimation
                    {
                        To = Colors.DeepSkyBlue,
                        Duration = TimeSpan.FromMilliseconds(300), // 0.2 секунды
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    // Запускаем анимацию
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
                }
            } // Синия при нажатии



            int a = 0;

            double seconds = 0.3; // <-- здесь задаёшь нужное количество секунд задержки

            // Ждём указанное время, не блокируя UI
            await Task.Delay(TimeSpan.FromSeconds(seconds));

            if (username.Length < 6 || password.Length < 8)
            {
                Succes = false;
                a = 1;
            }
            if ((Succes) && Password_input.Text != Password_input2.Text)
            {
                a = 2;
                Succes = false;
            }
            if (Succes)
            {
                NavigationService.Navigate(new LoginPage());
            }
            else
            {
                if (Register.Template.FindName("Oval_registration", Register) is Rectangle oval)
                {
                    // Если кисть заморожена, клонируем
                    if (oval.Fill.IsFrozen)
                        oval.Fill = oval.Fill.Clone();

                    // Сразу меняем цвет на Red
                    var brush = oval.Fill as SolidColorBrush;
                    if (brush == null) return;

                    // Плавная анимация обратно к исходному цвету (#E0E0E0)
                    var anim = new ColorAnimation
                    {
                        From = Colors.Red,
                        To = Color.FromRgb(224, 224, 224), // #E0E0E0
                        Duration = TimeSpan.FromSeconds(1), // 1 секунда
                        BeginTime = TimeSpan.FromSeconds(0.05) // через 0.05 сек после клика
                    };

                    brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
                    if (a == 1)
                    {
                        MessageBox.Show("Мало символов, попробуйте ещё раз");
                    }
                    else
                    {
                        MessageBox.Show("Пароли не совпадают");
                    }
                }
            }



        }
        private void Register_Enter(object sender, MouseEventArgs e)
        {
            AnimateOvalScale("Oval_registration", 1.1, 150, Register); // Увеличить до 1.1 за 150 мс
        }
        private void Register_Leave(object sender, MouseEventArgs e)
        {
            AnimateOvalScale("Oval_registration", 1.0, 150, Register); // Уменьшить до 1.0 за 150 мс
        }


    }
}
