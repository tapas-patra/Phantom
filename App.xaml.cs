using System;
using System.Windows;

namespace SecureOverlay
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // ✅ Catch all unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            
            // CRITICAL: Initialize debug logger FIRST
            var logger = DebugLogger.Instance;
            
            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("APPLICATION STARTING");
            Log.WriteLine($"📁 Log file: {Log.GetLogFilePath()}");
            Log.WriteLine("═══════════════════════════════════════════════════════");

            base.OnStartup(e);

            Log.WriteLine("Checking administrator privileges...");
            if (!IsRunningAsAdministrator())
            {
                Log.WriteLine("✗ Not running as administrator!");
                
                // ✅ UPDATED: Use InvisibleMessageBox instead of MessageBox
                InvisibleMessageBox.Show(
                    "Administrator privileges required.\n\n" +
                    "Please right-click the application and select\n" +
                    "'Run as administrator'",
                    "Administrator Required"
                );
                
                Log.WriteLine("Shutting down due to missing admin privileges");
                Shutdown();
                return;
            }
            Log.WriteLine("✓ Running as administrator");

            Log.WriteLine("Checking Windows version...");
            if (!IsWindows10Build19041OrLater())
            {
                var build = Environment.OSVersion.Version.Build;
                Log.WriteLine($"✗ Windows build {build} is too old (need 19041+)");
                
                // ✅ UPDATED: Use InvisibleMessageBox instead of MessageBox
                InvisibleMessageBox.Show(
                    $"Windows 10 version 2004 or later is required.\n\n" +
                    $"Your build: {build}\n" +
                    $"Required build: 19041+",
                    "Unsupported Windows Version"
                );
                
                Log.WriteLine("Shutting down due to unsupported Windows version");
                Shutdown();
                return;
            }
            Log.WriteLine($"✓ Windows version compatible (build {Environment.OSVersion.Version.Build})");

            Log.WriteLine("Prerequisites check complete - starting main window");
            Log.WriteLine("═══════════════════════════════════════════════════════");
        }

        // ═══════════════════════════════════════════════════════════════
        // FATAL CRASH HANDLER (AppDomain)
        // ═══════════════════════════════════════════════════════════════
        
        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var ex = e.ExceptionObject as Exception;
                Log.WriteLine("═══════════════════════════════════════════════════════");
                Log.WriteLine("💥 FATAL CRASH (APP DOMAIN)");
                Log.WriteLine($"Message: {ex?.Message}");
                Log.WriteLine($"Source: {ex?.Source}");
                Log.WriteLine($"Stack Trace:\n{ex?.StackTrace}");
                
                if (ex?.InnerException != null)
                {
                    Log.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                    Log.WriteLine($"Inner Stack: {ex.InnerException.StackTrace}");
                }
                
                Log.WriteLine($"Is Terminating: {e.IsTerminating}");
                Log.WriteLine("═══════════════════════════════════════════════════════");

                if (e.IsTerminating)
                {
                    // ✅ UPDATED: Use InvisibleMessageBox instead of MessageBox
                    InvisibleMessageBox.Show(
                        $"💥 FATAL CRASH\n\n" +
                        $"The application encountered a critical error and must close.\n\n" +
                        $"Error: {ex?.Message}\n\n" +
                        $"Log file location:\n{Log.GetLogFilePath()}\n\n" +
                        $"Please check the log file for details.",
                        "Application Crash"
                    );
                }
            }
            catch { }
        }

        // ═══════════════════════════════════════════════════════════════
        // UI THREAD EXCEPTION HANDLER
        // ═══════════════════════════════════════════════════════════════
        
        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                Log.WriteLine("═══════════════════════════════════════════════════════");
                Log.WriteLine("💥 EXCEPTION (UI THREAD)");
                Log.WriteLine($"Message: {e.Exception.Message}");
                Log.WriteLine($"Source: {e.Exception.Source}");
                Log.WriteLine($"Type: {e.Exception.GetType().Name}");
                Log.WriteLine($"Stack Trace:\n{e.Exception.StackTrace}");
                
                if (e.Exception.InnerException != null)
                {
                    Log.WriteLine($"Inner Exception: {e.Exception.InnerException.Message}");
                    Log.WriteLine($"Inner Stack:\n{e.Exception.InnerException.StackTrace}");
                }
                
                Log.WriteLine("═══════════════════════════════════════════════════════");

                // ✅ UPDATED: Use InvisibleMessageBox instead of MessageBox
                InvisibleMessageBox.Show(
                    $"⚠️ APPLICATION ERROR\n\n" +
                    $"An error occurred, but the app will continue running.\n\n" +
                    $"Error: {e.Exception.Message}\n\n" +
                    $"Log file location:\n{Log.GetLogFilePath()}\n\n" +
                    $"Check the log file for full details.",
                    "Application Error"
                );

                e.Handled = true; // Prevent full crash
            }
            catch { }
        }

        // ═══════════════════════════════════════════════════════════════
        // SHUTDOWN
        // ═══════════════════════════════════════════════════════════════
        
        protected override void OnExit(ExitEventArgs e)
        {
            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("APPLICATION EXITING");
            Log.WriteLine("═══════════════════════════════════════════════════════");
            
            base.OnExit(e);
            
            // Force terminate all threads
            Environment.Exit(0);
        }

        // ═══════════════════════════════════════════════════════════════
        // HELPER METHODS
        // ═══════════════════════════════════════════════════════════════
        
        private bool IsRunningAsAdministrator()
        {
            try
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error checking admin status: {ex.Message}");
                return false;
            }
        }

        private bool IsWindows10Build19041OrLater()
        {
            return Environment.OSVersion.Version.Build >= 19041;
        }
    }
}
