using System;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;
using SecureOverlay.Platform.Windows;

namespace SecureOverlay
{
    /// <summary>
    /// Saves logs to a file (in addition to the debug panel)
    /// </summary>
    public static class FileLogger
    {
        private static readonly string LogFilePath = WindowsAppPaths.CrashLogPath;
        private static readonly Channel<string> Lines = Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

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
                _ = Task.Run(WriteLoopAsync);
            }
            catch { }
        }

        public static void WriteLine(string message)
        {
            try
            {
                Lines.Writer.TryWrite($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
            }
            catch 
            {
                // Silently fail if file logging doesn't work
            }
        }

        public static string GetLogPath() => LogFilePath;

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
    }
}
