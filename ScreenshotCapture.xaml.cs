using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices; // ✅ ADD THIS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SecureOverlay.Services;

namespace SecureOverlay
{
    public partial class ScreenshotCapture : Window
    {
        // ═══════════════════════════════════════════════════════════════
        // ✅ ADD THESE P/INVOKE DECLARATIONS
        // ═══════════════════════════════════════════════════════════════
        
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        // ═══════════════════════════════════════════════════════════════
        // EXISTING FIELDS (unchanged)
        // ═══════════════════════════════════════════════════════════════
        
        private System.Windows.Point _startPoint;
        private bool _isSelecting = false;
        public BitmapImage? CapturedImage { get; private set; }
        public bool ImageCaptured { get; private set; } = false;

        private CursorManager? _cursorManager;
        
        private double _dpiScaleX = 1.0;
        private double _dpiScaleY = 1.0;

        // ═══════════════════════════════════════════════════════════════
        // EXISTING CONSTRUCTOR (unchanged)
        // ═══════════════════════════════════════════════════════════════
        
        public ScreenshotCapture()
        {
            InitializeComponent();
            
            WindowProtection.MakeInvisibleToScreenCapture(this);
            
            _cursorManager = new CursorManager(
                this, 
                CustomCursorCanvas, 
                useFakeCursor: true,
                fakeCursorSize: 1.0
            );
            
            this.MouseLeftButtonDown += ScreenshotCapture_MouseLeftButtonDown;
            this.MouseMove += ScreenshotCapture_MouseMove;
            this.MouseLeftButtonUp += ScreenshotCapture_MouseLeftButtonUp;
            
            this.MouseEnter += ScreenshotCapture_MouseEnter;
            this.MouseLeave += ScreenshotCapture_MouseLeave;
            
            this.Loaded += ScreenshotCapture_Loaded;
            this.Closing += (s, e) => _cursorManager?.Dispose();
            
            // ✅ FIX: Ensure screenshot window doesn't block fake cursor
            this.Deactivated += (s, e) => _cursorManager?.DeactivateCustomCursor();
            
            this.Focusable = true;
            this.Focus();
        }

        // ═══════════════════════════════════════════════════════════════
        // ✅ ADD THIS METHOD - Hide from taskbar/Alt+Tab/Win+Tab
        // ═══════════════════════════════════════════════════════════════
        
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                
                if (hwnd != IntPtr.Zero)
                {
                    // Hide from Alt+Tab and Win+Tab
                    int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                    exStyle |= WS_EX_TOOLWINDOW;
                    exStyle |= WS_EX_NOACTIVATE;
                    SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
                    
                    Log.WriteLine("✓ Screenshot window hidden from taskbar/Alt+Tab/Win+Tab");
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"⚠️ Failed to hide screenshot window: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // ALL OTHER METHODS REMAIN EXACTLY THE SAME
        // ═══════════════════════════════════════════════════════════════

        private void ScreenshotCapture_Loaded(object sender, RoutedEventArgs e)
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
                Log.WriteLine($"DPI Scale: {_dpiScaleX}x (X), {_dpiScaleY}x (Y)");
            }
            
            // ✅ Force canvas to full window size
            CustomCursorCanvas.Width = this.ActualWidth;
            CustomCursorCanvas.Height = this.ActualHeight;
            Log.WriteLine($"✓ Canvas sized: {CustomCursorCanvas.Width} x {CustomCursorCanvas.Height}");
            
            // Now activate cursor
            _cursorManager?.ActivateCustomCursor();
            
            this.Focus();
            Keyboard.Focus(this);
            
            Log.WriteLine("✓ Screenshot cursor activated and keyboard focused");
        }
        private void ScreenshotCapture_MouseEnter(object sender, MouseEventArgs e)
        {
            _cursorManager?.ActivateCustomCursor();
        }

        private void ScreenshotCapture_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isSelecting)
            {
                _cursorManager?.DeactivateCustomCursor();
            }
        }

        private void ScreenshotCapture_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(this);
            _isSelecting = true;
            
            SelectionRectangle.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionRectangle, _startPoint.X);
            Canvas.SetTop(SelectionRectangle, _startPoint.Y);
            SelectionRectangle.Width = 0;
            SelectionRectangle.Height = 0;
        }

        private void ScreenshotCapture_MouseMove(object sender, MouseEventArgs e)
        {
            var currentPoint = e.GetPosition(this);
            
            _cursorManager?.UpdateCustomCursorPosition(currentPoint);
            
            if (!_isSelecting) return;

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
            if (!_isSelecting) return;
            
            _isSelecting = false;

            var selectionLeft = Canvas.GetLeft(SelectionRectangle);
            var selectionTop = Canvas.GetTop(SelectionRectangle);
            var selectionWidth = SelectionRectangle.Width;
            var selectionHeight = SelectionRectangle.Height;

            if (selectionWidth < 10 || selectionHeight < 10)
            {
                Log.WriteLine("Selection too small - cancelling");
                SelectionRectangle.Visibility = Visibility.Collapsed;
                return;
            }

            int x = (int)Math.Round(selectionLeft * _dpiScaleX);
            int y = (int)Math.Round(selectionTop * _dpiScaleY);
            int width = (int)Math.Round(selectionWidth * _dpiScaleX);
            int height = (int)Math.Round(selectionHeight * _dpiScaleY);

            Log.WriteLine($"WPF coordinates: ({selectionLeft:F0}, {selectionTop:F0}) {selectionWidth:F0}x{selectionHeight:F0}");
            Log.WriteLine($"Physical pixels (DPI scaled): ({x}, {y}) {width}x{height}");
            
            _cursorManager?.DeactivateCustomCursor();
            
            this.Opacity = 0;
            System.Threading.Thread.Sleep(200);
            
            CaptureRegion(x, y, width, height);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            Log.WriteLine($"Key pressed: {e.Key}");
            
            if (e.Key == Key.Escape)
            {
                Log.WriteLine("Screenshot cancelled by user");
                
                _cursorManager?.DeactivateCustomCursor();
                
                this.DialogResult = false;
                Close();
                
                e.Handled = true;
            }
            else if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                Log.WriteLine("Capturing full screen");
                
                _cursorManager?.DeactivateCustomCursor();
                
                this.Opacity = 0;
                System.Threading.Thread.Sleep(150);
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
                    Log.WriteLine("Failed to get screen bounds");
                    this.DialogResult = false;
                    Close();
                    return;
                }
                
                CaptureRegion(0, 0, bounds.Value.Width, bounds.Value.Height);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error capturing full screen: {ex.Message}");
                this.DialogResult = false;
                Close();
            }
        }

        private void CaptureRegion(int x, int y, int width, int height)
        {
            try
            {
                Log.WriteLine($"Creating bitmap: {width}x{height}");
                Log.WriteLine($"Capturing from screen position: ({x}, {y})");
                
                using (Bitmap bitmap = new Bitmap(width, height))
                {
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.CopyFromScreen(x, y, 0, 0, bitmap.Size);
                    }

                    CapturedImage = BitmapToBitmapImage(bitmap);
                    ImageCaptured = true;
                    
                    Log.WriteLine($"✓ Screenshot captured: {width}x{height}");
                    this.DialogResult = true;
                    Close();
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error capturing region: {ex.Message}");
                Log.WriteLine($"Stack trace: {ex.StackTrace}");
                this.DialogResult = false;
                Close();
            }
        }

        private BitmapImage BitmapToBitmapImage(Bitmap bitmap)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, ImageFormat.Png);
                memory.Position = 0;

                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                return bitmapImage;
            }
        }

        public static BitmapImage? CaptureScreenshot()
        {
            var captureWindow = new ScreenshotCapture();
            var result = captureWindow.ShowDialog();
            
            if (result == true && captureWindow.ImageCaptured)
            {
                return captureWindow.CapturedImage;
            }
            
            return null;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // ✅ CRITICAL: Force canvas to match window size for maximized window
            CustomCursorCanvas.Width = this.ActualWidth;
            CustomCursorCanvas.Height = this.ActualHeight;
            
            Log.WriteLine($"✓ CustomCursorCanvas sized to: {CustomCursorCanvas.Width} x {CustomCursorCanvas.Height}");
            
            // Ensure canvas is on top
            Panel.SetZIndex(CustomCursorCanvas, 9999);
        }
    }
}
