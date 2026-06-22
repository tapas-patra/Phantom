using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Wpf;

namespace SecureOverlay
{
    public static class MarkdownHelper
    {
        private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
            .UseSupportedExtensions()
            .Build();

        public static void AppendMarkdown(FlowDocument document, string markdown, bool isUser = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(markdown))
                    return;

                // Create a temporary FlowDocument to parse markdown
                var tempDoc = Markdig.Wpf.Markdown.ToFlowDocument(markdown, _pipeline);

                // Style the content based on whether it's user or AI
                StyleDocument(tempDoc, isUser);

                // Add all blocks to the main document
                while (tempDoc.Blocks.Count > 0)
                {
                    var block = tempDoc.Blocks.FirstBlock;
                    tempDoc.Blocks.Remove(block);
                    document.Blocks.Add(block);
                }

                // Add spacing after the message
                document.Blocks.Add(new Paragraph(new Run("")) 
                { 
                    Margin = new Thickness(0, 0, 0, 10) 
                });
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Markdown rendering error: {ex.Message}");
                
                // Fallback to plain text
                var paragraph = new Paragraph(new Run(markdown))
                {
                    Foreground = isUser ? Brushes.LightBlue : Brushes.White,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 14
                };
                document.Blocks.Add(paragraph);
            }
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
                    
                    paragraph.Background = new SolidColorBrush(Color.FromRgb(25, 25, 25));
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
                    // Dark background for code blocks
                    border.Background = new SolidColorBrush(Color.FromRgb(25, 25, 25));
                    border.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 200, 0));
                    border.BorderThickness = new Thickness(2);
                    border.CornerRadius = new CornerRadius(5);
                    border.Padding = new Thickness(10);
                    
                    if (border.Child is System.Windows.Controls.TextBlock textBlock)
                    {
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
                            run.Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)); // Dark background
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
✓ Mouse cursor **IS visible**  
✓ Click ⚙️ for settings  

## ⌨️ Hotkeys:
- `Ctrl+Alt+`` = Hide/Show  
- `Ctrl+Alt+-` = Quit  
- `Ctrl+Alt+=` = Settings  

Ask me anything!";

            AppendMarkdown(document, welcome, false);
        }
    }
}
