using System.Windows;
using System.Windows.Controls;
using BuildMonitor.Core.Models;

namespace BuildMonitor.TrayApp;

public partial class ControlPlaneMetricsPanel : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(
        nameof(Snapshot),
        typeof(ControlPlaneMetricsSnapshot),
        typeof(ControlPlaneMetricsPanel),
        new PropertyMetadata(null));

    public static readonly DependencyProperty WorkflowProperty = DependencyProperty.Register(
        nameof(Workflow),
        typeof(ControlPlaneWorkflowSnapshot),
        typeof(ControlPlaneMetricsPanel),
        new PropertyMetadata(null));

    public ControlPlaneMetricsPanel()
    {
        InitializeComponent();
    }

    public ControlPlaneMetricsSnapshot? Snapshot
    {
        get => (ControlPlaneMetricsSnapshot?)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    public ControlPlaneWorkflowSnapshot? Workflow
    {
        get => (ControlPlaneWorkflowSnapshot?)GetValue(WorkflowProperty);
        set => SetValue(WorkflowProperty, value);
    }
}
