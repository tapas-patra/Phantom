using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace SecureOverlay
{
    public partial class ScreenshotCapture : Window
    {
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private System.Windows.Point _startPoint;
        private bool _isSelecting;
        private readonly bool _useFakeCursor;
        private readonly System.Windows.Point? _entryFromScreen;
        private CursorManager? _cursorManager;
        private bool _cursorReady;
        public BitmapImage? CapturedImage { get; private set; }
        public bool ImageCaptured { get; private set; }

        private double _dpiScaleX = 1.0;
        private double _dpiScaleY = 1.0;

        public ScreenshotCapture(bool useFakeCursor = true, System.Windows.Point? entryFromScreen = null)
        {
            _useFakeCursor = useFakeCursor;
            _entryFromScreen = entryFromScreen;
            InitializeComponent();

            // Never surface this picker in the taskbar / Alt+Tab.
            ShowInTaskbar = false;
            WindowProtection.MakeInvisibleToScreenCapture(this);

            MouseLeftButtonDown += ScreenshotCapture_MouseLeftButtonDown;
            MouseMove += ScreenshotCapture_MouseMove;
            MouseLeftButtonUp += ScreenshotCapture_MouseLeftButtonUp;
            MouseEnter += ScreenshotCapture_MouseEnter;
            Loaded += ScreenshotCapture_Loaded;
            Closing += ScreenshotCapture_Closing;
            Activated += (_, _) =>
            {
                EnsureKeyboardFocus();
                KeepFakeCursorAlive();
            };

            Focusable = true;

            if (_useFakeCursor)
            {
                // Real cursor must stay hidden; live cursor is the protected Topmost window.
                // Activation happens on Loaded (after Cross glyph) so entry slide can run.
                Cursor = Cursors.None;
                ForceCursor = true;
                _cursorManager = new CursorManager(this, CustomCursorCanvas, useFakeCursor: true, fakeCursorSize: 1.0);
            }
            else
            {
                Cursor = Cursors.Cross;
                ForceCursor = true;
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero)
                {
                    return;
                }

                var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                exStyle |= WS_EX_TOOLWINDOW;
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
                WindowProtection.ApplyCaptureExclusion(hwnd);
                Log.WriteLine("✓ Screenshot picker hidden from taskbar/Alt+Tab");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"⚠️ Failed to harden screenshot window: {ex.Message}");
            }
        }

        private void ScreenshotCapture_Loaded(object sender, RoutedEventArgs e)
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
            }

            CustomCursorCanvas.Width = ActualWidth;
            CustomCursorCanvas.Height = ActualHeight;
            Panel.SetZIndex(CustomCursorCanvas, 9999);

            EnsureKeyboardFocus();

            if (_cursorManager != null)
            {
                // Capture a crosshair glyph into the live cursor before the system cursor is hidden.
                Cursor = Cursors.Cross;
                ForceCursor = true;
                if (_entryFromScreen is { } from)
                {
                    // x,y (main-window click) → z,a (current pointer): same AnimateToPosition as exit.
                    _cursorManager.ActivateCustomCursorFrom(from);
                }
                else
                {
                    _cursorManager.SetApplicationFocusActive(true);
                }

                _cursorReady = true;
                Cursor = Cursors.None;
                ForceCursor = true;
            }

            Log.WriteLine(_useFakeCursor
                ? "✓ Screenshot picker ready (fake cursor active, system cursor hidden)"
                : "✓ Screenshot picker ready (system crosshair)");
        }

        private void ScreenshotCapture_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _cursorManager?.Dispose();
            _cursorManager = null;
        }

        private void ScreenshotCapture_MouseEnter(object sender, MouseEventArgs e)
        {
            KeepFakeCursorAlive();
        }

        private void KeepFakeCursorAlive()
        {
            if (_cursorManager == null || !_cursorReady)
            {
                return;
            }

            // Never tear the live cursor down while the picker is open — keep it above this Topmost overlay.
            _cursorManager.SetApplicationFocusActive(true);
            _cursorManager.EnsureLiveCursorAbove();
            Cursor = Cursors.None;
            ForceCursor = true;
        }

        private void ScreenshotCapture_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            EnsureKeyboardFocus();
            KeepFakeCursorAlive();
            CaptureMouse();
            _startPoint = e.GetPosition(SelectionCanvas);
            _isSelecting = true;

            SelectionRectangle.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionRectangle, _startPoint.X);
            Canvas.SetTop(SelectionRectangle, _startPoint.Y);
            SelectionRectangle.Width = 0;
            SelectionRectangle.Height = 0;
        }

        private void ScreenshotCapture_MouseMove(object sender, MouseEventArgs e)
        {
            var currentPoint = e.GetPosition(SelectionCanvas);
            if (_cursorManager != null)
            {
                _cursorManager.UpdateCustomCursorPosition(currentPoint);
                _cursorManager.EnsureLiveCursorAbove();
            }

            if (!_isSelecting)
            {
                return;
            }

            var x = Math.Min(_startPoint.X, currentPoint.X);
            var y = Math.Min(_startPoint.Y, currentPoint.Y);
            var width = Math.Abs(currentPoint.X - _startPoint.X);
            var height = Math.Abs(currentPoint.Y - _startPoint.Y);

            Canvas.SetLeft(SelectionRectangle, x);
            Canvas.SetTop(SelectionRectangle, y);
            SelectionRectangle.Width = width;
            SelectionRectangle.Height = height;
        }

        private void ScreenshotCapture_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isSelecting)
            {
                return;
            }

            _isSelecting = false;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            var selectionLeft = Canvas.GetLeft(SelectionRectangle);
            var selectionTop = Canvas.GetTop(SelectionRectangle);
            var selectionWidth = SelectionRectangle.Width;
            var selectionHeight = SelectionRectangle.Height;

            if (selectionWidth < 10 || selectionHeight < 10)
            {
                SelectionRectangle.Visibility = Visibility.Collapsed;
                KeepFakeCursorAlive();
                return;
            }

            var x = (int)Math.Round(selectionLeft * _dpiScaleX);
            var y = (int)Math.Round(selectionTop * _dpiScaleY);
            var width = (int)Math.Round(selectionWidth * _dpiScaleX);
            var height = (int)Math.Round(selectionHeight * _dpiScaleY);

            // Hide picker chrome for the bitmap grab; dispose live cursor first so it is not captured.
            _cursorManager?.Dispose();
            _cursorManager = null;
            Opacity = 0;
            System.Threading.Thread.Sleep(120);
            CaptureRegion(x, y, width, height);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                _cursorManager?.Dispose();
                _cursorManager = null;
                Opacity = 0;
                System.Threading.Thread.Sleep(120);
                CaptureFullScreen();
                e.Handled = true;
            }
        }

        private void CaptureFullScreen()
        {
            try
            {
                var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds;
                if (bounds == null)
                {
                    DialogResult = false;
                    Close();
                    return;
                }

                CaptureRegion(0, 0, bounds.Value.Width, bounds.Value.Height);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error capturing full screen: {ex.Message}");
                DialogResult = false;
                Close();
            }
        }

        private void CaptureRegion(int x, int y, int width, int height)
        {
            try
            {
                using var bitmap = new Bitmap(width, height);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(x, y, 0, 0, bitmap.Size);
                }

                CapturedImage = BitmapToBitmapImage(bitmap);
                ImageCaptured = true;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error capturing region: {ex.Message}");
                DialogResult = false;
                Close();
            }
        }

        private static BitmapImage BitmapToBitmapImage(Bitmap bitmap)
        {
            using var memory = new MemoryStream();
            bitmap.Save(memory, ImageFormat.Png);
            memory.Position = 0;

            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = memory;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            bitmapImage.Freeze();
            return bitmapImage;
        }

        public static BitmapImage? CaptureScreenshot(bool useFakeCursor = true, System.Windows.Point? entryFromScreen = null)
        {
            var captureWindow = new ScreenshotCapture(useFakeCursor, entryFromScreen);
            var result = captureWindow.ShowDialog();
            return result == true && captureWindow.ImageCaptured
                ? captureWindow.CapturedImage
                : null;
        }

        private void EnsureKeyboardFocus()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    Activate();
                    Focus();
                    Keyboard.Focus(this);
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"⚠️ Failed to focus screenshot window: {ex.Message}");
                }
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }
}
