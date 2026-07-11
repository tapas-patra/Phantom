using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        private const int MermaidRenderSurfaceWidth = 1400;
        private const int MermaidRenderSurfaceHeight = 1000;
        private const int MermaidCaptureMinWidth = 260;
        private const int MermaidCaptureMinHeight = 140;
        private const int MermaidCaptureMaxWidth = 1600;
        private const int MermaidCaptureMaxHeight = 2200;

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
        private static readonly Regex MermaidSubgraphBoundaryRegex = new(
            @"(?<left>[\]\)\}]|""|\bend\b)\s+(?<right>subgraph\b)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MermaidEndBoundaryRegex = new(
            @"(?<left>[\]\)\}""])\s+(?<right>end\b)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CodeFenceRegex = new(
            @"```",
            RegexOptions.Compiled);
        private static readonly Lazy<Task<CoreWebView2Environment>> MermaidEnvironment = new(
            () => CoreWebView2Environment.CreateAsync(null, WindowsAppPaths.WebView2CachePath));
        private static readonly string MermaidAssetFolder = Path.Combine(AppContext.BaseDirectory, "assets", "mermaid");
        private static readonly string MermaidScriptPath = Path.Combine(MermaidAssetFolder, "mermaid.min.js");
        private static readonly string ChatShellPath = Path.Combine(WindowsAppPaths.TempRoot, "chat_shell.html");

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

        public static async Task InitializeChatWebViewAsync(WebView2 webView)
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
            webView.CoreWebView2.Settings.IsZoomControlEnabled = true;
            webView.DefaultBackgroundColor = DrawingColor.Transparent;

            if (Equals(webView.Tag, "chat-shell-ready"))
            {
                return;
            }

            File.WriteAllText(ChatShellPath, BuildChatShellHtml());
            var navigationSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<CoreWebView2NavigationCompletedEventArgs>? navigationCompleted = null;
            navigationCompleted = (_, _) =>
            {
                navigationSource.TrySetResult(true);
            };

            try
            {
                var coreWebView = webView.CoreWebView2;
                if (coreWebView == null)
                {
                    return;
                }

                coreWebView.NavigationCompleted += navigationCompleted;
                coreWebView.Navigate($"file:///{ChatShellPath.Replace("\\", "/")}");
                await navigationSource.Task;
                webView.Tag = "chat-shell-ready";
            }
            finally
            {
                if (webView.CoreWebView2 != null && navigationCompleted != null)
                {
                    webView.CoreWebView2.NavigationCompleted -= navigationCompleted;
                }
            }
        }

        public static async Task RenderChatTranscriptAsync(WebView2 webView, IReadOnlyList<ChatRenderMessage> messages)
        {
            await InitializeChatWebViewAsync(webView);
            var html = BuildChatTranscriptHtml(messages);
            var payloadJson = JsonSerializer.Serialize(new { html });
            if (webView.CoreWebView2 == null)
            {
                return;
            }

            await webView.CoreWebView2.ExecuteScriptAsync($"window.phantomChat.render({payloadJson});");
        }

        public static async Task SetChatCursorHiddenAsync(WebView2 webView, bool hidden)
        {
            await InitializeChatWebViewAsync(webView);
            if (webView.CoreWebView2 == null)
            {
                return;
            }

            var hiddenJson = hidden ? "true" : "false";
            await webView.CoreWebView2.ExecuteScriptAsync($"window.phantomChat.setCursorHidden({hiddenJson});");
        }

        public static bool TryExtractFirstMermaidBlock(string markdown, out string mermaidCode)
        {
            mermaidCode = string.Empty;
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return false;
            }

            var match = MermaidBlockRegex.Match(markdown);
            if (!match.Success)
            {
                return false;
            }

            mermaidCode = match.Groups["code"].Value.Trim();
            return !string.IsNullOrWhiteSpace(mermaidCode);
        }

        public static string ReplaceMermaidBlocks(string markdown, string replacement)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return string.Empty;
            }

            return MermaidBlockRegex.Replace(markdown, replacement ?? string.Empty).Trim();
        }

        public sealed record ChatRenderMessage(bool IsUser, string Markdown);

        public static async Task<(bool Success, string ErrorMessage, string Candidate)> RenderMermaidToWebViewAsync(
            WebView2 webView,
            string mermaidCode)
        {
            Directory.CreateDirectory(WindowsAppPaths.WebView2CachePath);
            Directory.CreateDirectory(WindowsAppPaths.TempRoot);
            if (!File.Exists(MermaidScriptPath))
            {
                return (false, "Local Mermaid bundle not found.", mermaidCode);
            }

            var env = await MermaidEnvironment.Value;
            await webView.EnsureCoreWebView2Async(env);
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.IsZoomControlEnabled = true;
            webView.DefaultBackgroundColor = DrawingColor.Transparent;

            var messageSource = new TaskCompletionSource<MermaidBrowserMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<CoreWebView2WebMessageReceivedEventArgs>? webMessageHandler = null;
            try
            {
                webMessageHandler = (_, args) =>
                {
                    var message = args.TryGetWebMessageAsString();
                    Log.WriteLine($"Mermaid WebView: {message}");
                    if (TryParseMermaidBrowserMessage(message, out var parsed))
                    {
                        messageSource.TrySetResult(parsed);
                    }
                };
                webView.CoreWebView2.WebMessageReceived += webMessageHandler;

                var htmlFilePath = Path.Combine(WindowsAppPaths.TempRoot, $"mermaid_panel_{Guid.NewGuid():N}.html");
                Log.WriteLine($"Mermaid source: {FormatMermaidForLog(mermaidCode)}");
                File.WriteAllText(htmlFilePath, BuildMermaidViewerHtml(mermaidCode));
                webView.CoreWebView2.Navigate($"file:///{htmlFilePath.Replace("\\", "/")}");

                var completedTask = await Task.WhenAny(messageSource.Task, Task.Delay(8000));
                if (completedTask != messageSource.Task)
                {
                    return (false, "Timed out waiting for Mermaid render.", mermaidCode);
                }

                var browserMessage = await messageSource.Task;
                return browserMessage.IsSuccess
                    ? (true, string.Empty, browserMessage.Candidate ?? mermaidCode)
                    : (false, browserMessage.ErrorMessage ?? "Mermaid render failed.", browserMessage.Candidate ?? mermaidCode);
            }
            finally
            {
                if (webView.CoreWebView2 != null && webMessageHandler != null)
                {
                    webView.CoreWebView2.WebMessageReceived -= webMessageHandler;
                }
            }
        }

        private static void AppendStandardMarkdown(FlowDocument document, string markdown, bool isUser)
        {
            var normalizedMarkdown = NormalizeMarkdownForDisplay(markdown);
            var tempDoc = Markdig.Wpf.Markdown.ToFlowDocument(normalizedMarkdown, _pipeline);
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

        private static string BuildChatTranscriptHtml(IReadOnlyList<ChatRenderMessage> messages)
        {
            var builder = new StringBuilder();
            foreach (var message in messages)
            {
                builder.Append("<article class=\"message ")
                    .Append(message.IsUser ? "user" : "assistant")
                    .Append("\"><div class=\"message-body\">")
                    .Append(RenderMarkdownToHtml(message.Markdown))
                    .Append("</div></article>");
            }

            return builder.ToString();
        }

        private static string RenderMarkdownToHtml(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return string.Empty;
            }

            var normalizedMarkdown = NormalizeMarkdownForDisplay(markdown);
            var builder = new StringBuilder();
            foreach (var segment in SplitMarkdownSegments(normalizedMarkdown))
            {
                if (string.IsNullOrWhiteSpace(segment.Content))
                {
                    continue;
                }

                if (segment.IsMermaid)
                {
                    builder.Append(BuildMermaidHostHtml(segment.Content));
                }
                else
                {
                    builder.Append(Markdig.Markdown.ToHtml(segment.Content, _pipeline));
                }
            }

            return builder.ToString();
        }

        private static string BuildMermaidHostHtml(string mermaidCode)
        {
            var normalized = NormalizeMermaidWhitespace(mermaidCode);
            var candidatesJson = JsonSerializer.Serialize(BuildMermaidRenderCandidates(normalized));
            return """
<div class="mermaid-card">
  <div class="mermaid-host" data-candidates="__CANDIDATES__" data-raw="__RAW__"></div>
</div>
"""
                .Replace("__CANDIDATES__", WebUtility.HtmlEncode(candidatesJson), StringComparison.Ordinal)
                .Replace("__RAW__", WebUtility.HtmlEncode(normalized), StringComparison.Ordinal);
        }

        private static string BuildChatShellHtml()
        {
            var mermaidScriptUri = new Uri(MermaidScriptPath).AbsoluteUri;
            return """
<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8" />
  <style>
    :root {
      color-scheme: dark;
      --text: #f5f7fb;
      --muted: #9eb0c7;
      --user: #c4e9ff;
      --border: rgba(255,255,255,0.12);
      --panel: rgba(255,255,255,0.04);
      --code: #00ff7f;
      --inline-code: #ffd76b;
      --link: #73b8ff;
    }
    html, body {
      margin: 0;
      padding: 0;
      background: transparent;
      color: var(--text);
      font-family: "Segoe UI", sans-serif;
      overflow-x: hidden;
      overflow-y: auto;
    }
    body {
      padding: 0;
    }
    #transcript {
      padding: 0 2px 0 0;
    }
    .message {
      margin: 0 0 12px 0;
      line-height: 1.55;
      word-break: break-word;
    }
    .message.user {
      color: var(--user);
    }
    .message.assistant {
      color: var(--text);
    }
    .message-body > :first-child {
      margin-top: 0;
    }
    .message-body > :last-child {
      margin-bottom: 0;
    }
    body.hide-cursor,
    body.hide-cursor * {
      cursor: none !important;
    }
    p, ul, ol, pre, table, blockquote, h1, h2, h3, h4, h5, h6 {
      margin: 0 0 10px 0;
    }
    ul, ol {
      padding-left: 22px;
    }
    strong {
      color: #ffd76b;
    }
    em {
      color: #9ce49c;
    }
    a {
      color: var(--link);
    }
    code {
      color: var(--inline-code);
      font-family: Consolas, "Courier New", monospace;
      background: transparent;
    }
    pre {
      color: var(--code);
      font-family: Consolas, "Courier New", monospace;
      background: rgba(255,255,255,0.04);
      border: 1px solid rgba(255,255,255,0.1);
      border-radius: 8px;
      padding: 10px 12px;
      white-space: pre;
      overflow-x: auto;
    }
    pre code {
      display: block;
      white-space: inherit;
    }
    blockquote {
      border-left: 2px solid rgba(255,255,255,0.2);
      padding-left: 10px;
      color: var(--muted);
    }
    table {
      border-collapse: collapse;
      width: auto;
      max-width: 100%;
    }
    th, td {
      border: 1px solid rgba(255,255,255,0.16);
      padding: 6px 8px;
    }
    .mermaid-card {
      margin: 8px 0 12px 0;
      padding: 12px;
      border: 1px solid var(--border);
      border-radius: 10px;
      background: var(--panel);
      overflow: auto;
      max-height: 360px;
    }
    .mermaid-host svg {
      display: block;
      max-width: none;
      height: auto;
    }
    .mermaid-fallback {
      margin: 0;
      color: var(--code);
      white-space: pre;
    }
  </style>
</head>
<body>
  <div id="transcript"></div>
  <script src="__MERMAID_SRC__"></script>
  <script>
    const transcript = document.getElementById('transcript');
    let pendingCursorMove = null;
    let cursorMoveQueued = false;
    let cursorBridgeBound = false;

    function postHostMessage(message) {
      if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage(message);
      }
    }

    function flushCursorMove() {
      cursorMoveQueued = false;
      if (!pendingCursorMove) {
        return;
      }

      postHostMessage(`CHAT_CURSOR:move:${pendingCursorMove.x}:${pendingCursorMove.y}`);
      pendingCursorMove = null;
    }

    function queueCursorMove(event) {
      pendingCursorMove = {
        x: Math.round(event.clientX),
        y: Math.round(event.clientY)
      };

      if (cursorMoveQueued) {
        return;
      }

      cursorMoveQueued = true;
      window.requestAnimationFrame(flushCursorMove);
    }

    function bindCursorBridge() {
      if (cursorBridgeBound) {
        return;
      }

      cursorBridgeBound = true;
      window.addEventListener('pointerenter', () => postHostMessage('CHAT_CURSOR:enter'), true);
      window.addEventListener('pointerleave', () => postHostMessage('CHAT_CURSOR:leave'), true);
      window.addEventListener('pointermove', queueCursorMove, { passive: true });
      window.addEventListener('blur', () => postHostMessage('CHAT_CURSOR:leave'));
      document.addEventListener('visibilitychange', () => {
        if (document.hidden) {
          postHostMessage('CHAT_CURSOR:leave');
        }
      });
    }

    function isMermaidError(svg, text) {
      return /syntax error in text/i.test(text) ||
             /parse error/i.test(text) ||
             /mermaid version/i.test(text) ||
             /syntax error in text/i.test(svg);
    }

    async function renderMermaidHosts(root) {
      if (!window.mermaid) {
        return;
      }

      window.mermaid.initialize({ startOnLoad: false, securityLevel: 'loose', theme: 'dark' });
      const hosts = Array.from(root.querySelectorAll('.mermaid-host'));
      for (let i = 0; i < hosts.length; i += 1) {
        const host = hosts[i];
        const raw = host.getAttribute('data-raw') || '';
        let candidates = [];
        try {
          candidates = JSON.parse(host.getAttribute('data-candidates') || '[]');
        } catch {}

        let rendered = false;
        for (let j = 0; j < candidates.length; j += 1) {
          const candidate = candidates[j];
          try {
            const { svg } = await window.mermaid.render(`phantom-chat-mermaid-${i}-${j}`, candidate);
            const probe = document.createElement('div');
            probe.innerHTML = svg;
            const text = (probe.textContent || '').trim();
            if (isMermaidError(svg, text)) {
              throw new Error(text || 'Mermaid produced an error diagram');
            }

            host.innerHTML = svg;
            rendered = true;
            break;
          } catch {}
        }

        if (!rendered) {
          const pre = document.createElement('pre');
          pre.className = 'mermaid-fallback';
          pre.textContent = raw;
          host.replaceChildren(pre);
        }
      }
    }

    window.phantomChat = {
      setCursorHidden: function(hidden) {
        document.body.classList.toggle('hide-cursor', !!hidden);
      },
      render: async function(payload) {
        transcript.innerHTML = payload && payload.html ? payload.html : '';
        await renderMermaidHosts(transcript);
        requestAnimationFrame(() => window.scrollTo(0, document.body.scrollHeight));
      }
    };

    bindCursorBridge();
  </script>
</body>
</html>
"""
                .Replace("__MERMAID_SRC__", mermaidScriptUri, StringComparison.Ordinal);
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

        private static string NormalizeMarkdownForDisplay(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return string.Empty;
            }

            var normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
            if (CodeFenceRegex.Matches(normalized).Count % 2 != 0)
            {
                normalized += "\n```";
            }

            return normalized;
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
                Child = BuildMermaidLoadingState()
            };
            container.Children.Add(border);

            document.Blocks.Add(new BlockUIContainer(container));
            _ = InitializeMermaidBlockAsync(border, mermaidCode);
        }

        private static async Task InitializeMermaidBlockAsync(Border hostBorder, string mermaidCode)
        {
            try
            {
                var renderResult = await RenderMermaidToBitmapAsync(mermaidCode);
                if (renderResult.Image != null)
                {
                    hostBorder.Child = BuildMermaidImageViewport(renderResult.Image);
                    return;
                }

                Log.WriteLine($"Mermaid static render fallback: {renderResult.ErrorMessage}");
                hostBorder.Child = BuildMermaidFallback(renderResult.Candidate ?? mermaidCode);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Mermaid rendering error: {ex.Message}");
                hostBorder.Child = BuildMermaidFallback(mermaidCode);
            }
        }

        private static FrameworkElement BuildMermaidLoadingState()
        {
            return new Grid
            {
                Background = Brushes.Transparent,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Rendering diagram...",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = new SolidColorBrush(Color.FromRgb(180, 190, 205)),
                        FontFamily = new FontFamily("Segoe UI"),
                        FontSize = 13
                    }
                }
            };
        }

        private static FrameworkElement BuildMermaidImageViewport(BitmapSource image)
        {
            var imageControl = new System.Windows.Controls.Image
            {
                Source = image,
                Stretch = Stretch.None,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = MermaidViewportHeight,
                Width = MermaidViewportWidth,
                Content = imageControl
            };
        }

        private static FrameworkElement BuildMermaidFallback(string mermaidCode)
        {
            return new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                Padding = new Thickness(0),
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Height = MermaidViewportHeight,
                    Width = MermaidViewportWidth,
                    Content = new TextBlock
                    {
                        Text = $"```mermaid\n{mermaidCode}\n```",
                        TextWrapping = TextWrapping.NoWrap,
                        Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 127)),
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 13,
                        Margin = new Thickness(10)
                    }
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
      overflow: hidden;
      font-family: "Segoe UI", sans-serif;
      width: fit-content;
      height: fit-content;
    }
    #diagram {
      padding: 0;
      box-sizing: border-box;
      display: inline-block;
    }
    svg {
      max-width: none;
      height: auto;
      display: block;
    }
  </style>
</head>
<body>
  <div id="diagram"></div>
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
            const renderedText = (target.textContent || '').trim();
            const isErrorSvg =
              /syntax error in text/i.test(renderedText) ||
              /parse error/i.test(renderedText) ||
              /mermaid version/i.test(renderedText) ||
              /syntax error in text/i.test(svg);
            if (isErrorSvg) {
              target.innerHTML = '';
              throw new Error(renderedText || 'Mermaid produced an error diagram');
            }
            const svgNode = target.querySelector('svg');
            if (!svgNode) {
              throw new Error('Mermaid did not produce an SVG node');
            }
            const rect = svgNode.getBoundingClientRect();
            const viewBox = svgNode.viewBox && svgNode.viewBox.baseVal
              ? svgNode.viewBox.baseVal
              : null;
            const width = Math.ceil(
              Math.max(
                rect.width || 0,
                svgNode.width && svgNode.width.baseVal ? svgNode.width.baseVal.value : 0,
                viewBox ? viewBox.width : 0
              )
            );
            const height = Math.ceil(
              Math.max(
                rect.height || 0,
                svgNode.height && svgNode.height.baseVal ? svgNode.height.baseVal.value : 0,
                viewBox ? viewBox.height : 0
              )
            );
            if (width <= 0 || height <= 0) {
              throw new Error('Mermaid produced an empty SVG');
            }
            postToHost(JSON.stringify({ type: 'rendered', candidateIndex: i, candidate, width, height }));
            return;
          } catch (err) {
            lastError = err && err.message ? err.message : String(err);
          }
        }
      } catch (err) {
        lastError = err && err.message ? err.message : String(err);
      }
      postToHost(JSON.stringify({ type: 'error', error: lastError, candidate: lastCandidate }));
    })();
  </script>
</body>
</html>
"""
                .Replace("__MERMAID_SRC__", mermaidScriptUri, StringComparison.Ordinal)
                .Replace("__MERMAID_CANDIDATES__", mermaidCandidatesJson, StringComparison.Ordinal)
                .Replace("__MERMAID_JSON__", mermaidJson, StringComparison.Ordinal);
        }

        private static string BuildMermaidViewerHtml(string mermaidCode)
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
      width: 100%;
      height: 100%;
      overflow: auto;
      font-family: "Segoe UI", sans-serif;
    }
    #diagram {
      padding: 12px;
      box-sizing: border-box;
      min-width: max-content;
      min-height: 100%;
    }
    svg {
      max-width: none;
      height: auto;
      display: block;
    }
  </style>
</head>
<body>
  <div id="diagram"></div>
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
            const { svg } = await window.mermaid.render(`phantom-mermaid-viewer-${i}`, candidate);
            target.innerHTML = svg;
            const renderedText = (target.textContent || '').trim();
            const isErrorSvg =
              /syntax error in text/i.test(renderedText) ||
              /parse error/i.test(renderedText) ||
              /mermaid version/i.test(renderedText) ||
              /syntax error in text/i.test(svg);
            if (isErrorSvg) {
              target.innerHTML = '';
              throw new Error(renderedText || 'Mermaid produced an error diagram');
            }
            postToHost(JSON.stringify({ type: 'rendered', candidateIndex: i, candidate, width: 0, height: 0 }));
            return;
          } catch (err) {
            lastError = err && err.message ? err.message : String(err);
          }
        }
      } catch (err) {
        lastError = err && err.message ? err.message : String(err);
      }
      postToHost(JSON.stringify({ type: 'error', error: lastError, candidate: lastCandidate }));
    })();
  </script>
</body>
</html>
"""
                .Replace("__MERMAID_SRC__", mermaidScriptUri, StringComparison.Ordinal)
                .Replace("__MERMAID_CANDIDATES__", mermaidCandidatesJson, StringComparison.Ordinal)
                .Replace("__MERMAID_JSON__", mermaidJson, StringComparison.Ordinal);
        }

        private static async Task<MermaidRenderResult> RenderMermaidToBitmapAsync(string mermaidCode)
        {
            Directory.CreateDirectory(WindowsAppPaths.WebView2CachePath);
            Directory.CreateDirectory(WindowsAppPaths.TempRoot);
            if (!File.Exists(MermaidScriptPath))
            {
                return MermaidRenderResult.Failure("Local Mermaid bundle not found.", mermaidCode);
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                return MermaidRenderResult.Failure("Application dispatcher unavailable.", mermaidCode);
            }

            return await dispatcher.InvokeAsync(async () =>
            {
                var mainWindow = System.Windows.Application.Current?.MainWindow as MainWindow;
                var hostContainer = mainWindow?.FindName("WebView2Container") as Grid;
                if (hostContainer == null)
                {
                    return MermaidRenderResult.Failure("WebView2Container not found.", mermaidCode);
                }

                var webView = new WebView2
                {
                    Width = MermaidRenderSurfaceWidth,
                    Height = MermaidRenderSurfaceHeight,
                    Visibility = Visibility.Visible,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    IsHitTestVisible = false
                };

                EventHandler<CoreWebView2WebMessageReceivedEventArgs>? webMessageHandler = null;
                try
                {
                    hostContainer.Children.Add(webView);

                    var env = await MermaidEnvironment.Value;
                    await webView.EnsureCoreWebView2Async(env);
                    webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                    webView.DefaultBackgroundColor = DrawingColor.Transparent;

                    var messageSource = new TaskCompletionSource<MermaidBrowserMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
                    webMessageHandler = (_, args) =>
                    {
                        var message = args.TryGetWebMessageAsString();
                        Log.WriteLine($"Mermaid WebView: {message}");
                        if (TryParseMermaidBrowserMessage(message, out var parsed))
                        {
                            messageSource.TrySetResult(parsed);
                        }
                    };
                    webView.CoreWebView2.WebMessageReceived += webMessageHandler;

                    var htmlFilePath = Path.Combine(WindowsAppPaths.TempRoot, $"mermaid_{Guid.NewGuid():N}.html");
                    Log.WriteLine($"Mermaid source: {FormatMermaidForLog(mermaidCode)}");
                    File.WriteAllText(htmlFilePath, BuildMermaidHtml(mermaidCode));
                    webView.CoreWebView2.Navigate($"file:///{htmlFilePath.Replace("\\", "/")}");

                    var completedTask = await Task.WhenAny(messageSource.Task, Task.Delay(8000));
                    if (completedTask != messageSource.Task)
                    {
                        return MermaidRenderResult.Failure("Timed out waiting for Mermaid render.", mermaidCode);
                    }

                    var browserMessage = await messageSource.Task;
                    if (!browserMessage.IsSuccess)
                    {
                        return MermaidRenderResult.Failure(browserMessage.ErrorMessage ?? "Mermaid render failed.", browserMessage.Candidate ?? mermaidCode);
                    }

                    webView.Width = Math.Clamp(browserMessage.Width + 24, MermaidCaptureMinWidth, MermaidCaptureMaxWidth);
                    webView.Height = Math.Clamp(browserMessage.Height + 24, MermaidCaptureMinHeight, MermaidCaptureMaxHeight);
                    await Task.Delay(150);

                    using var pngStream = new MemoryStream();
                    await webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, pngStream);
                    pngStream.Position = 0;

                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = pngStream;
                    image.EndInit();
                    image.Freeze();

                    return MermaidRenderResult.Success(image);
                }
                catch (Exception ex)
                {
                    return MermaidRenderResult.Failure(ex.Message, mermaidCode);
                }
                finally
                {
                    var coreWebView = webView.CoreWebView2;
                    if (coreWebView != null && webMessageHandler != null)
                    {
                        coreWebView.WebMessageReceived -= webMessageHandler;
                    }

                    hostContainer.Children.Remove(webView);
                    webView.Dispose();
                }
            }).Task.Unwrap();
        }

        private static bool TryParseMermaidBrowserMessage(string message, out MermaidBrowserMessage result)
        {
            result = MermaidBrowserMessage.Error("Unrecognized Mermaid browser message.", string.Empty);
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(message);
                var root = document.RootElement;
                var type = root.TryGetProperty("type", out var typeElement)
                    ? typeElement.GetString()
                    : string.Empty;
                if (string.Equals(type, "rendered", StringComparison.OrdinalIgnoreCase))
                {
                    var width = root.TryGetProperty("width", out var widthElement) && widthElement.TryGetInt32(out var parsedWidth)
                        ? parsedWidth
                        : MermaidCaptureMinWidth;
                    var height = root.TryGetProperty("height", out var heightElement) && heightElement.TryGetInt32(out var parsedHeight)
                        ? parsedHeight
                        : MermaidCaptureMinHeight;
                    var candidate = root.TryGetProperty("candidate", out var candidateElement)
                        ? candidateElement.GetString() ?? string.Empty
                        : string.Empty;
                    result = MermaidBrowserMessage.Success(width, height, candidate);
                    return true;
                }

                if (string.Equals(type, "error", StringComparison.OrdinalIgnoreCase))
                {
                    var error = root.TryGetProperty("error", out var errorElement)
                        ? errorElement.GetString() ?? "Mermaid render failed."
                        : "Mermaid render failed.";
                    var candidate = root.TryGetProperty("candidate", out var candidateElement)
                        ? candidateElement.GetString() ?? string.Empty
                        : string.Empty;
                    result = MermaidBrowserMessage.Error(error, candidate);
                    return true;
                }
            }
            catch
            {
            }

            return false;
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
                updated = MermaidSubgraphBoundaryRegex.Replace(updated, "${left}\n${right}");
                updated = MermaidEndBoundaryRegex.Replace(updated, "${left}\n${right}");
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
            AppendMarkdown(document, GetWelcomeMarkdown(), false);
        }

        public static string GetWelcomeMarkdown()
        {
            return @"# Welcome to your invisible AI assistant! 🤖

✓ **Completely invisible** to screen sharing  
✓ **Mouse cursor** IS visible  
✓ **Click ⚙️** for settings  

## ⌨️ Hotkeys:
- `Ctrl+Alt+\`` = Hide/Show  
- `Ctrl+Alt+-` = Quit  
- `Ctrl+Alt+=` = Settings  

Ask me anything!";
        }

        private sealed record MarkdownSegment(string Content, bool IsMermaid);

        private sealed class MermaidRenderResult
        {
            public BitmapSource? Image { get; init; }
            public string? ErrorMessage { get; init; }
            public string? Candidate { get; init; }

            public static MermaidRenderResult Success(BitmapSource image)
            {
                return new MermaidRenderResult { Image = image };
            }

            public static MermaidRenderResult Failure(string errorMessage, string candidate)
            {
                return new MermaidRenderResult
                {
                    ErrorMessage = errorMessage,
                    Candidate = candidate
                };
            }
        }

        private sealed class MermaidBrowserMessage
        {
            public bool IsSuccess { get; init; }
            public int Width { get; init; }
            public int Height { get; init; }
            public string? Candidate { get; init; }
            public string? ErrorMessage { get; init; }

            public static MermaidBrowserMessage Success(int width, int height, string candidate)
            {
                return new MermaidBrowserMessage
                {
                    IsSuccess = true,
                    Width = width,
                    Height = height,
                    Candidate = candidate
                };
            }

            public static MermaidBrowserMessage Error(string errorMessage, string candidate)
            {
                return new MermaidBrowserMessage
                {
                    IsSuccess = false,
                    ErrorMessage = errorMessage,
                    Candidate = candidate
                };
            }
        }
    }
}
