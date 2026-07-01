using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
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
using DrawingColor = System.Drawing.Color;

namespace SecureOverlay
{
    public static class MarkdownHelper
    {
        private const double MermaidViewportWidth = 640d;
        private const double MermaidViewportHeight = 320d;

        static MarkdownHelper()
        {
            RunMermaidNormalizationSelfCheck();
        }

        private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
            .UseSupportedExtensions()
            .Build();
        private static readonly Regex MermaidBlockRegex = new(
            @"```mermaid\s*(?<code>[\s\S]*?)```",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MermaidInlineHeaderRegex = new(
            @"^(?<header>(?:(?:flowchart|graph)\s+(?:TB|TD|BT|RL|LR)|sequenceDiagram|classDiagram|stateDiagram-v2|stateDiagram|erDiagram|journey|gantt|pie|gitGraph|mindmap|timeline|quadrantChart|requirementDiagram|xychart-beta))\s+(?<body>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex MermaidNodeBoundaryRegex = new(
            @"(?<left>(?:\b[A-Za-z][A-Za-z0-9_-]*|[\]\)\}]))\s+(?<right>[A-Za-z][A-Za-z0-9_-]*[\[\(\{])",
            RegexOptions.Compiled);
        private static readonly Regex MermaidKeywordBoundaryRegex = new(
            @"(?<left>(?:\b[A-Za-z][A-Za-z0-9_-]*|[\]\)\}]))\s+(?<right>(?:subgraph|end|direction|style|classDef|class|click|linkStyle)\b)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MermaidEdgeBoundaryRegex = new(
            @"(?<left>(?:\b[A-Za-z][A-Za-z0-9_-]*|[\]\)\}]))\s+(?<right>[A-Za-z][A-Za-z0-9_-]*\s*[-.=ox<>]{2,})",
            RegexOptions.Compiled);
        private static readonly Lazy<Task<CoreWebView2Environment>> MermaidEnvironment = new(
            () => CoreWebView2Environment.CreateAsync(null, WindowsAppPaths.WebView2CachePath));
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
            var container = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 8, 0, 8),
                MaxWidth = MermaidViewportWidth
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
                Width = MermaidViewportWidth,
                Height = MermaidViewportHeight,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var border = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.FromRgb(72, 78, 92)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(0),
                Width = MermaidViewportWidth,
                Height = MermaidViewportHeight,
                ClipToBounds = true,
                Child = webView
            };
            container.Children.Add(border);

            document.Blocks.Add(new BlockUIContainer(container));
            _ = InitializeMermaidBlockAsync(webView, mermaidCode, container);
        }

        private static async Task InitializeMermaidBlockAsync(
            WebView2 webView,
            string mermaidCode,
            Panel container)
        {
            try
            {
                Directory.CreateDirectory(WindowsAppPaths.WebView2CachePath);
                Directory.CreateDirectory(WindowsAppPaths.TempRoot);
                if (!File.Exists(MermaidScriptPath))
                {
                    throw new FileNotFoundException("Local Mermaid bundle not found.", MermaidScriptPath);
                }

                var env = await MermaidEnvironment.Value;
                await webView.EnsureCoreWebView2Async(env);
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                webView.DefaultBackgroundColor = DrawingColor.Transparent;
                webView.CoreWebView2.WebMessageReceived += (_, args) =>
                {
                    Log.WriteLine($"Mermaid WebView: {args.TryGetWebMessageAsString()}");
                };
                var htmlFilePath = Path.Combine(WindowsAppPaths.TempRoot, $"mermaid_{Guid.NewGuid():N}.html");
                Log.WriteLine($"Mermaid source: {FormatMermaidForLog(mermaidCode)}");
                File.WriteAllText(htmlFilePath, BuildMermaidHtml(mermaidCode));
                webView.CoreWebView2.Navigate($"file:///{htmlFilePath.Replace("\\", "/")}");
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
            var mermaidCandidatesJson = JsonSerializer.Serialize(BuildMermaidRenderCandidates(mermaidCode));
            var mermaidJson = JsonSerializer.Serialize(NormalizeMermaidWhitespace(mermaidCode));
            var mermaidScriptUri = new Uri(MermaidScriptPath).AbsoluteUri;
            return """
<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8" />
  <style>
    html, body {
      margin: 0;
      padding: 0;
      background: transparent;
      color: #ffffff;
      height: 100%;
      overflow: hidden;
      font-family: "Segoe UI", sans-serif;
    }
    #viewport {
      height: 320px;
      overflow: auto;
      box-sizing: border-box;
    }
    #diagram {
      padding: 12px;
      box-sizing: border-box;
      min-height: 100%;
    }
    svg {
      max-width: 100%;
      height: auto;
      display: block;
    }
  </style>
</head>
<body>
  <div id="viewport">
    <div id="diagram"></div>
  </div>
  <script src="__MERMAID_SRC__"></script>
  <script>
    const graphDefinition = __MERMAID_JSON__;
    const graphCandidates = __MERMAID_CANDIDATES__;
    const target = document.getElementById('diagram');
    const postToHost = (payload) => {
      try {
        if (window.chrome && window.chrome.webview) {
          window.chrome.webview.postMessage(payload);
        }
      } catch {}
    };
    (async function () {
      let lastCandidate = graphDefinition;
      let lastError = 'Unknown Mermaid error';
      try {
        if (!window.mermaid) {
          throw new Error('Mermaid runtime not available');
        }
        window.mermaid.initialize({ startOnLoad: false, securityLevel: 'loose', theme: 'dark' });
        for (let i = 0; i < graphCandidates.length; i += 1) {
          const candidate = graphCandidates[i];
          lastCandidate = candidate;
          try {
            const { svg } = await window.mermaid.render(`phantom-mermaid-diagram-${i}`, candidate);
            target.innerHTML = svg;
            postToHost(`mermaid:rendered:${i}:${document.body.scrollHeight}`);
            return;
          } catch (err) {
            lastError = err && err.message ? err.message : String(err);
          }
        }
      } catch (err) {
        lastError = err && err.message ? err.message : String(err);
      }
      postToHost(`mermaid:error:${lastError}`);
      postToHost(`mermaid:candidate:${lastCandidate}`);
      target.innerHTML = `<pre style="white-space: pre-wrap; color: #00ff7f;">${lastCandidate.replace(/</g, '&lt;')}</pre>`;
    })();
  </script>
</body>
</html>
"""
                .Replace("__MERMAID_SRC__", mermaidScriptUri, StringComparison.Ordinal)
                .Replace("__MERMAID_CANDIDATES__", mermaidCandidatesJson, StringComparison.Ordinal)
                .Replace("__MERMAID_JSON__", mermaidJson, StringComparison.Ordinal);
        }

        private static IReadOnlyList<string> BuildMermaidRenderCandidates(string mermaidCode)
        {
            var candidates = new List<string>();
            var normalized = NormalizeMermaidWhitespace(mermaidCode);
            AddMermaidCandidate(candidates, normalized);

            if (!TrySplitMermaidHeaderAndBody(normalized, out var header, out var body))
            {
                return candidates;
            }

            AddMermaidCandidate(candidates, CombineMermaidHeaderAndBody(header, body));

            if (IsFlowchartHeader(header))
            {
                AddMermaidCandidate(candidates, CombineMermaidHeaderAndBody(header, NormalizeFlowchartBody(body, aggressive: false)));
                AddMermaidCandidate(candidates, CombineMermaidHeaderAndBody(header, NormalizeFlowchartBody(body, aggressive: true)));
            }

            return candidates;
        }

        private static string NormalizeMermaidWhitespace(string mermaidCode)
        {
            return mermaidCode.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        }

        private static bool TrySplitMermaidHeaderAndBody(string mermaidCode, out string header, out string body)
        {
            var normalized = NormalizeMermaidWhitespace(mermaidCode);
            var newlineIndex = normalized.IndexOf('\n');
            if (newlineIndex >= 0)
            {
                var firstLine = normalized[..newlineIndex].Trim();
                var remaining = normalized[(newlineIndex + 1)..].Trim();
                if (IsStandaloneMermaidHeader(firstLine) && !string.IsNullOrWhiteSpace(remaining))
                {
                    header = firstLine;
                    body = remaining;
                    return true;
                }
            }

            var inlineHeaderMatch = MermaidInlineHeaderRegex.Match(normalized);
            if (inlineHeaderMatch.Success)
            {
                header = inlineHeaderMatch.Groups["header"].Value.Trim();
                body = inlineHeaderMatch.Groups["body"].Value.Trim();
                return true;
            }

            header = string.Empty;
            body = string.Empty;
            return false;
        }

        private static string CombineMermaidHeaderAndBody(string header, string body)
        {
            return string.IsNullOrWhiteSpace(body)
                ? header.Trim()
                : $"{header.Trim()}\n{body.Trim()}";
        }

        private static bool IsFlowchartHeader(string header)
        {
            return header.StartsWith("flowchart ", StringComparison.OrdinalIgnoreCase) ||
                   header.StartsWith("graph ", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStandaloneMermaidHeader(string line)
        {
            return line.Equals("sequenceDiagram", StringComparison.OrdinalIgnoreCase) ||
                   line.Equals("classDiagram", StringComparison.OrdinalIgnoreCase) ||
                   line.Equals("stateDiagram-v2", StringComparison.OrdinalIgnoreCase) ||
                   line.Equals("stateDiagram", StringComparison.OrdinalIgnoreCase) ||
                   line.Equals("erDiagram", StringComparison.OrdinalIgnoreCase) ||
                   line.Equals("journey", StringComparison.OrdinalIgnoreCase) ||
                   line.Equals("gantt", StringComparison.OrdinalIgnoreCase) ||
                   line.Equals("pie", StringComparison.OrdinalIgnoreCase) ||
                   line.Equals("gitGraph", StringComparison.OrdinalIgnoreCase) ||
                   line.Equals("mindmap", StringComparison.OrdinalIgnoreCase) ||
                   line.Equals("timeline", StringComparison.OrdinalIgnoreCase) ||
                   line.Equals("quadrantChart", StringComparison.OrdinalIgnoreCase) ||
                   line.Equals("requirementDiagram", StringComparison.OrdinalIgnoreCase) ||
                   line.Equals("xychart-beta", StringComparison.OrdinalIgnoreCase) ||
                   Regex.IsMatch(line, @"^(?:flowchart|graph)\s+(?:TB|TD|BT|RL|LR)$", RegexOptions.IgnoreCase);
        }

        private static string NormalizeFlowchartBody(string body, bool aggressive)
        {
            var normalized = NormalizeMermaidWhitespace(body);
            normalized = Regex.Replace(normalized, @"\s*;\s*", "\n");

            for (var iteration = 0; iteration < 4; iteration++)
            {
                var updated = normalized;
                updated = MermaidKeywordBoundaryRegex.Replace(updated, "${left}\n${right}");
                updated = MermaidNodeBoundaryRegex.Replace(updated, "${left}\n${right}");
                updated = MermaidEdgeBoundaryRegex.Replace(updated, "${left}\n${right}");

                if (aggressive)
                {
                    updated = Regex.Replace(
                        updated,
                        @"(?<left>[\]\)\}]|""|\b[A-Za-z][A-Za-z0-9_-]*)\s+(?<right>[A-Z][A-Za-z0-9_-]*\b)",
                        "${left}\n${right}");
                }

                updated = NormalizeMermaidLines(updated);
                if (updated == normalized)
                {
                    break;
                }

                normalized = updated;
            }

            return normalized;
        }

        private static string NormalizeMermaidLines(string mermaidCode)
        {
            var builder = new StringBuilder();
            using var reader = new StringReader(mermaidCode);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(trimmed);
            }

            return builder.ToString();
        }

        private static void AddMermaidCandidate(List<string> candidates, string candidate)
        {
            var normalized = NormalizeMermaidLines(candidate);
            if (normalized.Length == 0)
            {
                return;
            }

            foreach (var existing in candidates)
            {
                if (string.Equals(existing, normalized, StringComparison.Ordinal))
                {
                    return;
                }
            }

            candidates.Add(normalized);
        }

        private static string FormatMermaidForLog(string mermaidCode)
        {
            var normalized = NormalizeMermaidWhitespace(mermaidCode).Replace("\n", "\\n");
            return normalized.Length <= 600 ? normalized : normalized[..600] + "...";
        }

        [Conditional("DEBUG")]
        private static void RunMermaidNormalizationSelfCheck()
        {
            Debug.Assert(
                CombineMermaidHeaderAndBody("flowchart TD", NormalizeFlowchartBody("A[JAQ CLI] --> B[Start]", aggressive: false)) ==
                "flowchart TD\nA[JAQ CLI] --> B[Start]",
                "Mermaid one-line normalization regressed.");
            Debug.Assert(
                CombineMermaidHeaderAndBody("flowchart TD", NormalizeFlowchartBody("A[Client] --> B[API] C --> D[Worker]", aggressive: false)) ==
                "flowchart TD\nA[Client] --> B[API]\nC --> D[Worker]",
                "Mermaid statement splitting regressed.");
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
