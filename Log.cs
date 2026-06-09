using System;
using System.IO;
using System.Diagnostics;

namespace SecureOverlay
{
    /// <summary>
    /// Custom logger that captures all output for the debug panel AND saves to file
    /// Use Log.WriteLine() instead of Debug.WriteLine()
    /// </summary>
    public static class Log
    {
        public static void WriteLine(string message)
        {
            // Send to debug output (Visual Studio)
            Debug.WriteLine(message);

            TryWriteToConsole(message);
            
            // Send to our debug logger (in-app debug panel)
            DebugLogger.Instance.AddLog(message);
            
            // ✅ NEW: Also write to file (survives crashes)
            FileLogger.WriteLine(message);
        }

        public static void Write(string message)
        {
            Debug.Write(message);
            TryWriteToConsole(message);
            DebugLogger.Instance.AddLog(message);
            FileLogger.WriteLine(message); // ✅ NEW
        }
        
        // ✅ NEW: Get log file location
        public static string GetLogFilePath()
        {
            return FileLogger.GetLogPath();
        }

        private static void TryWriteToConsole(string message)
        {
            try
            {
                if (!Console.IsOutputRedirected || !Console.IsErrorRedirected)
                {
                    Console.Error.WriteLine(message);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WT_SESSION"))
                    || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROMPT")))
                {
                    Console.Error.WriteLine(message);
                }
            }
            catch (IOException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
