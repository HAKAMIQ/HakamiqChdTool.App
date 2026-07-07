using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Features;
using HakamiqChdTool.App.Ui.Queue;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using HakamiqChdTool.App.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
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

        if (!TryNormalizeExistingExplorerFile(item.LogPath, out string logPath))
        {
            return false;
        }

        targetPath = logPath;
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

    private static Task<ChdProbeReportView?> BuildChdLogicalReportForQueueItemAsync(
        TaskQueueItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!TryNormalizeExistingExplorerFile(item.LogPath, out string logPath))
        {
            return Task.FromResult<ChdProbeReportView?>(null);
        }

        return Task.FromResult(ChdLogicalProbeReportFormatter.TryBuildViewFromInfoLog(logPath));
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

        string? sourcePath = !string.IsNullOrWhiteSpace(row?.OriginalPath)
            ? row.OriginalPath
            : !string.IsNullOrWhiteSpace(row?.SourcePath)
                ? row.SourcePath
                : !string.IsNullOrWhiteSpace(item.OriginalPath)
                    ? item.OriginalPath
                    : item.SourcePath;

        if (string.Equals(item.QueueRowDisplayState, TaskQueueStateCodes.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            AddOutputExplorerTarget(
                sourcePath,
                result,
                seen);

            return result;
        }

        string? outputPath = !string.IsNullOrWhiteSpace(row?.OutputPath)
            ? row.OutputPath
            : item.OutputPath;

        AddOutputExplorerTarget(
            outputPath,
            result,
            seen);

        if (result.Count == 0)
        {
            AddOutputExplorerTarget(
                sourcePath,
                result,
                seen);
        }

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

        if (TryAddExistingExplorerTarget(path, result, seen))
        {
            return;
        }

        if (!TryNormalizeExplorerCandidatePath(path, out string fullPath))
        {
            return;
        }

        string? parentDirectory;

        try
        {
            parentDirectory = Path.GetDirectoryName(fullPath);
        }
        catch (Exception ex) when (IsExpectedExplorerTargetPathException(ex))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            return;
        }

        _ = TryAddExistingExplorerDirectory(parentDirectory, result, seen);
    }

    private static bool TryAddExistingExplorerTarget(
        string? path,
        ICollection<string> result,
        ISet<string> seen)
    {
        if (!TryNormalizeExistingExplorerTarget(path, out string normalizedPath, out _))
        {
            return false;
        }

        if (!seen.Add(normalizedPath))
        {
            return true;
        }

        result.Add(normalizedPath);
        return true;
    }

    private static bool TryAddExistingExplorerDirectory(
        string? path,
        ICollection<string> result,
        ISet<string> seen)
    {
        if (!TryNormalizeExistingExplorerTarget(path, out string normalizedPath, out FileAttributes attributes))
        {
            return false;
        }

        if ((attributes & FileAttributes.Directory) == 0)
        {
            return false;
        }

        if (!seen.Add(normalizedPath))
        {
            return true;
        }

        result.Add(normalizedPath);
        return true;
    }

    private static bool TryNormalizeExistingExplorerFile(
        string? path,
        out string normalizedPath)
    {
        normalizedPath = string.Empty;

        if (!TryNormalizeExistingExplorerTarget(path, out string candidate, out FileAttributes attributes))
        {
            return false;
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            return false;
        }

        normalizedPath = candidate;
        return true;
    }

    private static bool TryNormalizeExistingExplorerTarget(
        string? path,
        out string normalizedPath,
        out FileAttributes attributes)
    {
        normalizedPath = string.Empty;
        attributes = default;

        if (!TryNormalizeExplorerCandidatePath(path, out string fullPath))
        {
            return false;
        }

        try
        {
            attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (IsExpectedExplorerTargetPathException(ex))
        {
            return false;
        }
    }

    private static bool TryNormalizeExplorerCandidatePath(
        string? path,
        out string normalizedPath)
    {
        normalizedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path.Trim());
            ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(path));
            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (IsExpectedExplorerTargetPathException(ex))
        {
            return false;
        }
    }

    private static bool IsExpectedExplorerTargetPathException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or SecurityException;
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