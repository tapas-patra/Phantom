using System;
using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace SecureOverlay
{
    public partial class FakeCursorWindow : Window
    {
        // ═══════════════════════════════════════════════════════════════
        // P/INVOKE FOR CURSOR CAPTURE
        // ═══════════════════════════════════════════════════════════════
        
        [DllImport("user32.dll")]
        private static extern IntPtr GetCursor();

        [DllImport("user32.dll")]
        private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO pIconInfo);

        [DllImport("user32.dll")]
        private static extern bool GetCursorInfo(ref CURSORINFO pci);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public POINT ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        private const int CURSOR_SHOWING = 0x00000001;

        // ═══════════════════════════════════════════════════════════════
        // P/INVOKE FOR HIDING FROM TASK VIEW
        // ═══════════════════════════════════════════════════════════════
        
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private System.Windows.Threading.DispatcherTimer? _cursorUpdateTimer;
        private IntPtr _lastCursorHandle = IntPtr.Zero;
        private BitmapSource? _cursorBitmap;
        private double _cursorScale = 1.0;
        private double _hotspotPixelX;
        private double _hotspotPixelY;

        public FakeCursorWindow()
        {
            InitializeComponent();
            
            // Start timer to update cursor image
            _cursorUpdateTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100) // Check every 100ms
            };
            _cursorUpdateTimer.Tick += UpdateCursorImage;
            _cursorUpdateTimer.Start();
            
            Log.WriteLine("✓ Fake cursor window created (VISIBLE to screen share)");
            Log.WriteLine("✓ Real-time cursor cloning enabled");
        }

        private void UpdateCursorImage(object? sender, EventArgs e)
        {
            try
            {
                CURSORINFO cursorInfo = new CURSORINFO();
                cursorInfo.cbSize = Marshal.SizeOf(cursorInfo);
                
                if (!GetCursorInfo(ref cursorInfo))
                    return;
                
                if (cursorInfo.flags != CURSOR_SHOWING)
                    return;
                
                // Only update if cursor changed
                if (cursorInfo.hCursor == _lastCursorHandle && _lastCursorHandle != IntPtr.Zero)
                    return;
                
                _lastCursorHandle = cursorInfo.hCursor;
                
                // Get cursor icon info
                ICONINFO iconInfo;
                if (!GetIconInfo(cursorInfo.hCursor, out iconInfo))
                    return;
                
                try
                {
                    // Convert cursor to bitmap
                    var cursorBitmap = System.Drawing.Icon.FromHandle(cursorInfo.hCursor).ToBitmap();
                    
                    // Convert to WPF BitmapSource
                    var handle = cursorBitmap.GetHbitmap();
                    try
                    {
                        var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                            handle,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions()
                        );
                        
                        // Update image
                        Dispatcher.Invoke(() =>
                        {
                            _cursorBitmap = bitmapSource;
                            _hotspotPixelX = iconInfo.xHotspot;
                            _hotspotPixelY = iconInfo.yHotspot;
                            ApplyCursorMetrics();
                        });
                    }
                    finally
                    {
                        DeleteObject(handle);
                    }
                    
                    cursorBitmap.Dispose();
                }
                finally
                {
                    // Clean up icon info handles
                    if (iconInfo.hbmColor != IntPtr.Zero)
                        DeleteObject(iconInfo.hbmColor);
                    if (iconInfo.hbmMask != IntPtr.Zero)
                        DeleteObject(iconInfo.hbmMask);
                }
            }
            catch (Exception ex)
            {
                // Silent fail - don't spam logs
                if (_lastCursorHandle == IntPtr.Zero)
                {
                    Log.WriteLine($"Cursor capture error: {ex.Message}");
                }
            }
        }

        public void PositionAt(double screenX, double screenY)
        {
            // Get DPI scaling factor
            var dpiScale = GetDpiScale();
            
            // Convert physical pixels to device-independent pixels
            double dipX = screenX / dpiScale.DpiScaleX - HotspotDipX(dpiScale);
            double dipY = screenY / dpiScale.DpiScaleY - HotspotDipY(dpiScale);
            
            this.Left = dipX;
            this.Top = dipY;
        }

        public void AnimateToPosition(double screenX, double screenY, int durationMs, Action onComplete)
        {
            try
            {
                var dpiScale = GetDpiScale();
                
                double dipX = screenX / dpiScale.DpiScaleX - HotspotDipX(dpiScale);
                double dipY = screenY / dpiScale.DpiScaleY - HotspotDipY(dpiScale);
                
                var leftAnimation = new DoubleAnimation
                {
                    From = this.Left,
                    To = dipX,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                
                var topAnimation = new DoubleAnimation
                {
                    From = this.Top,
                    To = dipY,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                
                leftAnimation.Completed += (s, e) =>
                {
                    onComplete?.Invoke();
                };
                
                this.BeginAnimation(Window.LeftProperty, leftAnimation);
                this.BeginAnimation(Window.TopProperty, topAnimation);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Animation error: {ex.Message}");
                PositionAt(screenX, screenY);
                onComplete?.Invoke();
            }
        }

        private DpiScale GetDpiScale()
        {
            var source = PresentationSource.FromVisual(this);
            if (source != null)
            {
                return new DpiScale(
                    source.CompositionTarget.TransformToDevice.M11,
                    source.CompositionTarget.TransformToDevice.M22
                );
            }
            
            return new DpiScale(1.0, 1.0);
        }

        public void SetScale(double scale)
        {
            _cursorScale = Math.Max(0.5, Math.Min(2.0, scale));
            ApplyCursorMetrics();
            Log.WriteLine($"Cursor scale applied: {_cursorScale:F2}x");
        }

        public Point GetHotspotScreenPosition()
        {
            var dpiScale = GetDpiScale();
            return new Point(
                (Left + HotspotDipX(dpiScale)) * dpiScale.DpiScaleX,
                (Top + HotspotDipY(dpiScale)) * dpiScale.DpiScaleY
            );
        }

        public void CancelAnimation()
        {
            BeginAnimation(Window.LeftProperty, null);
            BeginAnimation(Window.TopProperty, null);
        }

        private double HotspotDipX(DpiScale dpiScale) => _hotspotPixelX * _cursorScale / dpiScale.DpiScaleX;
        private double HotspotDipY(DpiScale dpiScale) => _hotspotPixelY * _cursorScale / dpiScale.DpiScaleY;

        private void ApplyCursorMetrics()
        {
            if (_cursorBitmap == null) return;
            var dpiScale = GetDpiScale();
            var width = Math.Max(1, _cursorBitmap.PixelWidth * _cursorScale / dpiScale.DpiScaleX);
            var height = Math.Max(1, _cursorBitmap.PixelHeight * _cursorScale / dpiScale.DpiScaleY);
            CursorImage.Source = _cursorBitmap;
            CursorImage.Width = width;
            CursorImage.Height = height;
            Width = width;
            Height = height;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                
                if (hwnd != IntPtr.Zero)
                {
                    int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                    exStyle |= WS_EX_TOOLWINDOW;
                    exStyle |= WS_EX_NOACTIVATE;
                    SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
                    
                    Log.WriteLine("✓ Fake cursor window hidden from Task View");
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"⚠️ Failed to hide fake cursor from Task View: {ex.Message}");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            
            // Stop timer
            _cursorUpdateTimer?.Stop();
            _cursorUpdateTimer = null;
            
            Log.WriteLine("✓ Fake cursor window closed");
        }

        public void EnsureTopmost()
        {
            if (this.IsVisible)
            {
                this.Topmost = false;
                this.Topmost = true;
                this.Activate();
                
                Log.WriteLine("✓ Fake cursor window forced to top");
            }
        }

        /// <summary>
        /// Force immediate cursor image update (don't wait for timer)
        /// </summary>
        public void UpdateCursorImageNow()
        {
            UpdateCursorImage(null, EventArgs.Empty);
            Log.WriteLine("✓ Forced immediate cursor image update");
        }

    }
}
