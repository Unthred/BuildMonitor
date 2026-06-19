using BuildMonitor.Infrastructure.Diagnostics;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace BuildMonitor.TrayApp;

public partial class BuildIntelligencePanel : WpfUserControl
{
    public static readonly System.Windows.DependencyProperty SnapshotProperty =
        System.Windows.DependencyProperty.Register(
            nameof(Snapshot),
            typeof(BuildIntelligenceSnapshot),
            typeof(BuildIntelligencePanel),
            new System.Windows.PropertyMetadata(null));

    public BuildIntelligenceSnapshot? Snapshot
    {
        get => (BuildIntelligenceSnapshot?)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    public BuildIntelligencePanel() => InitializeComponent();
}
