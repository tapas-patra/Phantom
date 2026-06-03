using System;
using System.IO;

namespace SecureOverlay
{
    /// <summary>
    /// Saves logs to a file (in addition to the debug panel)
    /// </summary>
    public static class FileLogger
    {
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SecureOverlay",
            "crash_log.txt"
        );

        static FileLogger()
        {
            try
            {
                var dir = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Start fresh log on each app launch
                File.WriteAllText(LogFilePath, $"=== SESSION START: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
            }
            catch { }
        }

        public static void WriteLine(string message)
        {
            try
            {
                var timestamped = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
                File.AppendAllText(LogFilePath, timestamped + "\n");
            }
            catch 
            {
                // Silently fail if file logging doesn't work
            }
        }

        public static string GetLogPath() => LogFilePath;
    }
}
