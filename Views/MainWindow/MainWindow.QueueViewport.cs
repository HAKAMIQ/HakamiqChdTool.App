using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private const int QueueViewportBufferRows = 8;

    private void TasksDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (TaskQueueItemViewModel removed in e.RemovedItems.OfType<TaskQueueItemViewModel>())
        {
            _viewport.Unpin(removed.QueueItemId);
            _viewport.ReleaseById(removed.QueueItemId);
        }

        foreach (TaskQueueItemViewModel added in e.AddedItems.OfType<TaskQueueItemViewModel>())
        {
            _viewport.Pin(added.QueueItemId);
        }

        TaskQueueItemViewModel? selected = TasksDataGrid.SelectedItem as TaskQueueItemViewModel;
        _viewModel.SelectedTask = selected;

        UpdateSelectionUiState();
    }

    private void TasksDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!IsQueueEmptySurfaceDoubleClick(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (!_viewModel.SelectFilesCommand.CanExecute(null))
        {
            return;
        }

        _viewModel.SelectFilesCommand.Execute(null);
        e.Handled = true;
    }

    private void TasksDataGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is not TaskQueueItemViewModel vm)
        {
            return;
        }

        _viewport.Pin(vm.QueueItemId);
        e.Row.Opacity = 1;

        UpdateQueueViewportWindow();
    }

    private void TasksDataGrid_UnloadingRow(object sender, DataGridRowEventArgs e)
    {
        if (e.Row?.Item is not TaskQueueItemViewModel vm)
        {
            return;
        }

        _viewport.Unpin(vm.QueueItemId);
        _viewport.ReleaseById(vm.QueueItemId);

        UpdateQueueViewportWindow();
    }

    private void TasksDataGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 &&
            e.ViewportHeightChange == 0)
        {
            return;
        }

        UpdateQueueViewportWindow();
    }

    private void UpdateQueueViewportWindow()
    {
        if (_queueViewportUpdateQueued ||
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
        {
            return;
        }

        _queueViewportUpdateQueued = true;

        try
        {
            _ = Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    _queueViewportUpdateQueued = false;

                    if (_shutdownStarted ||
                        _shutdownCompleted ||
                        Dispatcher.HasShutdownStarted ||
                        Dispatcher.HasShutdownFinished)
                    {
                        return;
                    }

                    UpdateQueueViewportWindowCore();
                }),
                DispatcherPriority.Background);
        }
        catch (TaskCanceledException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            _queueViewportUpdateQueued = false;
        }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            _queueViewportUpdateQueued = false;
        }
    }

    private void UpdateQueueViewportWindowCore()
    {
        if (_shutdownStarted ||
            _shutdownCompleted ||
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
        {
            return;
        }

        int itemCount = TasksDataGrid.Items.Count;
        if (itemCount == 0)
        {
            TryUpdateQueueViewportWindow(VisibleQueueWindow.Empty);
            return;
        }

        DataGridRowsPresenter? presenter = _queueRowsPresenterCache;
        if (presenter is null || !presenter.IsVisible)
        {
            presenter = FindVisualChild<DataGridRowsPresenter>(TasksDataGrid);
            _queueRowsPresenterCache = presenter;
        }

        if (presenter is null)
        {
            return;
        }

        int firstVisibleIndex = int.MaxValue;
        int lastVisibleIndex = -1;
        int childCount = VisualTreeHelper.GetChildrenCount(presenter);

        for (int i = 0; i < childCount; i++)
        {
            if (VisualTreeHelper.GetChild(presenter, i) is not DataGridRow row ||
                !row.IsVisible)
            {
                continue;
            }

            int rowIndex = row.GetIndex();
            if (rowIndex < 0)
            {
                continue;
            }

            firstVisibleIndex = Math.Min(firstVisibleIndex, rowIndex);
            lastVisibleIndex = Math.Max(lastVisibleIndex, rowIndex);
        }

        if (lastVisibleIndex < 0)
        {
            return;
        }

        int start = Math.Max(0, firstVisibleIndex - QueueViewportBufferRows);
        int end = Math.Min(itemCount - 1, lastVisibleIndex + QueueViewportBufferRows);

        TryUpdateQueueViewportWindow(new VisibleQueueWindow(
            start,
            end,
            end - start + 1));
    }

    private void TryUpdateQueueViewportWindow(VisibleQueueWindow window)
    {
        if (_shutdownStarted ||
            _shutdownCompleted ||
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            _viewport.UpdateWindow(window);
        }
        catch (ObjectDisposedException ex)
        {
            global::Serilog.Log.Debug(ex, "Queue viewport update ignored after disposal.");
        }
    }

    private static bool IsQueueEmptySurfaceDoubleClick(DependencyObject? source)
    {
        if (source is null)
        {
            return false;
        }

        if (FindVisualParent<DataGridRow>(source) is not null)
        {
            return false;
        }

        if (FindVisualParent<DataGridColumnHeader>(source) is not null)
        {
            return false;
        }

        if (FindVisualParent<ScrollBar>(source) is not null)
        {
            return false;
        }

        if (FindVisualParent<ButtonBase>(source) is not null)
        {
            return false;
        }

        return true;
    }

    private static TChild? FindVisualChild<TChild>(DependencyObject root)
        where TChild : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);

        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is TChild typed)
            {
                return typed;
            }

            TChild? nested = FindVisualChild<TChild>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static TParent? FindVisualParent<TParent>(DependencyObject? source)
        where TParent : DependencyObject
    {
        while (source is not null)
        {
            if (source is TParent typed)
            {
                return typed;
            }

            DependencyObject? parent = null;

            if (source is FrameworkElement frameworkElement)
            {
                parent = frameworkElement.Parent;
            }
            else if (source is FrameworkContentElement frameworkContentElement)
            {
                parent = frameworkContentElement.Parent;
            }

            if (parent is null && source is Visual or Visual3D)
            {
                parent = VisualTreeHelper.GetParent(source);
            }

            source = parent;
        }

        return null;
    }

    private void TasksDataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject) is not { } row)
        {
            return;
        }

        row.IsSelected = true;
        row.Focus();
    }
}