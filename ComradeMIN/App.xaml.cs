using System;
using System.Configuration;
using System.Data;
using System.Windows;

namespace ComradeMIN
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Глобальная обработка необработанных исключений
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                HandleException("НЕОБРАБОТАННОЕ ИСКЛЮЧЕНИЕ", args.ExceptionObject as Exception);
            };

            DispatcherUnhandledException += (s, args) =>
            {
                HandleException("ИСКЛЮЧЕНИЕ В UI ПОТОКЕ", args.Exception);
                args.Handled = true; // Предотвращаем падение приложения
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                HandleException("НЕОБРАБОТАННОЕ ИСКЛЮЧЕНИЕ В ЗАДАЧЕ", args.Exception);
                args.SetObserved(); // Помечаем как обработанное
            };
        }

        private void HandleException(string context, Exception ex)
        {
            if (ex == null) return;

            // Логируем в Debug
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] {context}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");

            // Логируем в файл
            try
            {
                string logDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ComradeMIN", "Logs");

                System.IO.Directory.CreateDirectory(logDir);

                string logFile = System.IO.Path.Combine(logDir, $"error_{DateTime.Now:yyyyMMdd}.log");
                string logMessage = $"[{DateTime.Now:HH:mm:ss}] {context}\n" +
                                   $"Message: {ex.Message}\n" +
                                   $"StackTrace: {ex.StackTrace}\n" +
                                   "========================================\n";

                System.IO.File.AppendAllText(logFile, logMessage);
            }
            catch
            {
                // Игнорируем ошибки логирования
            }

            // Показываем пользователю только в Debug режиме
#if DEBUG
            MessageBox.Show(
                $"Произошла ошибка: {ex.Message}\n\n" +
                $"Контекст: {context}\n" +
                "Подробности в логах.",
                "Ошибка приложения",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
#endif
        }
    }
}