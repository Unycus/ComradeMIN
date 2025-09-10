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

        private void Enter_to_register_Click(object sender, RoutedEventArgs e)
        {
            string username = Login_input.Text;
            string password = Password_input.Text;
            bool Succes = true;
            if (username.Length < 6 || password.Length < 8)
            {
                Succes = false;
                MessageBox.Show("Мало символов, попробуйте ещё раз");
            }
            if((Succes)&&Password_input.Text != Password_input2.Text)
            {
                Succes = false;
                MessageBox.Show("Пароли не совпадают");
            }
            if (Succes)
            {
                MessageBox.Show("Вы успешно зарегистрировались");
            }
        }
    }
}
