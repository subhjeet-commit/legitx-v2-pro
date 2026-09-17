using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace LegitX.WPF.Controls;

/// <summary>
/// A TextBlock that renders basic Markdown formatting:
/// **bold**, *italic*, `inline code`, ```code blocks```, - bullet lists, # headers, > blockquotes
/// </summary>
public class MarkdownTextBlock : TextBlock
{
    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.Register(nameof(Markdown), typeof(string), typeof(MarkdownTextBlock),
            new PropertyMetadata(string.Empty, OnMarkdownChanged));

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownTextBlock tb)
            tb.RenderMarkdown((string?)e.NewValue ?? "");
    }

    // Theme colors
    private static readonly SolidColorBrush CodeBg = new(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush CodeFg = new(Color.FromRgb(0xFF, 0x6B, 0x6B));
    private static readonly SolidColorBrush CodeBlockBg = new(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush AccentColor = new(Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly SolidColorBrush DimColor = new(Color.FromRgb(0x71, 0x71, 0x7A));
    private static readonly SolidColorBrush BulletColor = new(Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly SolidColorBrush QuoteBorder = new(Color.FromArgb(0x60, 0xEF, 0x44, 0x44));
    private static readonly FontFamily MonoFont = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#JetBrains Mono");
    private static readonly FontFamily UiFont = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Inter");

    static MarkdownTextBlock()
    {
        CodeBg.Freeze(); CodeFg.Freeze(); CodeBlockBg.Freeze();
        AccentColor.Freeze(); DimColor.Freeze(); BulletColor.Freeze(); QuoteBorder.Freeze();
    }

    private void RenderMarkdown(string markdown)
    {
        Inlines.Clear();
        if (string.IsNullOrEmpty(markdown)) return;

        var lines = markdown.Split('\n');
        var inCodeBlock = false;
        var codeBlockContent = new System.Text.StringBuilder();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Handle fenced code blocks ```
            if (line.TrimStart().StartsWith("```"))
            {
                if (inCodeBlock)
                {
                    // End code block
                    AddCodeBlock(codeBlockContent.ToString().TrimEnd());
                    codeBlockContent.Clear();
                    inCodeBlock = false;
                }
                else
                {
                    // Start code block
                    inCodeBlock = true;
                }
                continue;
            }

            if (inCodeBlock)
            {
                if (codeBlockContent.Length > 0) codeBlockContent.AppendLine();
                codeBlockContent.Append(line);
                continue;
            }

            // Add newline between lines (but not before the first)
            if (i > 0 && Inlines.Count > 0)
                Inlines.Add(new LineBreak());

            var trimmed = line.TrimStart();

            // Headers: # ## ###
            if (trimmed.StartsWith("### "))
            {
                AddHeader(trimmed[4..], 13.5, FontWeights.Bold);
                continue;
            }
            if (trimmed.StartsWith("## "))
            {
                AddHeader(trimmed[3..], 14.5, FontWeights.Bold);
                continue;
            }
            if (trimmed.StartsWith("# "))
            {
                AddHeader(trimmed[2..], 16, FontWeights.Bold);
                continue;
            }

            // Blockquote: > text
            if (trimmed.StartsWith("> "))
            {
                AddBlockquote(trimmed[2..]);
                continue;
            }

            // Bullet list: - item or * item or • item
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("• "))
            {
                AddBullet(trimmed[2..]);
                continue;
            }

            // Numbered list: 1. item, 2. item, etc.
            var numMatch = Regex.Match(trimmed, @"^(\d+)\.\s+(.+)");
            if (numMatch.Success)
            {
                AddNumberedItem(numMatch.Groups[1].Value, numMatch.Groups[2].Value);
                continue;
            }

            // Regular line: parse inline formatting
            ParseInlineFormatting(line);
        }

        // Handle unclosed code block
        if (inCodeBlock && codeBlockContent.Length > 0)
        {
            AddCodeBlock(codeBlockContent.ToString().TrimEnd());
        }
    }

    private void AddHeader(string text, double fontSize, FontWeight weight)
    {
        var run = new Run(text)
        {
            FontSize = fontSize,
            FontWeight = weight,
            Foreground = Brushes.White,
            FontFamily = UiFont
        };
        Inlines.Add(run);
    }

    private void AddBlockquote(string text)
    {
        // Vertical bar + indented text
        var bar = new Run("┃ ")
        {
            Foreground = QuoteBorder,
            FontWeight = FontWeights.Bold
        };
        Inlines.Add(bar);

        var run = new Run(text)
        {
            Foreground = DimColor,
            FontStyle = FontStyles.Italic
        };
        Inlines.Add(run);
    }

    private void AddBullet(string text)
    {
        var bullet = new Run("  ●  ")
        {
            Foreground = BulletColor,
            FontSize = 8
        };
        Inlines.Add(bullet);
        ParseInlineFormatting(text);
    }

    private void AddNumberedItem(string number, string text)
    {
        var num = new Run($"  {number}. ")
        {
            Foreground = AccentColor,
            FontWeight = FontWeights.SemiBold
        };
        Inlines.Add(num);
        ParseInlineFormatting(text);
    }

    private void AddCodeBlock(string code)
    {
        if (Inlines.Count > 0)
            Inlines.Add(new LineBreak());

        // Top padding
        var topPad = new Run("  ") { FontSize = 4 };
        Inlines.Add(topPad);
        Inlines.Add(new LineBreak());

        var lines = code.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var run = new Run("  " + lines[i] + "  ")
            {
                FontFamily = MonoFont,
                FontSize = 11.5,
                Foreground = CodeFg,
                Background = CodeBlockBg,
            };
            Inlines.Add(run);
            if (i < lines.Length - 1)
                Inlines.Add(new LineBreak());
        }

        // Bottom padding
        Inlines.Add(new LineBreak());
        var botPad = new Run("  ") { FontSize = 4 };
        Inlines.Add(botPad);
    }

    /// <summary>
    /// Parses inline formatting: **bold**, *italic*, `code`, ~~strikethrough~~, __underline__
    /// </summary>
    private void ParseInlineFormatting(string text)
    {
        // Regex to match inline patterns, ordered by priority
        // `code` | **bold** | __underline__ | *italic* | _italic_ | ~~strike~~
        var pattern = @"(`[^`]+`)|(\*\*[^*]+\*\*)|(__[^_]+__)|(\*[^*]+\*)|(~~[^~]+~~)|(_[^_]+_)";
        var matches = Regex.Matches(text, pattern);

        int lastIndex = 0;
        foreach (Match match in matches)
        {
            // Add plain text before this match
            if (match.Index > lastIndex)
            {
                var plain = text[lastIndex..match.Index];
                Inlines.Add(new Run(plain));
            }

            var val = match.Value;

            if (val.StartsWith('`'))
            {
                // Inline code
                var code = val.Trim('`');
                var run = new Run($" {code} ")
                {
                    FontFamily = MonoFont,
                    FontSize = 11.5,
                    Foreground = CodeFg,
                    Background = CodeBg,
                };
                Inlines.Add(run);
            }
            else if (val.StartsWith("**"))
            {
                // Bold
                var content = val[2..^2];
                var run = new Run(content)
                {
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                };
                Inlines.Add(run);
            }
            else if (val.StartsWith("__"))
            {
                // Underline
                var content = val[2..^2];
                var underlineRun = new Run(content);
                underlineRun.TextDecorations = System.Windows.TextDecorations.Underline;
                Inlines.Add(underlineRun);
            }
            else if (val.StartsWith("~~"))
            {
                // Strikethrough
                var content = val[2..^2];
                var strikeRun = new Run(content) { Foreground = DimColor };
                strikeRun.TextDecorations = System.Windows.TextDecorations.Strikethrough;
                Inlines.Add(strikeRun);
            }
            else if (val.StartsWith('*') || val.StartsWith('_'))
            {
                // Italic
                var content = val[1..^1];
                var run = new Run(content)
                {
                    FontStyle = FontStyles.Italic,
                };
                Inlines.Add(run);
            }

            lastIndex = match.Index + match.Length;
        }

        // Remaining plain text
        if (lastIndex < text.Length)
        {
            Inlines.Add(new Run(text[lastIndex..]));
        }
    }
}
