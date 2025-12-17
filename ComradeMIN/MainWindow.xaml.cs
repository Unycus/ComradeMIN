using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace ComradeMIN
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            MainFrame.Navigate(new LoginPage());

            MainFrame.Navigated += MainFrame_Navigated;
        }

        private void MainFrame_Navigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
        {
            Debug.WriteLine($"Навигация на: {e.Content.GetType().Name}");

            if (e.Content is UserDataBaseMessengePage chatPage)
            {
                Debug.WriteLine("Возврат на страницу чатов");

                while (MainFrame.CanGoBack)
                {
                    MainFrame.RemoveBackEntry();
                }
            }
        }
    }
}