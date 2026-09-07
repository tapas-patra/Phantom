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
        private FakeCursorWindow? _protectedCursorWindow;
        private bool _useFakeCursor = true;
        private bool _clickThroughActive;
        private bool _applicationFocusActive;
        private bool _systemCursorHidden;
        private int _transitionGeneration;

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

        [DllImport("user32.dll")]
        private static extern int ShowCursor(bool bShow);

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
            _applicationFocusActive = parentWindow.IsActive;
            
            // Create debounce timers
            _activateTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(DEBOUNCE_DELAY_MS)
            };
            _activateTimer.Tick += (s, e) =>
            {
                _activateTimer.Stop();
                if (CanActivateCursor())
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
                _protectedCursorWindow = new FakeCursorWindow(captureProtected: true);
                
                // IMPORTANT: Show window once to initialize, then hide it
                _fakeCursorWindow.Show();
                _fakeCursorWindow.Hide();
                _protectedCursorWindow.Show();
                _protectedCursorWindow.Hide();
                
                _fakeCursorWindow.SetScale(fakeCursorSize);
                _protectedCursorWindow.SetScale(1.0);
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
            if (!CanActivateCursor())
                return;

            _deactivateTimer?.Stop();

            if (_customCursorActive)
                return;

            _activateTimer?.Stop();
            _activateTimer?.Start();
        }

        private void ActivateCustomCursorImmediate()
        {
            if (_customCursorActive || !CanActivateCursor())
                return;

            try
            {
                _transitionGeneration++;
                _fakeCursorWindow?.CancelAnimation();
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

                if (_protectedCursorWindow != null)
                {
                    _protectedCursorWindow.UpdateCursorImageNow();
                    _protectedCursorWindow.Topmost = true;
                    _protectedCursorWindow.PositionAt(cursorPos.X, cursorPos.Y);
                    _protectedCursorWindow.Show();
                }
                
                // Hide system cursor
                if (_parentWindow != null)
                {
                    _parentWindow.Cursor = System.Windows.Input.Cursors.None;
                }
                HideSystemCursor();
                
                // The moving cursor is a protected window so it also works over WebView2.
                if (_cursorDot != null) _cursorDot.Visibility = Visibility.Collapsed;
                if (_cursorBadge != null) _cursorBadge.Visibility = Visibility.Collapsed;
                UpdateCursorVisualState();
                
                Log.WriteLine($"🖱️ Cursor locked at entry: ({cursorPos.X}, {cursorPos.Y})");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"✗ Failed to activate custom cursor: {ex.Message}");
                ResetCursorImmediately();
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

        private void DeactivateCustomCursorImmediate(bool force = false)
        {
            if (!_customCursorActive || (_isSuspended && !force))
                return;

            try
            {
                var transitionGeneration = ++_transitionGeneration;

                // Get exit position
                POINT exitPos;
                GetCursorPos(out exitPos);
                Point exitPoint = new Point(exitPos.X, exitPos.Y);
                
                var animationStart = _fakeCursorWindow?.GetHotspotScreenPosition() ?? _lastCursorPosition;

                // Calculate from the stationary decoy, not the moving protected cursor.
                double distance = Math.Sqrt(
                    Math.Pow(exitPoint.X - animationStart.X, 2) +
                    Math.Pow(exitPoint.Y - animationStart.Y, 2)
                );
                
                // Keep the handoff visible, but short enough that it cannot be mistaken for lag.
                const double PIXELS_PER_MS = 1.4;
                
                int animationMs = (int)(distance / PIXELS_PER_MS);
                
                // Clamp between reasonable limits
                animationMs = Math.Max(90, Math.Min(320, animationMs));
                
                Log.WriteLine($"🖱️ Animating cursor: ({animationStart.X:F0}, {animationStart.Y:F0}) → ({exitPoint.X}, {exitPoint.Y})");
                Log.WriteLine($"   Distance: {distance:F0}px | Duration: {animationMs}ms | Speed: {(distance / animationMs):F1}px/ms");
                
                if (_fakeCursorWindow != null)
                {
                    // Animate the fake cursor window position
                    _fakeCursorWindow.AnimateToPosition(exitPoint.X, exitPoint.Y, animationMs, () =>
                    {
                        if (transitionGeneration != _transitionGeneration || _customCursorActive)
                            return;

                        // After animation completes, hide fake cursor and show real cursor
                        _fakeCursorWindow.Hide();
                        
                        // Restore system cursor
                        if (_parentWindow != null)
                        {
                            _parentWindow.Cursor = null;
                        }
                        RestoreSystemCursor();
                        
                        Log.WriteLine("🖱️ Cursor unlocked at exit");
                    });
                }
                else
                {
                    RestoreSystemCursor();
                }

                _protectedCursorWindow?.Hide();
                
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
                _protectedCursorWindow?.Hide();
                if (_parentWindow != null)
                {
                    _parentWindow.Cursor = null;
                }
                RestoreSystemCursor();
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
            if (CanActivateCursor() && _customCursorActive)
            {
                if (_protectedCursorWindow != null && GetCursorPos(out var cursorPos))
                {
                    _protectedCursorWindow.PositionAt(cursorPos.X, cursorPos.Y);
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

            if (_protectedCursorWindow != null)
            {
                _cursorDot.Visibility = Visibility.Collapsed;
                _cursorBadge.Visibility = Visibility.Collapsed;
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

        public void SetClickThroughActive(bool active)
        {
            _clickThroughActive = active;
            if (active)
            {
                _activateTimer?.Stop();
                _deactivateTimer?.Stop();
                DeactivateCustomCursorImmediate(force: true);
                return;
            }

            TryActivateForCurrentPointer();
        }

        public void SetApplicationFocusActive(bool active)
        {
            _applicationFocusActive = active;
            if (!active)
            {
                ResetCursorImmediately();
                return;
            }

            TryActivateForCurrentPointer();
        }

        private bool CanActivateCursor()
        {
            return _useFakeCursor && !_isSuspended && !_clickThroughActive && _applicationFocusActive;
        }

        private void TryActivateForCurrentPointer()
        {
            if (!CanActivateCursor() || !IsPointerInsideParentWindow())
                return;

            ActivateCustomCursor();
        }

        public bool IsPointerInsideParentWindow()
        {
            if (_parentWindow?.IsVisible != true || !GetCursorPos(out var cursorPos))
                return false;

            var local = _parentWindow.PointFromScreen(new Point(cursorPos.X, cursorPos.Y));
            return local.X >= 0 && local.Y >= 0 &&
                   local.X <= _parentWindow.ActualWidth && local.Y <= _parentWindow.ActualHeight;
        }

        private void ResetCursorImmediately()
        {
            _transitionGeneration++;
            _activateTimer?.Stop();
            _deactivateTimer?.Stop();
            _embeddedSurfaceCursorActive = false;
            _customCursorActive = false;

            if (_cursorDot != null) _cursorDot.Visibility = Visibility.Collapsed;
            if (_cursorBadge != null) _cursorBadge.Visibility = Visibility.Collapsed;
            if (_fakeCursorWindow != null)
            {
                _fakeCursorWindow.CancelAnimation();
                _fakeCursorWindow.Hide();
            }
            if (_protectedCursorWindow != null)
            {
                _protectedCursorWindow.CancelAnimation();
                _protectedCursorWindow.Hide();
            }
            if (_parentWindow != null) _parentWindow.Cursor = null;
            RestoreSystemCursor();
        }

        private void HideSystemCursor()
        {
            if (_systemCursorHidden) return;
            ShowCursor(false);
            _systemCursorHidden = true;
        }

        private void RestoreSystemCursor()
        {
            if (!_systemCursorHidden) return;
            ShowCursor(true);
            _systemCursorHidden = false;
        }

        public void ShowFakeCursorPreview()
        {
            if (_fakeCursorWindow != null && CanActivateCursor())
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
                ResetCursorImmediately();

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

                if (_protectedCursorWindow != null)
                {
                    try
                    {
                        _protectedCursorWindow.Hide();
                        _protectedCursorWindow.Close();
                    }
                    finally
                    {
                        _protectedCursorWindow = null;
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
