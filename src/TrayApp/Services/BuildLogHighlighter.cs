using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using BuildMonitor.Infrastructure.LocalBuild;
using WpfColor = System.Windows.Media.Color;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfRichTextBox = System.Windows.Controls.RichTextBox;
using WpfScrollViewer = System.Windows.Controls.ScrollViewer;

namespace BuildMonitor.TrayApp.Services;

public static class BuildLogHighlighter
{
    public static IReadOnlyList<Run> Apply(
        WpfRichTextBox box,
        string logText,
        ThemePalette palette,
        ResolvedTheme theme)
    {
        box.Document.Blocks.Clear();

        var paragraph = new Paragraph
        {
            Margin = new Thickness(0),
            LineHeight = 1
        };

        var lineRuns = new List<Run>();
        var normalized = StripAnsi(logText.Replace("\r\n", "\n"));
        foreach (var line in normalized.Split('\n'))
        {
            var displayLine = line.TrimEnd('\r');
            var run = new Run(displayLine)
            {
                Foreground = new SolidColorBrush(ClassifyColor(displayLine, theme))
            };
            lineRuns.Add(run);
            paragraph.Inlines.Add(run);
            paragraph.Inlines.Add(new LineBreak());
        }

        box.Document.Blocks.Add(paragraph);
        box.Document.FontFamily = new WpfFontFamily("Consolas");
        box.Document.FontSize = 12;
        box.Background = new SolidColorBrush(palette.Background);
        box.Foreground = new SolidColorBrush(palette.Foreground);

        return lineRuns;
    }

    public static void HighlightIssue(
        WpfRichTextBox box,
        IReadOnlyList<Run> lineRuns,
        ref Run? highlightedRun,
        LogIssue issue,
        string displayLogText,
        ResolvedTheme theme)
    {
        ClearHighlight(ref highlightedRun);

        var lineIndex = ResolveLineIndex(issue, lineRuns, displayLogText);
        if (lineIndex < 0 || lineIndex >= lineRuns.Count)
        {
            return;
        }

        var run = lineRuns[lineIndex];
        run.Background = CreateLineHighlightBrush(theme);
        highlightedRun = run;
        SelectRun(box, run);
    }

    private static int ResolveLineIndex(
        LogIssue issue,
        IReadOnlyList<Run> lineRuns,
        string displayLogText)
    {
        if (issue.LineNumber >= 0
            && issue.LineNumber < lineRuns.Count
            && string.Equals(lineRuns[issue.LineNumber].Text, issue.Text.Trim(), StringComparison.Ordinal))
        {
            return issue.LineNumber;
        }

        var issueText = issue.Text.Trim();
        for (var i = 0; i < lineRuns.Count; i++)
        {
            var runText = lineRuns[i].Text;
            if (string.Equals(runText, issueText, StringComparison.Ordinal)
                || runText.Contains(issueText, StringComparison.Ordinal)
                || issueText.Contains(runText, StringComparison.Ordinal))
            {
                return i;
            }
        }

        var normalized = StripAnsi(displayLogText.Replace("\r\n", "\n"));
        var lines = normalized.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (string.Equals(line, issueText, StringComparison.Ordinal)
                || line.Contains(issueText, StringComparison.Ordinal))
            {
                return Math.Min(i, lineRuns.Count - 1);
            }
        }

        return issue.LineNumber >= 0 && issue.LineNumber < lineRuns.Count
            ? issue.LineNumber
            : -1;
    }

    public static void ClearHighlight(ref Run? highlightedRun)
    {
        if (highlightedRun is not null)
        {
            highlightedRun.Background = null;
            highlightedRun = null;
        }
    }

    private static SolidColorBrush CreateLineHighlightBrush(ResolvedTheme theme) =>
        theme == ResolvedTheme.Dark
            ? new SolidColorBrush(WpfColor.FromArgb(100, 62, 122, 180))
            : new SolidColorBrush(WpfColor.FromArgb(120, 0, 102, 204));

    private static void SelectRun(WpfRichTextBox box, Run run)
    {
        try
        {
            var start = run.ContentStart;
            var end = run.ContentEnd;

            box.Selection.Select(start, end);
            box.CaretPosition = start;
            ScrollToPointer(box, start);
        }
        catch (ArgumentException)
        {
            // Ignore invalid selection ranges.
        }
    }

    private static void ScrollToPointer(WpfRichTextBox box, TextPointer pointer)
    {
        try
        {
            var rect = pointer.GetCharacterRect(LogicalDirection.Forward);
            if (IsValidRect(rect))
            {
                ScrollRectIntoView(box, rect);
                return;
            }
        }
        catch (InvalidOperationException)
        {
            // Layout not ready yet.
        }

        box.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            try
            {
                var rect = pointer.GetCharacterRect(LogicalDirection.Forward);
                if (IsValidRect(rect))
                {
                    ScrollRectIntoView(box, rect);
                }
            }
            catch (InvalidOperationException)
            {
                // Best effort only.
            }
        });
    }

    private static bool IsValidRect(Rect rect) =>
        !double.IsNaN(rect.Top)
        && !double.IsInfinity(rect.Top)
        && rect.Height > 0;

    private static void ScrollRectIntoView(WpfRichTextBox box, Rect rect)
    {
        var scrollViewer = FindScrollViewer(box);
        scrollViewer?.ScrollToVerticalOffset(Math.Max(0, rect.Top - 40));
    }

    private static WpfScrollViewer? FindScrollViewer(DependencyObject element)
    {
        if (element is WpfScrollViewer viewer)
        {
            return viewer;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            var found = FindScrollViewer(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static WpfColor ClassifyColor(string line, ResolvedTheme theme)
    {
        if (Contains(line, "Build FAILED")
            || Contains(line, "Test Run Failed")
            || Contains(line, ": error ")
            || Contains(line, "error CS")
            || Contains(line, "error MSB")
            || line.TrimStart().StartsWith("Failed ", StringComparison.OrdinalIgnoreCase)
            || Contains(line, "[FAIL]"))
        {
            return WpfColor.FromRgb(220, 53, 69);
        }

        if (Contains(line, ": warning ")
            || Contains(line, "warning CS")
            || Contains(line, "warning MSB"))
        {
            return theme == ResolvedTheme.Dark
                ? WpfColor.FromRgb(255, 193, 7)
                : WpfColor.FromRgb(180, 120, 0);
        }

        if (Contains(line, "Build succeeded")
            || Contains(line, "Test Run Successful")
            || Contains(line, "Passed!")
            || line.TrimStart().StartsWith("Passed ", StringComparison.OrdinalIgnoreCase)
            || Contains(line, "[PASS]"))
        {
            return WpfColor.FromRgb(40, 167, 69);
        }

        if (Contains(line, "Determining projects to restore")
            || Contains(line, "Time Elapsed")
            || Contains(line, "Restore succeeded")
            || Contains(line, "Restore completed"))
        {
            return theme == ResolvedTheme.Dark
                ? WpfColor.FromRgb(158, 158, 158)
                : WpfColor.FromRgb(108, 117, 125);
        }

        return theme == ResolvedTheme.Dark
            ? WpfColor.FromRgb(230, 230, 230)
            : WpfColor.FromRgb(33, 37, 41);
    }

    private static bool Contains(string line, string marker) =>
        line.Contains(marker, StringComparison.OrdinalIgnoreCase);

    private static string StripAnsi(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\x1b\[[0-9;]*m", string.Empty);
}
