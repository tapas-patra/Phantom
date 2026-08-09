using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SecureOverlay
{
    /// <summary>
    /// Two-cursor system: Hides system cursor, shows custom cursor inside overlay + fake cursor outside
    /// </summary>
    public class CursorManager
    {
        // Custom cursor visual (inside overlay - invisible to screen share)
        private Canvas? _cursorCanvas;
        private Ellipse? _cursorDot;
        private Border? _cursorBadge;
        private TextBlock? _cursorGlyph;
        private Window? _parentWindow;
        private bool _customCursorActive = false;
        private bool _embeddedSurfaceCursorActive;
        private CursorVisualMode _cursorVisualMode = CursorVisualMode.Default;

        // Fake cursor window (visible to screen share)
        private FakeCursorWindow? _fakeCursorWindow;
        private bool _useFakeCursor = true;

        // Debounce timers to prevent flickering at borders
        private System.Windows.Threading.DispatcherTimer? _activateTimer;
        private System.Windows.Threading.DispatcherTimer? _deactivateTimer;
        private const int DEBOUNCE_DELAY_MS = 50;

        // Suspend flag to prevent changes during window drag
        private bool _isSuspended = false;

        // NEW: Track last cursor position for smooth exit animation
        private Point _lastCursorPosition;

        // Windows API for getting exact cursor position
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private enum CursorVisualMode
        {
            Default,
            ResizeHorizontal,
            ResizeVertical,
            ResizeDiagonalForward,
            ResizeDiagonalBackward
        }

        public CursorManager(Window parentWindow, Canvas cursorCanvas, bool useFakeCursor, double fakeCursorSize)
        {
            _parentWindow = parentWindow;
            _cursorCanvas = cursorCanvas;
            _useFakeCursor = useFakeCursor;
            
            // Create debounce timers
            _activateTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(DEBOUNCE_DELAY_MS)
            };
            _activateTimer.Tick += (s, e) =>
            {
                _activateTimer.Stop();
                if (!_isSuspended)
                    ActivateCustomCursorImmediate();
            };

            _deactivateTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(DEBOUNCE_DELAY_MS)
            };
            _deactivateTimer.Tick += (s, e) =>
            {
                _deactivateTimer.Stop();
                if (!_isSuspended)
                    DeactivateCustomCursorImmediate();
            };
            
            // Only create custom cursor if fake cursor is enabled
            if (_useFakeCursor)
            {
                CreateCustomCursor();
                _fakeCursorWindow = new FakeCursorWindow();
                
                // IMPORTANT: Show window once to initialize, then hide it
                _fakeCursorWindow.Show();
                _fakeCursorWindow.Hide();
                
                _fakeCursorWindow.SetScale(fakeCursorSize);
                Log.WriteLine($"✓ Two-cursor system enabled (fake cursor size: {fakeCursorSize * 100:F0}%)");
            }
            else
            {
                Log.WriteLine("✓ Fake cursor disabled - using normal system cursor");
            }
        }

        private void CreateCustomCursor()
        {
            // Create a custom cursor visual (glowing dot)
            _cursorDot = new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = new SolidColorBrush(Color.FromArgb(200, 0, 170, 255)),
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 2,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };

            // Add glow effect
            _cursorDot.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0, 170, 255),
                BlurRadius = 10,
                ShadowDepth = 0,
                Opacity = 1
            };

            // Add to canvas
            if (_cursorCanvas != null)
            {
                _cursorCanvas.Children.Add(_cursorDot);
                Canvas.SetZIndex(_cursorDot, 10000);
            }

            _cursorGlyph = new TextBlock
            {
                Text = "↔",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };

            _cursorBadge = new Border
            {
                Width = 22,
                Height = 22,
                Background = new SolidColorBrush(Color.FromArgb(210, 12, 24, 36)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(230, 187, 233, 255)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(11),
                Child = _cursorGlyph,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(90, 190, 255),
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.95
                }
            };

            if (_cursorCanvas != null)
            {
                _cursorCanvas.Children.Add(_cursorBadge);
                Canvas.SetZIndex(_cursorBadge, 10001);
            }
        }

        public void SuspendCursorChanges()
        {
            _isSuspended = true;
            _activateTimer?.Stop();
            _deactivateTimer?.Stop();
            Log.WriteLine("🔒 Cursor changes suspended (window dragging)");
        }

        public void ResumeCursorChanges()
        {
            _isSuspended = false;
            Log.WriteLine("🔓 Cursor changes resumed");
        }

        public void ActivateCustomCursor()
        {
            if (!_useFakeCursor || _isSuspended)
                return;

            _deactivateTimer?.Stop();

            if (_customCursorActive)
                return;

            _activateTimer?.Stop();
            _activateTimer?.Start();
        }

        private void ActivateCustomCursorImmediate()
        {
            if (_customCursorActive || _isSuspended)
                return;

            try
            {
                _customCursorActive = true;
                
                // Get EXACT cursor position
                POINT cursorPos;
                GetCursorPos(out cursorPos);
                
                // Store entry position
                _lastCursorPosition = new Point(cursorPos.X, cursorPos.Y);
                
                // Show fake cursor at entry position
                if (_fakeCursorWindow != null)
                {
                    // ✅ FIX: Update cursor image BEFORE showing window
                    _fakeCursorWindow.UpdateCursorImageNow();
                    
                    _fakeCursorWindow.Topmost = true;
                    _fakeCursorWindow.PositionAt(cursorPos.X, cursorPos.Y);
                    _fakeCursorWindow.Show();
                    
                    // ✅ DIAGNOSTIC: Log window state
                    Log.WriteLine($"✓ Fake cursor window shown:");
                    Log.WriteLine($"  Position: ({_fakeCursorWindow.Left}, {_fakeCursorWindow.Top})");
                    Log.WriteLine($"  Size: {_fakeCursorWindow.Width} x {_fakeCursorWindow.Height}");
                    Log.WriteLine($"  Visible: {_fakeCursorWindow.IsVisible}");
                    Log.WriteLine($"  Topmost: {_fakeCursorWindow.Topmost}");
                    Log.WriteLine($"  Opacity: {_fakeCursorWindow.Opacity}");
                }
                
                // Hide system cursor
                if (_parentWindow != null)
                {
                    _parentWindow.Cursor = System.Windows.Input.Cursors.None;
                }
                
                // Show custom cursor
                if (_cursorDot != null)
                {
                    _cursorDot.Visibility = Visibility.Visible;
                }
                UpdateCursorVisualState();
                
                Log.WriteLine($"🖱️ Cursor locked at entry: ({cursorPos.X}, {cursorPos.Y})");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"✗ Failed to activate custom cursor: {ex.Message}");
            }
        }


        public void DeactivateCustomCursor()
        {
            _embeddedSurfaceCursorActive = false;
            UpdateCursorVisualState();

            if (!_useFakeCursor || _isSuspended)
                return;

            _activateTimer?.Stop();

            if (!_customCursorActive)
                return;

            _deactivateTimer?.Stop();
            _deactivateTimer?.Start();
        }

        private void DeactivateCustomCursorImmediate()
        {
            if (!_customCursorActive || _isSuspended)
                return;

            try
            {
                // Get exit position
                POINT exitPos;
                GetCursorPos(out exitPos);
                Point exitPoint = new Point(exitPos.X, exitPos.Y);
                
                // Calculate distance
                double distance = Math.Sqrt(
                    Math.Pow(exitPoint.X - _lastCursorPosition.X, 2) + 
                    Math.Pow(exitPoint.Y - _lastCursorPosition.Y, 2)
                );
                
                // NEW: Use constant speed for natural movement
                // Speed: 600 pixels per second (0.6 pixels per millisecond)
                const double PIXELS_PER_MS = 0.6;
                
                int animationMs = (int)(distance / PIXELS_PER_MS);
                
                // Clamp between reasonable limits
                animationMs = Math.Max(100, Math.Min(800, animationMs));
                
                Log.WriteLine($"🖱️ Animating cursor: ({_lastCursorPosition.X:F0}, {_lastCursorPosition.Y:F0}) → ({exitPoint.X}, {exitPoint.Y})");
                Log.WriteLine($"   Distance: {distance:F0}px | Duration: {animationMs}ms | Speed: {(distance / animationMs):F1}px/ms");
                
                if (_fakeCursorWindow != null)
                {
                    // Animate the fake cursor window position
                    _fakeCursorWindow.AnimateToPosition(exitPoint.X, exitPoint.Y, animationMs, () =>
                    {
                        // After animation completes, hide fake cursor and show real cursor
                        _fakeCursorWindow.Hide();
                        
                        // Restore system cursor
                        if (_parentWindow != null)
                        {
                            _parentWindow.Cursor = null;
                        }
                        
                        Log.WriteLine("🖱️ Cursor unlocked at exit");
                    });
                }
                
                // Hide custom cursor immediately
                if (_cursorDot != null)
                {
                    _cursorDot.Visibility = Visibility.Collapsed;
                }

                if (_cursorBadge != null)
                {
                    _cursorBadge.Visibility = Visibility.Collapsed;
                }
                
                _customCursorActive = false;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"✗ Failed to deactivate custom cursor: {ex.Message}");
                
                // Fallback: just hide everything
                if (_fakeCursorWindow != null)
                {
                    _fakeCursorWindow.Hide();
                }
                if (_parentWindow != null)
                {
                    _parentWindow.Cursor = null;
                }
                _customCursorActive = false;
            }
        }


        // NEW: Update last cursor position (called from MouseMove)
        public void UpdateLastCursorPosition()
        {
            if (_customCursorActive)
            {
                POINT cursorPos;
                GetCursorPos(out cursorPos);
                _lastCursorPosition = new Point(cursorPos.X, cursorPos.Y);
            }
        }

        public void UpdateCustomCursorPosition(Point position)
        {
            if (_useFakeCursor && _cursorDot != null && _customCursorActive && !_isSuspended)
            {
                // Position custom cursor
                Canvas.SetLeft(_cursorDot, position.X - 8);
                Canvas.SetTop(_cursorDot, position.Y - 8);
                if (_cursorBadge != null)
                {
                    Canvas.SetLeft(_cursorBadge, position.X - (_cursorBadge.Width / 2));
                    Canvas.SetTop(_cursorBadge, position.Y - (_cursorBadge.Height / 2));
                }
                
                // Update last position for smooth exit
                UpdateLastCursorPosition();
            }
        }

        public void SetEmbeddedSurfaceCursorActive(bool active)
        {
            _embeddedSurfaceCursorActive = active;
            UpdateCursorVisualState();
        }

        public void SetResizeCursorHint(string resizeTag)
        {
            _cursorVisualMode = resizeTag switch
            {
                "Left" or "Right" => CursorVisualMode.ResizeHorizontal,
                "Top" or "Bottom" => CursorVisualMode.ResizeVertical,
                "TopLeft" or "BottomRight" => CursorVisualMode.ResizeDiagonalBackward,
                "TopRight" or "BottomLeft" => CursorVisualMode.ResizeDiagonalForward,
                _ => CursorVisualMode.Default
            };
            UpdateCursorVisualState();
        }

        public void ClearResizeCursorHint()
        {
            _cursorVisualMode = CursorVisualMode.Default;
            UpdateCursorVisualState();
        }

        private void UpdateCursorVisualState()
        {
            if (_cursorDot == null || _cursorBadge == null || _cursorGlyph == null)
            {
                return;
            }

            if (!_customCursorActive)
            {
                _cursorDot.Visibility = Visibility.Collapsed;
                _cursorBadge.Visibility = Visibility.Collapsed;
                return;
            }

            if (_embeddedSurfaceCursorActive)
            {
                _cursorDot.Visibility = Visibility.Collapsed;
                _cursorBadge.Visibility = Visibility.Collapsed;
                return;
            }

            if (_cursorVisualMode == CursorVisualMode.Default)
            {
                _cursorDot.Visibility = Visibility.Visible;
                _cursorBadge.Visibility = Visibility.Collapsed;
                return;
            }

            _cursorDot.Visibility = Visibility.Collapsed;
            _cursorBadge.Visibility = Visibility.Visible;
            _cursorGlyph.Text = _cursorVisualMode switch
            {
                CursorVisualMode.ResizeHorizontal => "↔",
                CursorVisualMode.ResizeVertical => "↕",
                CursorVisualMode.ResizeDiagonalForward => "⤢",
                CursorVisualMode.ResizeDiagonalBackward => "⤡",
                _ => "↔"
            };
        }

        public void UpdateFakeCursorSize(double scale)
        {
            _fakeCursorWindow?.SetScale(scale);
        }

        public void ShowFakeCursorPreview()
        {
            if (_fakeCursorWindow != null)
            {
                POINT cursorPos;
                GetCursorPos(out cursorPos);
                
                _fakeCursorWindow.PositionAt(cursorPos.X, cursorPos.Y);
                _fakeCursorWindow.Show();
                _fakeCursorWindow.EnsureTopmost();
                _fakeCursorWindow.Topmost = true;
            }
        }

        public void HideFakeCursorPreview()
        {
            if (_fakeCursorWindow != null && !_customCursorActive)
            {
                _fakeCursorWindow.Hide();
            }
        }

        public void Dispose()
        {
            Log.WriteLine("Disposing cursor manager...");
            
            try
            {
                // 1. Stop all timers
                if (_activateTimer != null)
                {
                    _activateTimer.Stop();
                    _activateTimer.Tick -= null;
                    _activateTimer = null;
                    Log.WriteLine("  ✓ Activate timer stopped");
                }
                
                if (_deactivateTimer != null)
                {
                    _deactivateTimer.Stop();
                    _deactivateTimer.Tick -= null;
                    _deactivateTimer = null;
                    Log.WriteLine("  ✓ Deactivate timer stopped");
                }
                
                // 2. Restore system cursor immediately
                if (_parentWindow != null)
                {
                    _parentWindow.Cursor = null;
                    Log.WriteLine("  ✓ System cursor restored");
                }
                
                // 3. Hide custom cursor
                if (_cursorDot != null && _cursorCanvas != null)
                {
                    _cursorDot.Visibility = Visibility.Collapsed;
                    _cursorCanvas.Children.Remove(_cursorDot);
                    _cursorDot = null;
                    Log.WriteLine("  ✓ Custom cursor removed");
                }

                if (_cursorBadge != null && _cursorCanvas != null)
                {
                    _cursorBadge.Visibility = Visibility.Collapsed;
                    _cursorCanvas.Children.Remove(_cursorBadge);
                    _cursorBadge = null;
                    _cursorGlyph = null;
                    Log.WriteLine("  ✓ Cursor resize badge removed");
                }
                
                // 4. FORCE CLOSE fake cursor window
                if (_fakeCursorWindow != null)
                {
                    Log.WriteLine("  Closing fake cursor window...");
                    
                    try
                    {
                        // Stop any running animations
                        _fakeCursorWindow.BeginAnimation(Window.LeftProperty, null);
                        _fakeCursorWindow.BeginAnimation(Window.TopProperty, null);
                        
                        // Hide and close
                        _fakeCursorWindow.Hide();
                        _fakeCursorWindow.Close();
                        
                        Log.WriteLine("  ✓ Fake cursor window closed");
                    }
                    catch (Exception ex)
                    {
                        Log.WriteLine($"  Warning: Error closing fake cursor window: {ex.Message}");
                    }
                    finally
                    {
                        _fakeCursorWindow = null;
                    }
                }
                
                _customCursorActive = false;
                _isSuspended = false;
                
                Log.WriteLine("✓ Cursor manager fully disposed");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error in cursor manager disposal: {ex.Message}");
            }
        }

    }
}
