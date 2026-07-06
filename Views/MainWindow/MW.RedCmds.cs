using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Ui.Queue;
using HakamiqChdTool.App.Ui.Shell;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Features;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private const string RedumpScanCancelledFooterKey = "LocRedump_ScanCancelledFooter";
    private const string RedumpSelectedScanFailedFooterKey = "LocRedump_SelectedScanFailedFooter";
    private const string RedumpScanTitleKey = "LocRedump_ScanTitle";
    private const string RedumpAllScanStartedFooterKey = "LocRedump_AllScanStartedFooter";
    private const string RedumpItemScanFailedContinueFooterFormatKey = "LocRedump_ItemScanFailedContinueFooterFormat";
    private const string RedumpAllScanStoppedFooterFormatKey = "LocRedump_AllScanStoppedFooterFormat";
    private const string RedumpAllScanCompletedWithFailuresFooterFormatKey = "LocRedump_AllScanCompletedWithFailuresFooterFormat";
    private const string RedumpAllScanCompletedFooterFormatKey = "LocRedump_AllScanCompletedFooterFormat";
    private const string CommonCancelKey = "LocCommon_Cancel";

    private sealed record RedumpScanCandidate(
        Guid ItemId,
        string Path);

    private async Task RunIntegrityContextAsync(TaskQueueItemViewModel? item)
    {
        item ??= TasksDataGrid.SelectedItem as TaskQueueItemViewModel;
        if (IsQueueInteractionLocked || item is null)
        {
            return;
        }

        if (!RequireAppFeature(AppFeature.RedumpDeepIntegrity))
        {
            return;
        }

        string? path = ResolveQueueItemProbePath(item);
        if (string.IsNullOrWhiteSpace(path))
        {
            SetFooterStatus(MainWindowMessages.IntegrityNoDiskFileFooter);
            ShowRedumpNotice(
                MainWindowMessages.IntegrityNoDiskFileTitle,
                ArabicUi.Get(MainWindowMessages.IntegrityNoDiskFileBody));
            return;
        }

        await RunDeepIntegrityValidationAsync(item, path).ConfigureAwait(true);
    }

    public bool CanRunRedumpIntegrityForSelectedQueueItem(TaskQueueItemViewModel? item)
    {
        item ??= TasksDataGrid.SelectedItem as TaskQueueItemViewModel;

        if (item is null ||
            IsQueueInteractionLocked ||
            !_settings.EnableDeepIntegrityCheck ||
            !_appFeatureService.IsEnabled(AppFeature.RedumpDeepIntegrity))
        {
            return false;
        }

        string path = ResolveRedumpProbePath(item);
        return !string.IsNullOrWhiteSpace(path) && IsRedumpProbePathAvailable(path);
    }

    public async Task RunRedumpIntegrityForSelectedQueueItemAsync(TaskQueueItemViewModel? item)
    {
        item ??= TasksDataGrid.SelectedItem as TaskQueueItemViewModel;

        if (item is null || IsQueueInteractionLocked || !_settings.EnableDeepIntegrityCheck)
        {
            return;
        }

        if (!RequireAppFeature(AppFeature.RedumpDeepIntegrity))
        {
            return;
        }

        string path = ResolveRedumpProbePath(item);
        if (string.IsNullOrWhiteSpace(path) || !IsRedumpProbePathAvailable(path))
        {
            SetFooterStatus(MainWindowMessages.IntegrityNoDiskFileFooter);
            return;
        }

        try
        {
            await RunDeepIntegrityValidationAsync(item, path).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            SetFooterStatus(Ui(RedumpScanCancelledFooterKey));
        }
        catch (Exception ex) when (IsExpectedRedumpRuntimeException(ex))
        {
            SetFooterStatus(Ui(RedumpSelectedScanFailedFooterKey));
            ShowRedumpNotice(
                RedumpScanTitleKey,
                RuntimeDiagnosticFormatter.SummarizeException(ex));
        }
    }

    public bool CanRunRedumpIntegrityForAnyQueueItem()
    {
        return !IsQueueInteractionLocked &&
            _settings.EnableDeepIntegrityCheck &&
            _appFeatureService.IsEnabled(AppFeature.RedumpDeepIntegrity) &&
            _queueView.Count > 0;
    }

    public async Task RunRedumpIntegrityForAllQueueItemsAsync()
    {
        if (IsQueueInteractionLocked || !_settings.EnableDeepIntegrityCheck)
        {
            return;
        }

        if (!RequireAppFeature(AppFeature.RedumpDeepIntegrity))
        {
            return;
        }

        Guid[] itemIds = _queueRowStore.Rows
            .Select(row => row.ItemId)
            .ToArray();

        if (itemIds.Length == 0)
        {
            SetFooterStatus(MainWindowMessages.IntegrityNoDiskFileFooter);
            return;
        }

        RedumpScanCandidate[] candidates = BuildRedumpScanCandidates(itemIds);
        if (candidates.Length == 0)
        {
            SetFooterStatus(MainWindowMessages.IntegrityNoDiskFileFooter);
            return;
        }

        int eligibleCount = candidates.Length;
        int scannedCount = 0;
        int failedCount = 0;

        SetFooterStatus(Ui(RedumpAllScanStartedFooterKey));

        try
        {
            foreach (RedumpScanCandidate candidate in candidates)
            {
                if (_windowLifetimeCts.IsCancellationRequested || IsQueueInteractionLocked)
                {
                    break;
                }

                TaskQueueItemViewModel? item = _viewport.TryGetMaterialized(candidate.ItemId);
                bool realizedForScan = false;

                if (item is null)
                {
                    int rowIndex = _queueRowStore.IndexOf(candidate.ItemId);
                    if (rowIndex >= 0)
                    {
                        item = _viewport.Realize(rowIndex);
                        realizedForScan = item is not null;
                    }
                }

                try
                {
                    if (item is null)
                    {
                        continue;
                    }

                    try
                    {
                        SetFooterStatus(UiFormat("LocRedumpV2_AllProgressFormat", scannedCount + 1, eligibleCount, Path.GetFileName(candidate.Path)));

                        await RunDeepIntegrityValidationAsync(
                                item,
                                candidate.Path)
                            .ConfigureAwait(true);

                        scannedCount++;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex) when (IsExpectedRedumpRuntimeException(ex))
                    {
                        failedCount++;
                        SetFooterStatus(UiFormat(RedumpItemScanFailedContinueFooterFormatKey, failedCount));
                    }
                }
                finally
                {
                    if (realizedForScan)
                    {
                        _viewport.ReleaseById(candidate.ItemId);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            SetFooterStatus(Ui(RedumpScanCancelledFooterKey));
            return;
        }

        if (eligibleCount == 0)
        {
            SetFooterStatus(MainWindowMessages.IntegrityNoDiskFileFooter);
            return;
        }

        if (_windowLifetimeCts.IsCancellationRequested || IsQueueInteractionLocked)
        {
            SetFooterStatus(UiFormat(RedumpAllScanStoppedFooterFormatKey, scannedCount));
            return;
        }

        if (failedCount > 0)
        {
            SetFooterStatus(UiFormat(RedumpAllScanCompletedWithFailuresFooterFormatKey, scannedCount, failedCount));
            return;
        }

        SetFooterStatus(UiFormat(RedumpAllScanCompletedFooterFormatKey, scannedCount));
    }

    private RedumpScanCandidate[] BuildRedumpScanCandidates(IReadOnlyList<Guid> itemIds)
    {
        var candidates = new List<RedumpScanCandidate>(itemIds.Count);

        foreach (Guid itemId in itemIds)
        {
            TaskQueueItemViewModel? item = _viewport.TryGetMaterialized(itemId);
            bool realizedForScan = false;

            if (item is null)
            {
                int rowIndex = _queueRowStore.IndexOf(itemId);
                if (rowIndex >= 0)
                {
                    item = _viewport.Realize(rowIndex);
                    realizedForScan = item is not null;
                }
            }

            try
            {
                if (item is null)
                {
                    continue;
                }

                string path = ResolveRedumpProbePath(item);
                if (string.IsNullOrWhiteSpace(path) || !IsRedumpProbePathAvailable(path))
                {
                    continue;
                }

                candidates.Add(new RedumpScanCandidate(itemId, path));
            }
            finally
            {
                if (realizedForScan)
                {
                    _viewport.ReleaseById(itemId);
                }
            }
        }

        return [.. candidates];
    }

    private static bool IsRedumpProbePathAvailable(string path) =>
        File.Exists(path) || Directory.Exists(path);

    private void ShowRedumpNotice(string titleKey, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var dialog = new RedumpNoticeDialog(
            Ui(titleKey),
            message)
        {
            Owner = this
        };

        dialog.ShowDialog();
    }

    private static string ResolveRedumpProbePath(TaskQueueItemViewModel item)
    {
        if (!string.IsNullOrWhiteSpace(item.SourcePath))
        {
            return item.SourcePath;
        }

        if (!string.IsNullOrWhiteSpace(item.OriginalPath))
        {
            return item.OriginalPath;
        }

        return string.Empty;
    }

    private static bool IsExpectedRedumpRuntimeException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException;
    }

    private static string Ui(string key)
    {
        return ArabicUi.Get(key);
    }

    private static string UiFormat(string key, params object[] args)
    {
        return ArabicUi.Format(key, args);
    }
}
