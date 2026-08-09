using System;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
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
        private readonly ConcurrentQueue<string> _pendingMessages = new();
        private volatile bool _uiCollectionEnabled;

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

            _pendingMessages.Enqueue(timestampedMessage);
            while (_pendingMessages.Count > _maxMessages && _pendingMessages.TryDequeue(out _))
            {
            }

            if (_uiCollectionEnabled && System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(FlushPending));
            }
        }

        public void SetUiCollectionEnabled(bool enabled)
        {
            _uiCollectionEnabled = enabled;
            if (enabled && System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(FlushPending));
            }
        }

        private void FlushPending()
        {
            while (_pendingMessages.TryDequeue(out var message))
            {
                AddMessageToCollection(message);
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
