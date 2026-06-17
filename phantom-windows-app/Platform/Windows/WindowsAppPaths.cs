using System;
using System.IO;

namespace SecureOverlay.Platform.Windows
{
    public static class WindowsAppPaths
    {
        private const string LegacyFolderName = "SecureOverlay";
        private const string PrimaryFolderName = "Windows Host Service 271";

        private static readonly string AppDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        public static string LegacyRoot => Path.Combine(AppDataRoot, LegacyFolderName);
        public static string PrimaryRoot => Path.Combine(AppDataRoot, PrimaryFolderName);

        public static string LegacySettingsPath => Path.Combine(LegacyRoot, "settings.json");
        public static string LegacyConversationCachePath => Path.Combine(LegacyRoot, "conversation_cache.json");

        public static string DatabasePath => Path.Combine(PrimaryRoot, "phantom.db");
        public static string CrashLogPath => Path.Combine(PrimaryRoot, "crash_log.txt");
        public static string WebView2CachePath => Path.Combine(PrimaryRoot, "WebView2Cache");
        public static string TempRoot => Path.Combine(PrimaryRoot, "Temp");
        public static string SpeechRecognitionHtmlPath => Path.Combine(TempRoot, "speech_recognition.html");
        public static string MicrophonePermissionHtmlPath => Path.Combine(TempRoot, "microphone_permission.html");
        public static string SafeModeMarkerPath => Path.Combine(PrimaryRoot, "safe_mode.txt");
    }
}
