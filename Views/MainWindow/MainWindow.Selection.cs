using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Views;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private const int QueueViewportBufferRows = 8;

    private void QueueContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu)
        {
            _queueContextMenuViewModel.RaiseAllCanExecuteChanged();
        }
    }

    internal bool TryGetQueueItemExplorerTarget(TaskQueueItemViewModel? item, out string? targetPath)
    {
        targetPath = null;

        IReadOnlyList<string> targets = ResolveQueueItemExplorerTargets(item);
        if (targets.Count == 0)
        {
            return false;
        }

        targetPath = targets[0];
        return true;
    }

    private void OpenFolderForQueueItem(TaskQueueItemViewModel? item)
    {
        IReadOnlyList<string> targetPaths = ResolveQueueItemExplorerTargets(item);
        if (targetPaths.Count == 0)
        {
            SetFooterStatus(MainWindowMessages.OpenFolderNoPathFooter);

            ShowNoticeDialog(MainWindowMessages.OpenFolderTitle, MainWindowMessages.OpenFolderNoPathBody);

            return;
        }

        bool openedAny = false;

        foreach (string targetPath in targetPaths)
        {
            openedAny |= _windowActivationService.TryShowPath(targetPath);
        }

        if (!openedAny)
        {
            SetFooterStatus(MainWindowMessages.OpenFolderFailedFooter);

            ShowNoticeDialog(MainWindowMessages.OpenFolderTitle, MainWindowMessages.OpenFolderFailedBody);
        }
    }

    internal bool TryGetQueueItemOperationLogTarget(TaskQueueItemViewModel? item, out string? targetPath)
    {
        targetPath = null;
        item ??= TasksDataGrid.SelectedItem as TaskQueueItemViewModel;

        if (item is null || !item.HasVerificationResult)
        {
            return false;
        }

        targetPath = item.LogPath;
        return true;
    }

    internal async Task OpenOperationLogForQueueItemAsync(TaskQueueItemViewModel? item)
    {
        item ??= TasksDataGrid.SelectedItem as TaskQueueItemViewModel;
        if (item is null || !item.HasVerificationResult)
        {
            SetFooterStatus(MainWindowMessages.OpenFolderNoPathFooter);
            return;
        }

        ChdLogicalProbeReportPresentation? chdLogicalReport = await BuildChdLogicalReportForQueueItemAsync(item)
            .ConfigureAwait(true);

        if (_shutdownStarted
            || _shutdownCompleted
            || Dispatcher.HasShutdownStarted
            || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        QueueVerificationResultPresentation presentation =
            QueueVerificationResultPresenter.BuildVerificationResultPresentation(
                item.FileName,
                item.FileTitleDisplay,
                item.VerificationResultBadgeText,
                item.IntegrityState,
                item.IntegrityStatusMessage,
                item.QueueRowDisplayDetailArabic,
                chdLogicalReport);

        var dialog = new VerificationResultDialog(presentation)
        {
            Owner = this
        };

        _ = dialog.ShowDialog();
    }

    private async Task<ChdLogicalProbeReportPresentation?> BuildChdLogicalReportForQueueItemAsync(TaskQueueItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        string outputPath = ResolveQueueItemOutputPath(item);
        if (IsReadableChdFile(outputPath))
        {
            try
            {
                ChdLogicalProbeResult probe = await ChdLogicalProbeReportService
    .ProbeAsync(outputPath, _windowLifetimeCts.Token)
    .ConfigureAwait(true);

                ChdLogicalProbeReportPresentation? report = ChdLogicalProbeReportFormatter.BuildPresentation(probe);
                if (report?.HasMetrics == true)
                {
                    return report;
                }
            }
            catch (OperationCanceledException) when (_windowLifetimeCts.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception ex) when (IsExpectedChdLogicalReportException(ex))
            {
                // Fall back to the saved info log, if the operation already produced one.
            }
        }

        return ChdLogicalProbeReportFormatter.TryBuildPresentationFromInfoLog(item.LogPath);
    }

    private string ResolveQueueItemOutputPath(TaskQueueItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        QueueRowData? row = _queueRowStore.GetById(item.QueueItemId);
        if (!string.IsNullOrWhiteSpace(row?.OutputPath))
        {
            return row.OutputPath.Trim();
        }

        return string.IsNullOrWhiteSpace(item.OutputPath)
            ? string.Empty
            : item.OutputPath.Trim();
    }

    private static bool IsReadableChdFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path.Trim());
            return File.Exists(fullPath)
                && string.Equals(Path.GetExtension(fullPath), ".chd", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsExpectedChdLogicalReportException(ex))
        {
            return false;
        }
    }

    private static bool IsExpectedChdLogicalReportException(Exception ex) =>
        ex is ArgumentException
        or IOException
        or NotSupportedException
        or UnauthorizedAccessException
        or System.Security.SecurityException
        or PathTooLongException
        or InvalidOperationException;

    private IReadOnlyList<string> ResolveQueueItemExplorerTargets(TaskQueueItemViewModel? item)
    {
        item ??= TasksDataGrid.SelectedItem as TaskQueueItemViewModel;

        if (item is null)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>(capacity: 1);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        QueueRowData? row = _queueRowStore.GetById(item.QueueItemId);

        string? outputPath = !string.IsNullOrWhiteSpace(row?.OutputPath)
            ? row.OutputPath
            : item.OutputPath;

        AddOutputExplorerTarget(outputPath, result, seen);

        return result;
    }

    private static void AddOutputExplorerTarget(
        string? path,
        ICollection<string> result,
        ISet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path.Trim());
        }
        catch (ArgumentException)
        {
            return;
        }
        catch (NotSupportedException)
        {
            return;
        }
        catch (PathTooLongException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        if ((File.Exists(fullPath) || Directory.Exists(fullPath)) && seen.Add(fullPath))
        {
            result.Add(fullPath);
            return;
        }

        string? parentDirectory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            return;
        }

        try
        {
            string fullParentDirectory = Path.GetFullPath(parentDirectory);

            if (Directory.Exists(fullParentDirectory) && seen.Add(fullParentDirectory))
            {
                result.Add(fullParentDirectory);
            }
        }
        catch (ArgumentException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (PathTooLongException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    internal void RemoveQueueItemFromSession(TaskQueueItemViewModel? item)
    {
        item ??= TasksDataGrid.SelectedItem as TaskQueueItemViewModel;

        if (item is null || IsQueueInteractionLocked)
        {
            return;
        }

        int selectedIndex = TasksDataGrid.SelectedIndex;
        _queueRowStore.TryRemove(item.QueueItemId);

        if (_queueView.Count > 0)
        {
            TasksDataGrid.SelectedIndex = Math.Max(0, Math.Min(selectedIndex, _queueView.Count - 1));
            _viewModel.SelectedTask = TasksDataGrid.SelectedItem as TaskQueueItemViewModel;
        }
        else
        {
            TasksDataGrid.SelectedItem = null;
            _viewModel.SelectedTask = null;
        }

        SetFooterStatus(MainWindowMessages.ItemRemovedFooter);
        UpdateUiState();
    }

    internal void RetryQueueItemFromSession(TaskQueueItemViewModel? item)
    {
        item ??= TasksDataGrid.SelectedItem as TaskQueueItemViewModel;

        if (item is null || IsQueueInteractionLocked)
        {
            return;
        }

        if (!RequirePremiumFeature(PremiumFeature.AdvancedQueue))
        {
            return;
        }

        item.ResetForRetry(MainWindowMessages.ReadyForProcessing);
        SyncRowFromViewModel(item);
        UpdateUiState();
    }

    internal void CancelQueueJobFromSession(TaskQueueItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (!item.HasActiveQueueBinding || TaskQueueStateCodes.IsTerminal(item.CurrentState))
        {
            return;
        }

        _queue.Cancel(item.QueueItemId);
    }

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
        if (e.VerticalChange == 0 && e.ViewportHeightChange == 0)
        {
            return;
        }

        UpdateQueueViewportWindow();
    }

    private void UpdateQueueViewportWindow()
    {
        if (_queueViewportUpdateQueued || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
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

                    if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished || _shutdownCompleted)
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
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished || _shutdownCompleted)
        {
            return;
        }

        int itemCount = TasksDataGrid.Items.Count;
        if (itemCount == 0)
        {
            _viewport.UpdateWindow(VisibleQueueWindow.Empty);
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
            if (VisualTreeHelper.GetChild(presenter, i) is not DataGridRow row || !row.IsVisible)
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

        _viewport.UpdateWindow(new VisibleQueueWindow(start, end, end - start + 1));
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
