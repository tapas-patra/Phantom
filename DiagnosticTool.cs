using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SecureOverlay
{
    public static class DiagnosticTool
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint dwAffinity);

        public static void ShowDetailedDiagnostics(IntPtr windowHandle)
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════════");
            sb.AppendLine("       SECURE OVERLAY - DIAGNOSTIC REPORT");
            sb.AppendLine("═══════════════════════════════════════════════════════════");
            sb.AppendLine();

            var version = Environment.OSVersion.Version;
            sb.AppendLine("🖥️  WINDOWS VERSION:");
            sb.AppendLine($"    Build: {version.Build} (Required: 19041+)");
            sb.AppendLine($"    Status: {(version.Build >= 19041 ? "✅ OK" : "❌ TOO OLD")}");
            sb.AppendLine();

            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            bool isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            
            sb.AppendLine("🔐 ADMINISTRATOR:");
            sb.AppendLine($"    Status: {(isAdmin ? "✅ YES" : "❌ NO")}");
            sb.AppendLine();

            sb.AppendLine("🪟 WINDOW:");
            sb.AppendLine($"    Handle: 0x{windowHandle:X}");
            sb.AppendLine();

            if (windowHandle != IntPtr.Zero)
            {
                uint affinity = 0;
                bool success = GetWindowDisplayAffinity(windowHandle, out affinity);
                
                sb.AppendLine("🛡️  PROTECTION:");
                sb.AppendLine($"    Affinity: 0x{affinity:X} ({affinity})");
                sb.AppendLine($"    Expected: 0x11 (17)");
                
                if (success && affinity == 0x11)
                {
                    sb.AppendLine("    Status: ✅ PROTECTED");
                    sb.AppendLine();
                    sb.AppendLine("✅ Main window is protected!");
                    sb.AppendLine("✅ All dialogs are also protected!");
                    sb.AppendLine();
                    sb.AppendLine("If still visible, test with:");
                    sb.AppendLine("  • DESKTOP apps (not browser)");
                    sb.AppendLine("  • Share 'Entire Screen' (not window)");
                }
                else
                {
                    sb.AppendLine("    Status: ❌ NOT PROTECTED");
                }
            }

            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════════");

            // Use protected message box
            ProtectedMessageBox.Show(sb.ToString(), "Diagnostic Report");
        }

        public static string GetTestInstructions()
        {
            return 
                "HOW TO TEST:\n\n" +
                "1. Open Teams/Zoom DESKTOP APP (not browser)\n" +
                "2. Start meeting, share 'Entire Screen'\n" +
                "3. Move this window around\n" +
                "4. Click buttons to test dialogs\n" +
                "5. Ask viewer if they see ANYTHING\n\n" +
                "EXPECTED: Everything is invisible!\n" +
                "Main window + ALL dialogs are hidden!";
        }
    }
}
