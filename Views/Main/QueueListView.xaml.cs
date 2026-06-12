using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HakamiqChdTool.App.Views.Main;

public partial class QueueListView : UserControl
{
    public QueueListView()
    {
        InitializeComponent();
    }

    public event DragEventHandler? TaskGridDrop;

    public event EventHandler<DataGridRowEventArgs>? TasksLoadingRow;

    public event EventHandler<DataGridRowEventArgs>? TasksUnloadingRow;

    public event ScrollChangedEventHandler? TasksScrollChanged;

    public event MouseButtonEventHandler? TasksPreviewMouseRightButtonDown;

    public event MouseButtonEventHandler? TasksMouseDoubleClick;

    public event SelectionChangedEventHandler? TasksSelectionChanged;

    public event RoutedEventHandler? QueueContextMenuOpened;

    public DataGrid TaskGrid => TasksDataGrid;

    private void ForwardTaskGridDrop(object sender, DragEventArgs e) =>
        TaskGridDrop?.Invoke(sender, e);

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
