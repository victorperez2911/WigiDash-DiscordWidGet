using System;
using System.IO;

namespace WigiDash.DiscordWidget.Core
{
    public static class FileLogger
    {
        private static readonly string _logFilePath;
        private static readonly object _lock = new object();

        static FileLogger()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logDir = Path.Combine(appDataPath, "G.SKILL", "WigiDashManager", "WidgetData", "DiscordWidget", "Logs");

            Directory.CreateDirectory(logDir);

            var logFileName = $"discord_widget_{DateTime.Now:yyyyMMdd_HHmmss}.log";
            _logFilePath = Path.Combine(logDir, logFileName);

            Log("=== Discord Widget Logger Started ===");
            Log($"Log file: {_logFilePath}");
        }

        public static void Log(string message)
        {
            lock (_lock)
            {
                try
                {
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    var logLine = $"[{timestamp}] {message}";

                    File.AppendAllText(_logFilePath, logLine + Environment.NewLine);

                    // Also output to Debug
                    System.Diagnostics.Debug.WriteLine(logLine);
                }
                catch
                {
                    // Ignore logging errors
                }
            }
        }

        public static string GetLogFilePath()
        {
            return _logFilePath;
        }

        public static void OpenLogDirectory()
        {
            try
            {
                var logDir = Path.GetDirectoryName(_logFilePath);
                System.Diagnostics.Process.Start("explorer.exe", logDir);
            }
            catch
            {
                // Ignore
            }
        }
    }
}
