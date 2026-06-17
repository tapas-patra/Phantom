using System;
using System.Diagnostics;
using System.Threading;

namespace SecureOverlay
{
    public static class FallbackProtection
    {
        public static bool ApplyEnhancedProtection(IntPtr hwnd)
        {
            Log.WriteLine("Applying enhanced protection...");
            
            // Set window to topmost
            NativeMethods.SetWindowPos(
                hwnd, 
                NativeMethods.HWND_TOPMOST, 
                0, 0, 0, 0, 
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE
            );

            // Apply stealth window styles
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            exStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW;
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);

            // Try multiple times to apply protection
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                Log.WriteLine($"Protection attempt {attempt}/5...");
                
                // Reset to NONE first (except on first attempt)
                if (attempt > 1)
                {
                    NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_NONE);
                    Thread.Sleep(100);
                }

                // Try to set EXCLUDEFROMCAPTURE
                bool success = NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                
                if (success)
                {
                    // Verify it worked
                    Thread.Sleep(50);
                    uint affinity;
                    bool verified = NativeMethods.GetWindowDisplayAffinity(hwnd, out affinity);
                    
                    if (verified && affinity == NativeMethods.WDA_EXCLUDEFROMCAPTURE)
                    {
                        Log.WriteLine($"✓ Protection applied successfully on attempt {attempt}");
                        return true;
                    }
                    else
                    {
                        Log.WriteLine($"Verification failed. Affinity: 0x{affinity:X}");
                    }
                }
                else
                {
                    Log.WriteLine($"SetWindowDisplayAffinity failed");
                }

                if (attempt < 5)
                {
                    Thread.Sleep(200);
                }
            }

            // All attempts failed - try fallback WDA_MONITOR
            Log.WriteLine("Trying fallback WDA_MONITOR...");
            bool fallback = NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_MONITOR);
            
            if (fallback)
            {
                Log.WriteLine("⚠️ Partial protection (WDA_MONITOR) applied");
            }
            else
            {
                Log.WriteLine("✗ All protection methods failed");
            }
            
            return fallback;
        }

        public static void ReapplyProtection(IntPtr hwnd)
        {
            uint currentAffinity;
            bool success = NativeMethods.GetWindowDisplayAffinity(hwnd, out currentAffinity);

            if (!success || currentAffinity != NativeMethods.WDA_EXCLUDEFROMCAPTURE)
            {
                Log.WriteLine($"Protection lost! Current: 0x{currentAffinity:X}. Reapplying...");
                NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                
                // Verify
                Thread.Sleep(50);
                NativeMethods.GetWindowDisplayAffinity(hwnd, out currentAffinity);
                Log.WriteLine($"Reapplied. New value: 0x{currentAffinity:X}");
            }
        }
    }
}
