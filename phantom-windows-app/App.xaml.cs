using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Application.Telemetry;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Infrastructure.Hosted;
using SecureOverlay.Infrastructure.Persistence;
using SecureOverlay.Infrastructure.Telemetry;
using SecureOverlay.Services;

namespace SecureOverlay
{
    public partial class App : System.Windows.Application
    {
        private ITelemetryService? _telemetryService;

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
            try
            {
                var store = new SqliteRuntimeStore(SettingsManager.GetSettingsPath());
                IAuthSessionRepository authSessionRepository = new SqliteAuthSessionRepository(store);
                ITelemetryRepository telemetryRepository = new SqliteTelemetryRepository(store);
                var hostedRuntimeOptions = HostedClientFactory.LoadOptions();
                _telemetryService = new HostedTelemetryService(
                    telemetryRepository,
                    authSessionRepository,
                    HostedClientFactory.CreateTelemetryClient(hostedRuntimeOptions),
                    hostedRuntimeOptions);
                _telemetryService.Track("app", "startup", new Dictionary<string, string>
                {
                    ["has_callback"] = false.ToString(),
                    ["hosted_mode"] = hostedRuntimeOptions.Mode,
                    ["hosted_backend"] = hostedRuntimeOptions.DesktopBackendBaseUrl
                });

                Log.WriteLine("Checking administrator privileges...");
                if (!IsRunningAsAdministrator())
                {
                    Log.WriteLine("✗ Not running as administrator!");
                    _telemetryService.Track("app", "startup_admin_required");
                    ShowCriticalStartupError(
                        "Administrator Required",
                        "Administrator privileges required.\n\nPlease right-click the application and select\n'Run as administrator'.");
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
                    _telemetryService.Track("app", "startup_unsupported_windows", new Dictionary<string, string>
                    {
                        ["build"] = build.ToString()
                    });
                    ShowCriticalStartupError(
                        "Unsupported Windows Version",
                        $"Windows 10 version 2004 or later is required.\n\nYour build: {build}\nRequired build: 19041+");
                    Log.WriteLine("Shutting down due to unsupported Windows version");
                    Shutdown();
                    return;
                }

                Log.WriteLine($"✓ Windows version compatible (build {Environment.OSVersion.Version.Build})");
                _telemetryService.Track("app", "startup_ready", new Dictionary<string, string>
                {
                    ["build"] = Environment.OSVersion.Version.Build.ToString()
                });

                Log.WriteLine("Prerequisites check complete - starting startup gate");
                Log.WriteLine("═══════════════════════════════════════════════════════");

                var restartMainWindowOnly = e.Args.Any(arg =>
                    string.Equals(arg, "--restart-main-window", StringComparison.OrdinalIgnoreCase));

                if (restartMainWindowOnly)
                {
                    Log.WriteLine("Restart flag detected - reopening main window directly");
                    var recoveredMainWindow = new MainWindow(new AppLaunchContext
                    {
                        GateState = SecureOverlay.Domain.Enums.StartupGateState.Ready,
                        Title = "Restart Recovery",
                        Message = "Recovered directly into the interview shell after an in-app restart.",
                        Detail = "Authentication and startup gate were intentionally bypassed for main-window recovery.",
                        CanStartInterview = true,
                        CanResumeLockedInterview = true
                    });
                    MainWindow = recoveredMainWindow;
                    recoveredMainWindow.Show();
                    Log.WriteLine("Main window reopened directly from restart flag");
                    return;
                }

                Log.WriteLine("Creating startup window...");
                var startupWindow = new StartupWindow(new LocalStartupGateService());
                MainWindow = startupWindow;
                startupWindow.Show();
                Log.WriteLine("Startup window shown successfully");
            }
            catch (Exception ex)
            {
                Log.WriteLine("═══════════════════════════════════════════════════════");
                Log.WriteLine("💥 FATAL STARTUP ERROR");
                Log.WriteLine($"Message: {ex.Message}");
                Log.WriteLine($"Source: {ex.Source}");
                Log.WriteLine($"Stack Trace:\n{ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    Log.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                    Log.WriteLine($"Inner Stack: {ex.InnerException.StackTrace}");
                }

                Log.WriteLine("═══════════════════════════════════════════════════════");
                ShowCriticalStartupError(
                    "Phantom Startup Failed",
                    $"The application failed before the main window could open.\n\nError: {ex.Message}\n\nLog file:\n{Log.GetLogFilePath()}");
                Shutdown(-1);
            }
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
                _telemetryService?.Track("crash", "unhandled_exception", new Dictionary<string, string>
                {
                    ["message"] = ex?.Message ?? "unknown",
                    ["source"] = ex?.Source ?? "unknown",
                    ["terminating"] = e.IsTerminating.ToString()
                });

                if (e.IsTerminating)
                {
                    ShowCriticalStartupError(
                        "Application Crash",
                        $"The application encountered a critical error and must close.\n\nError: {ex?.Message}\n\nLog file:\n{Log.GetLogFilePath()}");
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
                _telemetryService?.Track("crash", "dispatcher_exception", new Dictionary<string, string>
                {
                    ["message"] = e.Exception.Message,
                    ["source"] = e.Exception.Source ?? "unknown",
                    ["type"] = e.Exception.GetType().Name
                });
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

                MessageBox.Show(
                    $"An error occurred, but the app will continue running.\n\nError: {e.Exception.Message}\n\nLog file:\n{Log.GetLogFilePath()}",
                    "Application Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

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
            _telemetryService?.Track("app", "exit");
            
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

        private static void ShowCriticalStartupError(string title, string message)
        {
            try
            {
                MessageBox.Show(
                    message,
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
            }
        }
    }
}
