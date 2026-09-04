using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace SecureOverlay
{
    /// <summary>
    /// Protects ComboBox dropdown popups from screen capture
    /// </summary>
    public static class ComboBoxProtection
    {
        public static void ProtectComboBox(ComboBox comboBox)
        {
            comboBox.MaxDropDownHeight = Math.Min(comboBox.MaxDropDownHeight > 0 ? comboBox.MaxDropDownHeight : 320, 320);
            comboBox.DropDownOpened += (s, e) =>
            {
                try
                {
                    // PART_Popup owns a separate HWND and is not in the ComboBox visual tree.
                    var popup = comboBox.Template.FindName("PART_Popup", comboBox) as Popup;
                    if (popup?.Child != null)
                    {
                        // Wait for popup to be fully rendered
                        popup.Child.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                var hwndSource = PresentationSource.FromVisual(popup.Child) as HwndSource;
                                if (hwndSource != null)
                                {
                                    var hwnd = hwndSource.Handle;
                                    WindowProtection.ApplyCaptureExclusion(hwnd);
                                    Log.WriteLine($"✓ ComboBox popup protected: 0x{hwnd:X}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.WriteLine($"Popup protection error: {ex.Message}");
                            }
                        }), System.Windows.Threading.DispatcherPriority.Loaded);
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"ComboBox protection error: {ex.Message}");
                }
            };
        }

    }
}
