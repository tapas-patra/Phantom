using System;
using System.Runtime.InteropServices;

namespace SecureOverlay
{
    public static class NativeMethods
    {
        // Window Display Affinity
        public const uint WDA_NONE = 0x00000000;
        public const uint WDA_MONITOR = 0x00000001;
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint dwAffinity);

        // Extended Window Styles
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        // Desktop Window Manager
        public const int DWMWA_EXCLUDED_FROM_PEEK = 7;
        public const int DWMWA_CLOAK = 13;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        // Window Positioning
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public const uint SWP_SHOWWINDOW = 0x0040;

        // Keyboard Hook
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);

        // Virtual Key Codes
        public const int VK_OEM_3 = 0xC0;      // ` ~
        public const int VK_OEM_MINUS = 0xBD;  // - _
        public const int VK_OEM_PLUS = 0xBB;   // = +
        public const int VK_F13 = 0x7C;
        public const int VK_F14 = 0x7D;
        public const int VK_F15 = 0x7E;
        
        public const int VK_CONTROL = 0x11;
        public const int VK_MENU = 0x12;       // Alt
        public const int VK_SHIFT = 0x10;

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        public static bool IsKeyPressed(int vKey)
        {
            return (GetAsyncKeyState(vKey) & 0x8000) != 0;
        }
    }
}
