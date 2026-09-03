using System;
using System.Windows;
using System.Windows.Interop;
using System.Diagnostics;

namespace SecureOverlay
{
    /// <summary>
    /// Applies screen capture protection to any window
    /// </summary>
    public static class WindowProtection
    {
        public static void MakeInvisibleToScreenCapture(Window window)
        {
            window.Loaded += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                
                if (hwnd == IntPtr.Zero)
                {
                    Log.WriteLine("Failed to get window handle");
                    return;
                }

                ApplyProtection(hwnd);
            };
        }

        public static void ApplyProtection(IntPtr hwnd)
        {
            // Apply window styles
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            exStyle |= NativeMethods.WS_EX_LAYERED;
            exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            exStyle |= NativeMethods.WS_EX_NOACTIVATE;
            
            // DO NOT add WS_EX_TRANSPARENT - this would make clicks pass through
            // We want the window to capture mouse events so cursor shows in screen share
            
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);

            // Apply DWM attributes
            try
            {
                int excludeFromPeek = 1;
                NativeMethods.DwmSetWindowAttribute(
                    hwnd,
                    NativeMethods.DWMWA_EXCLUDED_FROM_PEEK,
                    ref excludeFromPeek,
                    sizeof(int)
                );
            }
            catch { }

            ApplyCaptureExclusion(hwnd);
            Log.WriteLine("  - Mouse cursor WILL be visible when hovering");
        }

        public static void ApplyCaptureExclusion(IntPtr hwnd)
        {
            bool success = NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
            
            if (success)
            {
                Log.WriteLine($"✓ Protection applied to window 0x{hwnd:X}");
                Log.WriteLine($"  - Window invisible to screen capture");
            }
            else
            {
                Log.WriteLine($"✗ Failed to apply protection to window 0x{hwnd:X}");
            }

            if (NativeMethods.GetWindowDisplayAffinity(hwnd, out var affinity))
            {
                Log.WriteLine($"  Affinity: 0x{affinity:X}");
            }
        }
    }
}
