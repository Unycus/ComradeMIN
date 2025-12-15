using System;
using System.IO;
using System.Diagnostics;

namespace ComradeMIN
{
    public static class LoggingService
    {
        private static readonly object _lock = new object();
        private static string _logDirectory;

        static LoggingService()
        {
            // Папка для логов в AppData
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _logDirectory = Path.Combine(appData, "ComradeMIN", "Logs");

            if (!Directory.Exists(_logDirectory))
                Directory.CreateDirectory(_logDirectory);
        }

        public static void Log(string message, LogLevel level = LogLevel.Info)
        {
            lock (_lock)
            {
                try
                {
                    string logFile = Path.Combine(_logDirectory, $"app_{DateTime.Now:yyyyMMdd}.log");
                    string logEntry = $"{DateTime.Now:HH:mm:ss} [{level}] {message}";

                    // Пишем в Debug
                    Debug.WriteLine(logEntry);

                    // Пишем в файл
                    File.AppendAllText(logFile, logEntry + Environment.NewLine);
                }
                catch
                {
                    // Игнорируем ошибки логирования
                }
            }
        }

        public static void LogError(string message, Exception ex = null)
        {
            string errorMessage = message;
            if (ex != null)
            {
                errorMessage += $"\nException: {ex.Message}\nStackTrace: {ex.StackTrace}";
            }
            Log(errorMessage, LogLevel.Error);
        }

        public static void LogWarning(string message)
        {
            Log(message, LogLevel.Warning);
        }

        public static void LogInfo(string message)
        {
            Log(message, LogLevel.Info);
        }

        public static void LogDebug(string message)
        {
#if DEBUG
            Log(message, LogLevel.Debug);
#endif
        }

        public enum LogLevel
        {
            Debug,
            Info,
            Warning,
            Error
        }
    }
}