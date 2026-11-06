using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;

namespace HakamiqChdTool.App.Views.Main;

public partial class QueueWorkspaceView : UserControl
{
    public static readonly DependencyProperty OperationModeKeyProperty =
        DependencyProperty.Register(
            nameof(OperationModeKey),
            typeof(string),
            typeof(QueueWorkspaceView),
            new PropertyMetadata("Convert"));

    public QueueWorkspaceView()
    {
        InitializeComponent();
    }

    public event DragEventHandler? WorkspaceFilesDropped;

    public event DragEventHandler? WorkspacePreviewDragOver;

    public event DragEventHandler? WorkspacePreviewDragLeave;

    public event DragEventHandler? WorkspacePreviewDrop;

    public event EventHandler<DataGridRowEventArgs>? TasksLoadingRow;

    public event EventHandler<DataGridRowEventArgs>? TasksUnloadingRow;

    public event ScrollChangedEventHandler? TasksScrollChanged;

    public event MouseButtonEventHandler? TasksPreviewMouseRightButtonDown;

    public event MouseButtonEventHandler? TasksMouseDoubleClick;

    public event SelectionChangedEventHandler? TasksSelectionChanged;

    public event RoutedEventHandler? QueueContextMenuOpened;

    public string OperationModeKey
    {
        get => (string)GetValue(OperationModeKeyProperty);
        set => SetValue(OperationModeKeyProperty, string.IsNullOrWhiteSpace(value) ? "Convert" : value);
    }

    public DataGrid TaskGrid => QueueListSection.TaskGrid;

    public Rectangle DropZoneFrame => DropZoneDashedFrame;

    public Border DropHighlight => DropHighlightOverlay;

    public Border EmptyDropHint => EmptyQueueDropHint;

    private void ForwardWorkspaceDrop(object sender, DragEventArgs e) =>
        WorkspaceFilesDropped?.Invoke(sender, e);

    private void ForwardWorkspacePreviewDragOver(object sender, DragEventArgs e) =>
        WorkspacePreviewDragOver?.Invoke(sender, e);

    private void ForwardWorkspacePreviewDragLeave(object sender, DragEventArgs e) =>
        WorkspacePreviewDragLeave?.Invoke(sender, e);

    private void ForwardWorkspacePreviewDrop(object sender, DragEventArgs e) =>
        WorkspacePreviewDrop?.Invoke(sender, e);

    private void ForwardTasksLoadingRow(object? sender, DataGridRowEventArgs e) =>
        TasksLoadingRow?.Invoke(sender, e);

    private void ForwardTasksUnloadingRow(object sender, DataGridRowEventArgs e) =>
        TasksUnloadingRow?.Invoke(sender, e);

    private void ForwardTasksScrollChanged(object sender, ScrollChangedEventArgs e) =>
        TasksScrollChanged?.Invoke(sender, e);

    private void ForwardTasksPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) =>
        TasksPreviewMouseRightButtonDown?.Invoke(sender, e);

    private void ForwardTasksMouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        TasksMouseDoubleClick?.Invoke(sender, e);

    private void ForwardTasksSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        TasksSelectionChanged?.Invoke(sender, e);

    private void ForwardQueueContextMenuOpened(object sender, RoutedEventArgs e) =>
        QueueContextMenuOpened?.Invoke(sender, e);
}
