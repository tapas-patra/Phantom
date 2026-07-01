using System;
using System.IO;
using SecureOverlay.Platform.Windows;

namespace SecureOverlay
{
    public static class RagTraceLogger
    {
        private static readonly string LogFilePath = WindowsAppPaths.RagLogPath;
        private static readonly (bool Enabled, string Source, string Value) Configuration = ReadConfiguration();
        private static readonly object Sync = new object();
        private static bool _initialized;

        public static bool IsEnabled => Configuration.Enabled;

        public static void WriteLine(string message)
        {
            if (!Configuration.Enabled)
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

        public static string GetLogPath()
        {
            if (Configuration.Enabled)
            {
                EnsureInitialized();
            }

            return LogFilePath;
        }

        public static string GetConfigurationSummary()
        {
            return $"enabled={Configuration.Enabled} source={Configuration.Source} value='{Configuration.Value}'";
        }

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

        private static (bool Enabled, string Source, string Value) ReadConfiguration()
        {
            var processValue = Environment.GetEnvironmentVariable("RAG_LOG");
            if (!string.IsNullOrWhiteSpace(processValue))
            {
                return (IsTruthy(processValue), "process", processValue);
            }

            var userValue = Environment.GetEnvironmentVariable("RAG_LOG", EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(userValue))
            {
                return (IsTruthy(userValue), "user", userValue);
            }

            var machineValue = Environment.GetEnvironmentVariable("RAG_LOG", EnvironmentVariableTarget.Machine);
            if (!string.IsNullOrWhiteSpace(machineValue))
            {
                return (IsTruthy(machineValue), "machine", machineValue);
            }

            return (false, "unset", string.Empty);
        }

        private static bool IsTruthy(string value)
        {
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
