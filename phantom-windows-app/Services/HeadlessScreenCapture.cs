using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SecureOverlay.Infrastructure.Hosted.Contracts;
using Forms = System.Windows.Forms;

namespace SecureOverlay.Services
{
    /// <summary>
    /// Headless screen capture for Companion Mode. Captures a full display with
    /// Graphics.CopyFromScreen without creating a Window, activating, or showing a
    /// picker. The interactive <see cref="ScreenshotCapture.CaptureScreenshot"/> path
    /// must never be used from Companion Mode because it steals focus.
    /// </summary>
    public static class HeadlessScreenCapture
    {
        public static BitmapImage? CaptureDisplay(string? displayId)
        {
            try
            {
                var screen = ResolveScreen(displayId);
                if (screen == null) return null;

                var bounds = screen.Bounds;
                using var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
                }

                return BitmapToBitmapImage(bitmap);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"HeadlessScreenCapture.CaptureDisplay failed: {ex.Message}");
                return null;
            }
        }

        public static byte[]? CaptureDisplayAsPng(string? displayId)
        {
            try
            {
                var screen = ResolveScreen(displayId);
                if (screen == null) return null;

                var bounds = screen.Bounds;
                using var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
                }
                using var memory = new MemoryStream();
                bitmap.Save(memory, ImageFormat.Png);
                return memory.ToArray();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"HeadlessScreenCapture.CaptureDisplayAsPng failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Builds a small JPEG thumbnail (long edge ~480) for capture.completed frames.
        /// Returns null if encoding fails. Caller must enforce the 80 KB decoded cap.
        /// </summary>
        public static string? BuildThumbnailJpegBase64(BitmapImage image, int longEdge = 480)
        {
            try
            {
                var width = (int)image.PixelWidth;
                var height = (int)image.PixelHeight;
                if (width == 0 || height == 0) return null;

                var scale = Math.Min(1.0, (double)longEdge / Math.Max(width, height));
                var targetWidth = Math.Max(1, (int)(width * scale));
                var targetHeight = Math.Max(1, (int)(height * scale));

                var encoder = new JpegBitmapEncoder { QualityLevel = 70 };
                var transformed = new TransformedBitmap(image, new ScaleTransform(scale, scale));
                encoder.Frames.Add(BitmapFrame.Create(transformed));
                using var memory = new MemoryStream();
                encoder.Save(memory);
                return Convert.ToBase64String(memory.ToArray());
            }
            catch (Exception ex)
            {
                Log.WriteLine($"HeadlessScreenCapture.BuildThumbnailJpegBase64 failed: {ex.Message}");
                return null;
            }
        }

        public static System.Collections.Generic.List<CompanionDisplayDto> ListDisplays()
        {
            var screens = Forms.Screen.AllScreens;
            var list = new System.Collections.Generic.List<CompanionDisplayDto>(screens.Length);
            for (var i = 0; i < screens.Length; i++)
            {
                var screen = screens[i];
                list.Add(new CompanionDisplayDto
                {
                    Id = i.ToString(),
                    Name = screen.DeviceName,
                    IsDefault = screen.Primary
                });
            }
            return list;
        }

        private static Forms.Screen? ResolveScreen(string? displayId)
        {
            var screens = Forms.Screen.AllScreens;
            if (screens.Length == 0) return null;

            if (string.IsNullOrWhiteSpace(displayId) || displayId == "0")
            {
                return Forms.Screen.PrimaryScreen ?? screens[0];
            }

            if (int.TryParse(displayId, out var index) && index >= 0 && index < screens.Length)
            {
                return screens[index];
            }

            return screens.FirstOrDefault(s => string.Equals(s.DeviceName, displayId, StringComparison.Ordinal))
                ?? screens[0];
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
    }
}
