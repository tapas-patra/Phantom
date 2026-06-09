using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;

namespace SecureOverlay
{
    /// <summary>
    /// Debug logger that captures all log output
    /// </summary>
    public class DebugLogger
    {
        private static DebugLogger? _instance;
        private readonly ObservableCollection<string> _logMessages = new ObservableCollection<string>();
        private readonly int _maxMessages = 1000;

        public static DebugLogger Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new DebugLogger();
                }
                return _instance;
            }
        }

        public ObservableCollection<string> LogMessages => _logMessages;

        private DebugLogger()
        {
            // Add startup message
            AddLog("═══════════════════════════════════════════════════════");
            AddLog("DEBUG LOGGER INITIALIZED");
            AddLog("All Log.WriteLine() output will appear here");
            AddLog("═══════════════════════════════════════════════════════");
        }

        public void AddLog(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            var timestampedMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";

            // Try to update on UI thread if available
            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    AddMessageToCollection(timestampedMessage);
                });
            }
            else
            {
                // Before UI is ready, just add directly
                AddMessageToCollection(timestampedMessage);
            }
        }

        private void AddMessageToCollection(string message)
        {
            try
            {
                // Remove old messages if we have too many
                while (_logMessages.Count >= _maxMessages)
                {
                    _logMessages.RemoveAt(0);
                }

                _logMessages.Add(message);
            }
            catch
            {
                // Ignore errors during logging
            }
        }

        public void Clear()
        {
            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _logMessages.Clear();
                    AddLog("Logs cleared by user");
                });
            }
        }

        public string GetAllLogs()
        {
            var sb = new StringBuilder();
            
            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var msg in _logMessages)
                    {
                        sb.AppendLine(msg);
                    }
                });
            }
            else
            {
                foreach (var msg in _logMessages)
                {
                    sb.AppendLine(msg);
                }
            }
            
            return sb.ToString();
        }
    }
}
