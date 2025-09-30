using System;
using System.Collections.Generic;
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
    /// Логика взаимодействия для OptionsPage.xaml
    /// </summary>
    public partial class OptionsPage : Page
    {
        public OptionsPage()
        {
            InitializeComponent();
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




        private void Enter_to_chat_MouseEnter(object sender, MouseEventArgs e)
        {
            AnimateOvalScale("Oval_Options", 1.1, 150, Enter_to_chat); // увеличить до 1.1 за 150 мс
        }

        private void Enter_to_chat_MouseLeave(object sender, MouseEventArgs e)
        {
            AnimateOvalScale("Oval_Options", 1.0, 150, Enter_to_chat); 
        }

        private async void Enter_to_chat_Click(object sender, RoutedEventArgs e)
        {
            if (Enter_to_chat.Template.FindName("Oval_Options", Enter_to_chat) is Rectangle oval1)
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
                    NavigationService.Navigate(new UserDataBaseMessengePage());
                }
            } // Синия при нажатии
        }

        private void Option_Password_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Реализовать вызов пароля из БД
            Option_Password.Text = "asdasd";
        }
    }
}
