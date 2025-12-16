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

            // Обработчик навигации для отслеживания переходов
            MainFrame.Navigated += MainFrame_Navigated;
        }

        private void MainFrame_Navigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
        {
            Debug.WriteLine($"Навигация на: {e.Content.GetType().Name}");

            // Если мы возвращаемся на страницу чатов из настроек
            if (e.Content is UserDataBaseMessengePage chatPage)
            {
                Debug.WriteLine("Возврат на страницу чатов");

                // Принудительно очищаем историю навигации
                while (MainFrame.CanGoBack)
                {
                    MainFrame.RemoveBackEntry();
                }
            }
        }
    }
}