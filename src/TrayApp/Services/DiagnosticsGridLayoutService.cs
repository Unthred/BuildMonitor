using System.Windows.Controls;
using WpfDataGrid = System.Windows.Controls.DataGrid;

namespace BuildMonitor.TrayApp.Services;

internal static class DiagnosticsGridLayoutService
{
    private static readonly string[] ColumnKeys =
    [
        "When",
        "Kind",
        "Summary",
        "Files",
        "LikelyCause",
        "Detail",
        "Verdict",
        "YourNote",
        "Mark"
    ];

    public static void ApplyColumnWidths(WpfDataGrid grid, IReadOnlyDictionary<string, double>? savedWidths)
    {
        if (savedWidths is null || savedWidths.Count == 0)
        {
            return;
        }

        for (var i = 0; i < grid.Columns.Count && i < ColumnKeys.Length; i++)
        {
            var key = ColumnKeys[i];
            if (savedWidths.TryGetValue(key, out var width) && width >= 40)
            {
                grid.Columns[i].Width = new DataGridLength(width);
            }
        }
    }

    public static Dictionary<string, double> CaptureColumnWidths(WpfDataGrid grid)
    {
        var widths = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var i = 0; i < grid.Columns.Count && i < ColumnKeys.Length; i++)
        {
            var width = grid.Columns[i].ActualWidth;
            if (width >= 40)
            {
                widths[ColumnKeys[i]] = width;
            }
        }

        return widths;
    }
}
