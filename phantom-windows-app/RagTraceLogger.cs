using System;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;
using SecureOverlay.Platform.Windows;

namespace SecureOverlay
{
    public static class RagTraceLogger
    {
        private static readonly string LogFilePath = WindowsAppPaths.RagLogPath;
        private static readonly (bool Enabled, string Source, string Value) Configuration = ReadConfiguration();
        private static readonly Channel<string> Lines = Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
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
                Lines.Writer.TryWrite($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
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
                _ = Task.Run(WriteLoopAsync);
                _initialized = true;
            }
        }

        private static async Task WriteLoopAsync()
        {
            try
            {
                await using var writer = new StreamWriter(LogFilePath, append: true) { AutoFlush = true };
                await foreach (var line in Lines.Reader.ReadAllAsync())
                {
                    await writer.WriteLineAsync(line);
                }
            }
            catch
            {
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
