using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Wpf;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SecureOverlay.Platform.Windows;

namespace SecureOverlay
{
    public static class MarkdownHelper
    {
        private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
            .UseSupportedExtensions()
            .Build();
        private static readonly Regex MermaidBlockRegex = new(
            @"```mermaid\s*(?<code>[\s\S]*?)```",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Lazy<Task<CoreWebView2Environment>> MermaidEnvironment = new(
            () => CoreWebView2Environment.CreateAsync(null, WindowsAppPaths.WebView2CachePath));
        private const string MermaidAssetHost = "phantom-assets.local";
        private static readonly string MermaidAssetFolder = Path.Combine(AppContext.BaseDirectory, "assets", "mermaid");
        private static readonly string MermaidScriptPath = Path.Combine(MermaidAssetFolder, "mermaid.min.js");

        public static void AppendMarkdown(FlowDocument document, string markdown, bool isUser = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(markdown))
                    return;

                foreach (var segment in SplitMarkdownSegments(markdown))
                {
                    if (string.IsNullOrWhiteSpace(segment.Content))
                    {
                        continue;
                    }

                    if (segment.IsMermaid)
                    {
                        AppendMermaidBlock(document, segment.Content);
                    }
                    else
                    {
                        AppendStandardMarkdown(document, segment.Content, isUser);
                    }
                }

                document.Blocks.Add(new Paragraph(new Run(""))
                {
                    Margin = new Thickness(0, 0, 0, 10)
                });
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Markdown rendering error: {ex.Message}");

                var paragraph = new Paragraph(new Run(markdown))
                {
                    Foreground = isUser ? Brushes.LightBlue : Brushes.White,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 14
                };
                document.Blocks.Add(paragraph);
            }
        }

        private static void AppendStandardMarkdown(FlowDocument document, string markdown, bool isUser)
        {
            var tempDoc = Markdig.Wpf.Markdown.ToFlowDocument(markdown, _pipeline);
            StyleDocument(tempDoc, isUser);

            while (tempDoc.Blocks.Count > 0)
            {
                var block = tempDoc.Blocks.FirstBlock;
                if (block == null)
                {
                    break;
                }

                tempDoc.Blocks.Remove(block);
                document.Blocks.Add(block);
            }
        }

        private static IReadOnlyList<MarkdownSegment> SplitMarkdownSegments(string markdown)
        {
            var segments = new List<MarkdownSegment>();
            var currentIndex = 0;
            foreach (Match match in MermaidBlockRegex.Matches(markdown))
            {
                if (match.Index > currentIndex)
                {
                    segments.Add(new MarkdownSegment(markdown[currentIndex..match.Index], false));
                }

                segments.Add(new MarkdownSegment(match.Groups["code"].Value.Trim(), true));
                currentIndex = match.Index + match.Length;
            }

            if (currentIndex < markdown.Length)
            {
                segments.Add(new MarkdownSegment(markdown[currentIndex..], false));
            }

            return segments;
        }

        private static void AppendMermaidBlock(FlowDocument document, string mermaidCode)
        {
            var diagramHeight = CalculateDiagramHeight(mermaidCode);
            var container = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 8, 0, 8)
            };
            container.Children.Add(new TextBlock
            {
                Text = "Diagram",
                Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var webView = new WebView2
            {
                Width = 720,
                Height = diagramHeight,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 18, 24)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(72, 78, 92)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(0),
                Child = webView
            };
            container.Children.Add(border);

            document.Blocks.Add(new BlockUIContainer(container));
            _ = InitializeMermaidBlockAsync(webView, mermaidCode, diagramHeight, container);
        }

        private static async Task InitializeMermaidBlockAsync(
            WebView2 webView,
            string mermaidCode,
            double fallbackHeight,
            Panel container)
        {
            try
            {
                Directory.CreateDirectory(WindowsAppPaths.WebView2CachePath);
                if (!File.Exists(MermaidScriptPath))
                {
                    throw new FileNotFoundException("Local Mermaid bundle not found.", MermaidScriptPath);
                }

                var env = await MermaidEnvironment.Value;
                await webView.EnsureCoreWebView2Async(env);
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    MermaidAssetHost,
                    MermaidAssetFolder,
                    CoreWebView2HostResourceAccessKind.Allow);
                webView.NavigateToString(BuildMermaidHtml(mermaidCode));

                void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
                {
                    webView.NavigationCompleted -= OnNavigationCompleted;
                    _ = ResizeMermaidBlockAsync(webView, fallbackHeight);
                }

                webView.NavigationCompleted += OnNavigationCompleted;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Mermaid rendering error: {ex.Message}");
                container.Children.Clear();
                container.Children.Add(new TextBlock
                {
                    Text = "Diagram",
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                    FontFamily = new FontFamily("Segoe UI Semibold"),
                    FontSize = 13,
                    Margin = new Thickness(0, 0, 0, 6)
                });
                container.Children.Add(BuildMermaidFallback(mermaidCode));
            }
        }

        private static async Task ResizeMermaidBlockAsync(WebView2 webView, double fallbackHeight)
        {
            try
            {
                var rawHeight = await webView.ExecuteScriptAsync("Math.max(document.body.scrollHeight, document.documentElement.scrollHeight).toString()");
                if (double.TryParse(rawHeight.Trim('"'), out var measuredHeight))
                {
                    webView.Height = Math.Clamp(measuredHeight + 16d, 220d, 720d);
                    return;
                }
            }
            catch
            {
            }

            webView.Height = fallbackHeight;
        }

        private static FrameworkElement BuildMermaidFallback(string mermaidCode)
        {
            return new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.FromRgb(72, 78, 92)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Child = new TextBlock
                {
                    Text = $"```mermaid\n{mermaidCode}\n```",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 127)),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 13
                }
            };
        }

        private static string BuildMermaidHtml(string mermaidCode)
        {
            var mermaidJson = JsonSerializer.Serialize(mermaidCode);
            return $$"""
<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8" />
  <style>
    html, body {
      margin: 0;
      padding: 0;
      background: #0f1218;
      color: #ffffff;
      overflow: auto;
      font-family: "Segoe UI", sans-serif;
    }
    #diagram {
      padding: 12px;
    }
    svg {
      max-width: 100%;
      height: auto;
    }
  </style>
</head>
<body>
  <div id="diagram"></div>
  <script src="https://{{MermaidAssetHost}}/mermaid.min.js"></script>
  <script>
    const graphDefinition = {{mermaidJson}};
    const target = document.getElementById('diagram');
    (async function () {
      try {
        mermaid.initialize({ startOnLoad: false, securityLevel: 'loose', theme: 'dark' });
        const { svg } = await mermaid.render('phantom-mermaid-diagram', graphDefinition);
        target.innerHTML = svg;
      } catch (err) {
        target.innerHTML = `<pre style="white-space: pre-wrap; color: #00ff7f;">${graphDefinition.replace(/</g, '&lt;')}</pre>`;
      }
    })();
  </script>
</body>
</html>
""";
        }

        private static double CalculateDiagramHeight(string mermaidCode)
        {
            var lineCount = mermaidCode.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
            return Math.Clamp(180d + (lineCount * 20d), 220d, 560d);
        }

        private static void StyleDocument(FlowDocument doc, bool isUser)
        {
            // Apply custom styles to all blocks
            foreach (var block in doc.Blocks)
            {
                StyleBlock(block, isUser);
            }
        }

        private static void StyleBlock(Block block, bool isUser)
        {
            // Base color
            var textColor = isUser ? Color.FromRgb(173, 216, 230) : Color.FromRgb(255, 255, 255);
            
            if (block is Paragraph paragraph)
            {
                // Check if this entire paragraph is a code block
                bool isCodeBlock = false;
                
                // Code blocks often have a specific background
                if (paragraph.Background != null && paragraph.Background != Brushes.Transparent)
                {
                    isCodeBlock = true;
                }
                
                // Check font family
                if (paragraph.FontFamily != null)
                {
                    var fontName = paragraph.FontFamily.Source.ToLower();
                    if (fontName.Contains("consolas") || fontName.Contains("courier") || fontName.Contains("mono"))
                    {
                        isCodeBlock = true;
                    }
                }
                
                if (isCodeBlock)
                {
                    // ══════════════════════════════════════════════════════════════
                    // MULTI-LINE CODE BLOCK - GREEN TEXT
                    // ══════════════════════════════════════════════════════════════
                    
                    paragraph.Background = Brushes.Transparent;
                    paragraph.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 127)); // Bright green
                    paragraph.FontFamily = new FontFamily("Consolas, Courier New, monospace");
                    paragraph.FontSize = 13;
                    paragraph.Padding = new Thickness(10);
                    paragraph.Margin = new Thickness(0, 8, 0, 8);
                    
                    // Apply green to all runs inside
                    foreach (var inline in paragraph.Inlines)
                    {
                        if (inline is Run run)
                        {
                            run.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 127));
                        }
                    }
                }
                else
                {
                    // Normal paragraph
                    paragraph.Foreground = new SolidColorBrush(textColor);
                    paragraph.FontFamily = new FontFamily("Segoe UI");
                    paragraph.FontSize = 14;
                    paragraph.Margin = new Thickness(0, 5, 0, 5);
                }
                
                // Style inline elements (for inline code detection)
                StyleInlines(paragraph.Inlines, isUser, isCodeBlock);
            }
            else if (block is Section section)
            {
                foreach (var childBlock in section.Blocks)
                {
                    StyleBlock(childBlock, isUser);
                }
            }
            else if (block is List list)
            {
                list.Foreground = new SolidColorBrush(textColor);
                list.FontFamily = new FontFamily("Segoe UI");
                list.FontSize = 14;
                list.Margin = new Thickness(20, 5, 0, 5);
                
                foreach (var listItem in list.ListItems)
                {
                    foreach (var childBlock in listItem.Blocks)
                    {
                        StyleBlock(childBlock, isUser);
                    }
                }
            }
            else if (block is Table table)
            {
                table.Foreground = new SolidColorBrush(textColor);
                table.BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));
                table.BorderThickness = new Thickness(1);
                table.CellSpacing = 0;
                table.Margin = new Thickness(0, 10, 0, 10);
            }
            else if (block is BlockUIContainer uiContainer)
            {
                if (uiContainer.Child is System.Windows.Controls.Border border)
                {
                    if (border.Child is System.Windows.Controls.TextBlock textBlock)
                    {
                        border.Background = Brushes.Transparent;
                        border.BorderBrush = Brushes.Transparent;
                        border.BorderThickness = new Thickness(0);
                        border.CornerRadius = new CornerRadius(0);
                        border.Padding = new Thickness(10);
                        textBlock.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 127));
                        textBlock.FontFamily = new FontFamily("Consolas, Courier New, monospace");
                        textBlock.FontSize = 13;
                    }
                }
            }
        }

        private static void StyleInlines(InlineCollection inlines, bool isUser, bool isInsideCodeBlock = false)
        {
            foreach (var inline in inlines)
            {
                if (inline is Run run)
                {
                    // ══════════════════════════════════════════════════════════════
                    // INLINE CODE DETECTION - Run elements with monospace font + gray background
                    // ══════════════════════════════════════════════════════════════
                    
                    if (!isInsideCodeBlock)
                    {
                        bool isInlineCode = false;
                        
                        // Check for monospace font
                        if (run.FontFamily != null)
                        {
                            var fontName = run.FontFamily.Source.ToLower();
                            if (fontName.Contains("consolas") || fontName.Contains("courier") || fontName.Contains("lucida") || fontName.Contains("mono"))
                            {
                                isInlineCode = true;
                            }
                        }
                        
                        // Check for gray background (#FFD3D3D3)
                        if (run.Background is SolidColorBrush bgBrush)
                        {
                            var bgColor = bgBrush.Color;
                            // Check if it's a light gray (D3D3D3 or similar)
                            if (bgColor.R > 200 && bgColor.G > 200 && bgColor.B > 200)
                            {
                                isInlineCode = true;
                            }
                        }
                        
                        if (isInlineCode)
                        {
                            // ✓✓ APPLY YELLOW INLINE CODE STYLING
                            run.Background = Brushes.Transparent;
                            run.Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0)); // Bright gold/yellow
                            run.FontFamily = new FontFamily("Consolas, Courier New, monospace");
                            run.FontSize = 13;
                        }
                        else
                        {
                            // Normal text
                            run.Foreground = new SolidColorBrush(isUser ? 
                                Color.FromRgb(173, 216, 230) : 
                                Color.FromRgb(255, 255, 255));
                        }
                    }
                }
                else if (inline is Bold bold)
                {
                    if (!isInsideCodeBlock)
                    {
                        bold.Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0)); // Gold
                    }
                    StyleInlines(bold.Inlines, isUser, isInsideCodeBlock);
                }
                else if (inline is Italic italic)
                {
                    if (!isInsideCodeBlock)
                    {
                        italic.Foreground = new SolidColorBrush(Color.FromRgb(144, 238, 144)); // Light green
                    }
                    StyleInlines(italic.Inlines, isUser, isInsideCodeBlock);
                }
                else if (inline is Underline underline)
                {
                    StyleInlines(underline.Inlines, isUser, isInsideCodeBlock);
                }
                else if (inline is Hyperlink hyperlink)
                {
                    if (!isInsideCodeBlock)
                    {
                        hyperlink.Foreground = new SolidColorBrush(Color.FromRgb(100, 149, 237)); // Cornflower blue
                        hyperlink.TextDecorations = TextDecorations.Underline;
                    }
                    StyleInlines(hyperlink.Inlines, isUser, isInsideCodeBlock);
                }
                else if (inline is Span span)
                {
                    // Process Span inlines recursively
                    StyleInlines(span.Inlines, isUser, isInsideCodeBlock);
                }
            }
        }

        public static void ClearDocument(FlowDocument document)
        {
            document.Blocks.Clear();
        }

        public static void AddWelcomeMessage(FlowDocument document)
        {
            var welcome = @"# Welcome to your invisible AI assistant! 🤖

✓ **Completely invisible** to screen sharing  
✓ **Mouse cursor** IS visible  
✓ **Click ⚙️** for settings  

## ⌨️ Hotkeys:
- `Ctrl+Alt+\`` = Hide/Show  
- `Ctrl+Alt+-` = Quit  
- `Ctrl+Alt+=` = Settings  

Ask me anything!";

            AppendMarkdown(document, welcome, false);
        }

        private sealed record MarkdownSegment(string Content, bool IsMermaid);
    }
}
