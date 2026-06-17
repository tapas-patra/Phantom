using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace SecureOverlay
{
    /// <summary>
    /// Monitors for Win+Tab keypress and temporarily hides windows
    /// </summary>
    public class TaskViewMonitor
    {
        // ═══════════════════════════════════════════════════════════════
        // P/INVOKE FOR KEYBOARD HOOK
        // ═══════════════════════════════════════════════════════════════
        
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int VK_TAB = 0x09;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        // ═══════════════════════════════════════════════════════════════
        // FIELDS
        // ═══════════════════════════════════════════════════════════════
        
        private IntPtr _hookID = IntPtr.Zero;
        private LowLevelKeyboardProc _proc;
        private Window _mainWindow;
        private Window? _fakeCursorWindow;
        private DispatcherTimer? _restoreTimer;
        private bool _windowsHidden = false;

        // ═══════════════════════════════════════════════════════════════
        // CONSTRUCTOR
        // ═══════════════════════════════════════════════════════════════
        
        public TaskViewMonitor(Window mainWindow, Window? fakeCursorWindow = null)
        {
            _mainWindow = mainWindow;
            _fakeCursorWindow = fakeCursorWindow;
            _proc = HookCallback;
        }

        // ═══════════════════════════════════════════════════════════════
        // START/STOP MONITORING
        // ═══════════════════════════════════════════════════════════════
        
        public void StartMonitoring()
        {
            _hookID = SetHook(_proc);
            Log.WriteLine("✓ Task View keyboard monitor started (Win+Tab detection)");
        }

        public void StopMonitoring()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
            _restoreTimer?.Stop();
            Log.WriteLine("✓ Task View monitor stopped");
        }

        // ═══════════════════════════════════════════════════════════════
        // KEYBOARD HOOK
        // ═══════════════════════════════════════════════════════════════
        
        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
            {
                var curModule = curProcess.MainModule;
                if (curModule == null)  // ← Add null check
                {
                    Log.WriteLine("⚠️ Could not get current module for keyboard hook");
                    return IntPtr.Zero;
                }
                
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, 
                    GetModuleHandle(curModule.ModuleName), 0);
            }
        }


        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);

                // Check if Tab is pressed
                if (vkCode == VK_TAB)
                {
                    // Check if Windows key is also pressed
                    bool winKeyPressed = (GetKeyState(VK_LWIN) & 0x8000) != 0 || 
                                        (GetKeyState(VK_RWIN) & 0x8000) != 0;

                    if (winKeyPressed)
                    {
                        Log.WriteLine("🔍 Win+Tab detected - hiding windows");
                        HideWindows();
                    }
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        // ═══════════════════════════════════════════════════════════════
        // HIDE/RESTORE WINDOWS
        // ═══════════════════════════════════════════════════════════════
        
        private void HideWindows()
        {
            if (_windowsHidden) return;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                // Hide main window
                if (_mainWindow != null && _mainWindow.Visibility == Visibility.Visible)
                {
                    _mainWindow.Visibility = Visibility.Collapsed;
                }

                // Hide fake cursor window
                if (_fakeCursorWindow != null && _fakeCursorWindow.Visibility == Visibility.Visible)
                {
                    _fakeCursorWindow.Visibility = Visibility.Collapsed;
                }

                _windowsHidden = true;
            });

            // Restore after 2 seconds (user should be done with Task View by then)
            _restoreTimer?.Stop();
            _restoreTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _restoreTimer.Tick += (s, e) =>
            {
                RestoreWindows();
                _restoreTimer.Stop();
            };
            _restoreTimer.Start();
        }

        private void RestoreWindows()
        {
            if (!_windowsHidden) return;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                // Restore main window
                if (_mainWindow != null)
                {
                    _mainWindow.Visibility = Visibility.Visible;
                }

                // Restore fake cursor window
                if (_fakeCursorWindow != null)
                {
                    _fakeCursorWindow.Visibility = Visibility.Visible;
                }

                _windowsHidden = false;
                Log.WriteLine("🔍 Task View closed - windows restored");
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // UPDATE FAKE CURSOR WINDOW REFERENCE
        // ═══════════════════════════════════════════════════════════════
        
        public void SetFakeCursorWindow(Window fakeCursorWindow)
        {
            _fakeCursorWindow = fakeCursorWindow;
            Log.WriteLine("✓ Fake cursor window registered with Task View monitor");
        }
    }
}
