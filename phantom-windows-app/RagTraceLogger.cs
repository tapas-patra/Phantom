using System;
using System.IO;
using SecureOverlay.Platform.Windows;

namespace SecureOverlay
{
    public static class RagTraceLogger
    {
        private static readonly string LogFilePath = WindowsAppPaths.RagLogPath;
        private static readonly bool Enabled = IsEnabledFromEnvironment();
        private static readonly object Sync = new object();
        private static bool _initialized;

        public static bool IsEnabled => Enabled;

        public static void WriteLine(string message)
        {
            if (!Enabled)
            {
                return;
            }

            try
            {
                EnsureInitialized();
                var timestamped = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
                lock (Sync)
                {
                    File.AppendAllText(LogFilePath, timestamped + Environment.NewLine);
                }
            }
            catch
            {
            }
        }

        public static string GetLogPath() => LogFilePath;

        private static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            lock (Sync)
            {
                if (_initialized)
                {
                    return;
                }

                var dir = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(
                    LogFilePath,
                    $"=== RAG SESSION START: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
                _initialized = true;
            }
        }

        private static bool IsEnabledFromEnvironment()
        {
            var value = Environment.GetEnvironmentVariable("RAG_LOG");
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
