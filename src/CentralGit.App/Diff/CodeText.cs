using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CentralGit.App.Diff;

/// <summary>
/// Attached property that renders a <see cref="CodeLine"/> into a TextBlock: one Run per
/// syntax token, and TextHighlighters for the intraline changes. Containers are recycled by the
/// ListView, so every set fully replaces what was there.
/// </summary>
public static class CodeText
{
    public static readonly DependencyProperty LineProperty = DependencyProperty.RegisterAttached(
        "Line", typeof(CodeLine), typeof(CodeText), new PropertyMetadata(null, OnLineChanged));

    public static CodeLine? GetLine(TextBlock element) => (CodeLine?)element.GetValue(LineProperty);

    public static void SetLine(TextBlock element, CodeLine? value) => element.SetValue(LineProperty, value);

    private static readonly Dictionary<Color, SolidColorBrush> Brushes = [];

    private static SolidColorBrush Brush(Color c)
    {
        if (!Brushes.TryGetValue(c, out var b))
            Brushes[c] = b = new SolidColorBrush(c);
        return b;
    }

    private static void OnLineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var block = (TextBlock)d;
        block.Inlines.Clear();
        block.TextHighlighters.Clear();

        if (e.NewValue is not CodeLine line)
            return;

        foreach (var run in line.Runs)
            block.Inlines.Add(new Run { Text = line.Text.Substring(run.Start, run.Length), Foreground = Brush(run.Color) });

        if (line.Highlights.Count > 0)
        {
            var highlighter = new TextHighlighter { Background = Brush(line.HighlightColor), Foreground = Brush(Theming.Palette.TextPrimary) };
            foreach (var h in line.Highlights)
                highlighter.Ranges.Add(new TextRange { StartIndex = h.Start, Length = h.Length });
            block.TextHighlighters.Add(highlighter);
        }
    }
}
