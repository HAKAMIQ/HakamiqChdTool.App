using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Ui.Queue;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Chd;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using HakamiqChdTool.App.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private void QueueContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu)
        {
            _queueContextMenuViewModel.RaiseAllCanExecuteChanged();
        }
    }

    internal bool TryGetQueueItemExplorerTarget(
        TaskQueueItemViewModel? item,
        out string? targetPath)
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
            ShowNoticeDialog(
                MainWindowMessages.OpenFolderTitle,
                MainWindowMessages.OpenFolderNoPathBody);

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
            ShowNoticeDialog(
                MainWindowMessages.OpenFolderTitle,
                MainWindowMessages.OpenFolderFailedBody);
        }
    }

    internal bool TryGetQueueItemOperationLogTarget(
        TaskQueueItemViewModel? item,
        out string? targetPath)
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

        ChdProbeReportView? chdLogicalReport = await BuildChdLogicalReportForQueueItemAsync(item)
            .ConfigureAwait(true);

        if (_shutdownStarted ||
            _shutdownCompleted ||
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
        {
            return;
        }

        QueueVerifyView presentation = QueueVerificationResultPresenter
            .BuildVerifyView(
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

    private async Task<ChdProbeReportView?> BuildChdLogicalReportForQueueItemAsync(
        TaskQueueItemViewModel item)
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

                ChdProbeReportView? report =
                    ChdLogicalProbeReportFormatter.BuildView(probe);

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
                _ = ex;
            }
        }

        return ChdLogicalProbeReportFormatter.TryBuildViewFromInfoLog(item.LogPath);
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

            return File.Exists(fullPath) &&
                string.Equals(
                    Path.GetExtension(fullPath),
                    ".chd",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsExpectedChdLogicalReportException(ex))
        {
            return false;
        }
    }

    private static bool IsExpectedChdLogicalReportException(Exception ex)
    {
        return ex is ArgumentException
            or IOException
            or NotSupportedException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or PathTooLongException
            or InvalidOperationException;
    }

    private IReadOnlyList<string> ResolveQueueItemExplorerTargets(TaskQueueItemViewModel? item)
    {
        item ??= TasksDataGrid.SelectedItem as TaskQueueItemViewModel;

        if (item is null)
        {
            return [];
        }

        var result = new List<string>(capacity: 1);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        QueueRowData? row = _queueRowStore.GetById(item.QueueItemId);

        string? outputPath = !string.IsNullOrWhiteSpace(row?.OutputPath)
            ? row.OutputPath
            : item.OutputPath;

        AddOutputExplorerTarget(
            outputPath,
            result,
            seen);

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

        if ((File.Exists(fullPath) || Directory.Exists(fullPath)) &&
            seen.Add(fullPath))
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

            if (Directory.Exists(fullParentDirectory) &&
                seen.Add(fullParentDirectory))
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
            TasksDataGrid.SelectedIndex = Math.Max(
                0,
                Math.Min(selectedIndex, _queueView.Count - 1));

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

        if (!RequireAppFeature(AppFeature.AdvancedQueue))
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

        if (!item.HasActiveQueueBinding ||
            TaskQueueStateCodes.IsTerminal(item.CurrentState))
        {
            return;
        }

        _queue.Cancel(item.QueueItemId);
    }
}