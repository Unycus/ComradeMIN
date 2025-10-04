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
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ComradeMIN
{
    /// <summary>
    /// Логика взаимодействия для LoginPage.xaml
    /// </summary>
    public partial class LoginPage : Page
    {
        public LoginPage()
        {
            InitializeComponent();
        }

        private void Enter_Click(object sender, RoutedEventArgs e)
        {
            string username = Login_input.Text;
            string password = Password_input.Text;
            bool Succes = true;
            if (username.Length < 6 || password.Length < 8)
            {
                Succes = false;
                MessageBox.Show("Мало символов, попробуйте ещё раз");
            }

            if (Succes)
            {
                MessageBox.Show("Вы успешно вошли");
                NavigationService.Navigate(new UserDataBaseMessengePage());
            }
        }
            private void Enter_to_register_Click(object sender, RoutedEventArgs e)
            {
                NavigationService.Navigate(new RegisterPage());
            }
<<<<<<< Updated upstream
        
=======


        }



        private void Enter_MouseEnter(object sender, MouseEventArgs e)
        {
            // Анимация 
            AnimateOvalScale("Oval", 1.1, 150, Enter); // увеличить до 1.1 за 150 мс
        }

        private void Enter_MouseLeave(object sender, MouseEventArgs e)
        {
            // Возврат формы
            AnimateOvalScale("Oval", 1.0, 150, Enter); // вернуть к 1.0 за 150 мс
        }
        private void Enter_to_register_MouseEnter(object sender, MouseEventArgs e)
        {
            AnimateOvalScale("Oval2", 1.1, 150, Enter_to_register); // увеличить до 1.1 за 150 мс
        }

        // Анимация при уходе мыши
        private void Enter_to_register_MouseLeave(object sender, MouseEventArgs e)
        {
            AnimateOvalScale("Oval2", 1.0, 150, Enter_to_register); // вернуть к 1.0 за 150 мс
        }

        // Клик с изменением цвета и плавным возвратом
        private async void Enter_to_register_Click(object sender, RoutedEventArgs e)
        {
            if (Enter_to_register.Template.FindName("Oval2", Enter_to_register) is Rectangle oval)
            {
                // Если кисть заморожена, создаём её копию
                if (oval.Fill.IsFrozen)
                    oval.Fill = oval.Fill.Clone();

                // Получаем кисть
                if (oval.Fill is SolidColorBrush brush)
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
            }


            double seconds = 0.3; // <-- здесь задаёшь нужное количество секунд задержки

            // Ждём указанное время, не блокируя UI
            await Task.Delay(TimeSpan.FromSeconds(seconds));

            // Навигация на страницу после задержки
            NavigationService.Navigate(new RegisterPage());
        }

        private void glazik_Click(object sender, RoutedEventArgs e)
        {
            string passhide = "";
            string stars = "";
            
            if (glazik.Content == "🔑")
            {

                for (int i = 1; i <= passhide.Length; i++)
                {
                    stars += "*";
                }
                
                Password_input.Text = stars;
                glazik.Content = "🔐";
            }
            else
            {
                MessageBox.Show(passhide);
                Password_input.Text = passhide.ToString();
                glazik.Content = "🔑";
            }
        }
>>>>>>> Stashed changes
    }
}
