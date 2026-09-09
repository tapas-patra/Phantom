using System;
using System.Collections.Generic;
using System.Linq;

namespace SecureOverlay.Services
{
    public static class MultimodalContentBuilder
    {
        public static IReadOnlyList<string> Normalize(IReadOnlyList<string>? imagesBase64)
        {
            if (imagesBase64 == null || imagesBase64.Count == 0)
            {
                return Array.Empty<string>();
            }

            return imagesBase64
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Take(3)
                .ToArray();
        }

        public static bool HasImages(IReadOnlyList<string>? imagesBase64)
            => Normalize(imagesBase64).Count > 0;

        public static object[] BuildOpenAiContent(string text, IReadOnlyList<string> images, bool mistralDirectUrl = false)
        {
            var parts = new List<object>(1 + images.Count)
            {
                new { type = "text", text }
            };

            foreach (var image in images)
            {
                if (mistralDirectUrl)
                {
                    parts.Add(new
                    {
                        type = "image_url",
                        image_url = $"data:image/png;base64,{image}"
                    });
                }
                else
                {
                    parts.Add(new
                    {
                        type = "image_url",
                        image_url = new { url = $"data:image/png;base64,{image}" }
                    });
                }
            }

            return parts.ToArray();
        }

        public static object[] BuildClaudeContent(string text, IReadOnlyList<string> images)
        {
            var parts = new List<object>(1 + images.Count)
            {
                new { type = "text", text }
            };

            foreach (var image in images)
            {
                parts.Add(new
                {
                    type = "image",
                    source = new
                    {
                        type = "base64",
                        media_type = "image/png",
                        data = image
                    }
                });
            }

            return parts.ToArray();
        }

        public static List<object> BuildGeminiParts(string text, IReadOnlyList<string> images)
        {
            var parts = new List<object>(1 + images.Count)
            {
                new { text }
            };

            foreach (var image in images)
            {
                parts.Add(new
                {
                    inline_data = new
                    {
                        mime_type = "image/png",
                        data = image
                    }
                });
            }

            return parts;
        }
    }
}
