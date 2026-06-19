using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Diagnostics;

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
                    // Find the popup
                    var popup = FindVisualChild<Popup>(comboBox);
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
                                    WindowProtection.ApplyProtection(hwnd);
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

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    return typedChild;

                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }
    }
}
