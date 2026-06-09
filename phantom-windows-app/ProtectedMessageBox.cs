using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Diagnostics;

namespace SecureOverlay
{
    public class ProtectedMessageBox : Window
    {
        private IntPtr _windowHandle;

        public ProtectedMessageBox(string message, string title, MessageBoxButton buttons, MessageBoxImage icon)
        {
            // Window properties
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Width = 600;
            SizeToContent = SizeToContent.Height;
            Title = title;

            // Apply protection when loaded
            Loaded += (s, e) => ApplyProtection();

            // Build UI
            BuildUI(message, title, buttons, icon);
        }

        private void ApplyProtection()
        {
            _windowHandle = new WindowInteropHelper(this).Handle;

            if (_windowHandle == IntPtr.Zero)
            {
                Log.WriteLine("Failed to get dialog window handle!");
                return;
            }

            // Apply window styles
            int exStyle = NativeMethods.GetWindowLong(_windowHandle, NativeMethods.GWL_EXSTYLE);
            exStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW;
            NativeMethods.SetWindowLong(_windowHandle, NativeMethods.GWL_EXSTYLE, exStyle);

            // Apply DWM protection
            try
            {
                int excludeFromPeek = 1;
                NativeMethods.DwmSetWindowAttribute(_windowHandle, NativeMethods.DWMWA_EXCLUDED_FROM_PEEK, 
                    ref excludeFromPeek, sizeof(int));
            }
            catch { }

            // Apply screen capture protection
            bool success = NativeMethods.SetWindowDisplayAffinity(_windowHandle, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
            
            if (!success)
            {
                // Try enhanced method
                FallbackProtection.ApplyEnhancedProtection(_windowHandle);
            }

            // Verify
            uint affinity;
            if (NativeMethods.GetWindowDisplayAffinity(_windowHandle, out affinity))
            {
                Log.WriteLine($"Dialog protected: 0x{affinity:X}");
            }
        }

        private void BuildUI(string message, string title, MessageBoxButton buttons, MessageBoxImage icon)
        {
            var grid = new System.Windows.Controls.Grid();
            grid.Margin = new Thickness(0);

            // Outer shadow
            var outerBorder = new System.Windows.Controls.Border
            {
                Background = new SolidColorBrush(Color.FromArgb(96, 0, 0, 0)),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(10),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    ShadowDepth = 0,
                    BlurRadius = 30,
                    Opacity = 0.9,
                    Color = Colors.Black
                }
            };
            grid.Children.Add(outerBorder);

            // Main border
            var mainBorder = new System.Windows.Controls.Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20)),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(12),
                BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(30, 25, 30, 25)
            };
            grid.Children.Add(mainBorder);

            var contentStack = new System.Windows.Controls.StackPanel();
            mainBorder.Child = contentStack;

            // Title
            var titleBlock = new System.Windows.Controls.TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 20),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    ShadowDepth = 2,
                    BlurRadius = 8,
                    Color = Colors.Black
                }
            };
            contentStack.Children.Add(titleBlock);

            // Icon + Message
            var messagePanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 25)
            };

            // Icon
            string iconText = icon switch
            {
                MessageBoxImage.Information => "ℹ️",
                MessageBoxImage.Question => "❓",
                MessageBoxImage.Warning => "⚠️",
                MessageBoxImage.Error => "❌",
                _ => "ℹ️"
            };

            var iconBlock = new System.Windows.Controls.TextBlock
            {
                Text = iconText,
                FontSize = 32,
                Margin = new Thickness(0, 0, 20, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            messagePanel.Children.Add(iconBlock);

            // Message
            var messageBlock = new System.Windows.Controls.TextBlock
            {
                Text = message,
                FontSize = 14,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 500,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    ShadowDepth = 2,
                    BlurRadius = 6,
                    Color = Colors.Black
                }
            };
            messagePanel.Children.Add(messageBlock);
            contentStack.Children.Add(messagePanel);

            // Buttons
            var buttonPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            if (buttons == MessageBoxButton.OK)
            {
                buttonPanel.Children.Add(CreateButton("OK", MessageBoxResult.OK));
            }
            else if (buttons == MessageBoxButton.YesNo)
            {
                buttonPanel.Children.Add(CreateButton("Yes", MessageBoxResult.Yes));
                buttonPanel.Children.Add(CreateButton("No", MessageBoxResult.No));
            }
            else if (buttons == MessageBoxButton.YesNoCancel)
            {
                buttonPanel.Children.Add(CreateButton("Yes", MessageBoxResult.Yes));
                buttonPanel.Children.Add(CreateButton("No", MessageBoxResult.No));
                buttonPanel.Children.Add(CreateButton("Cancel", MessageBoxResult.Cancel));
            }

            contentStack.Children.Add(buttonPanel);

            this.Content = grid;
        }

        private System.Windows.Controls.Button CreateButton(string text, MessageBoxResult result)
        {
            var button = new System.Windows.Controls.Button
            {
                Content = text,
                Width = 100,
                Height = 35,
                Margin = new Thickness(5, 0, 0, 0),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                BorderThickness = new Thickness(1.5)
            };

            button.Click += (s, e) =>
            {
                DialogResult = result == MessageBoxResult.Yes || result == MessageBoxResult.OK;
                Close();
            };

            // Button style
            var style = new Style(typeof(System.Windows.Controls.Button));
            var template = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button));
            var factory = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            factory.SetValue(System.Windows.Controls.Border.BackgroundProperty, 
                new System.Windows.TemplateBindingExtension(System.Windows.Controls.Button.BackgroundProperty));
            factory.SetValue(System.Windows.Controls.Border.BorderBrushProperty, 
                new System.Windows.TemplateBindingExtension(System.Windows.Controls.Button.BorderBrushProperty));
            factory.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, 
                new System.Windows.TemplateBindingExtension(System.Windows.Controls.Button.BorderThicknessProperty));
            factory.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(6));

            var contentPresenter = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
            contentPresenter.SetValue(System.Windows.Controls.ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(System.Windows.Controls.ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            factory.AppendChild(contentPresenter);

            template.VisualTree = factory;
            style.Setters.Add(new Setter(System.Windows.Controls.Button.TemplateProperty, template));

            var trigger = new System.Windows.Trigger { Property = System.Windows.Controls.Button.IsMouseOverProperty, Value = true };
            trigger.Setters.Add(new Setter(System.Windows.Controls.Button.OpacityProperty, 0.8));
            style.Triggers.Add(trigger);

            button.Style = style;

            return button;
        }

        // Static Show methods
        public static void Show(string message, string title = "SecureOverlay", MessageBoxImage icon = MessageBoxImage.Information)
        {
            var dialog = new ProtectedMessageBox(message, title, MessageBoxButton.OK, icon);
            dialog.ShowDialog();
        }

        public static bool? ShowQuestion(string message, string title = "SecureOverlay")
        {
            var dialog = new ProtectedMessageBox(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            return dialog.ShowDialog();
        }

        public static void ShowError(string message, string title = "Error")
        {
            var dialog = new ProtectedMessageBox(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            dialog.ShowDialog();
        }

        public static void ShowWarning(string message, string title = "Warning")
        {
            var dialog = new ProtectedMessageBox(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            dialog.ShowDialog();
        }
    }
}
