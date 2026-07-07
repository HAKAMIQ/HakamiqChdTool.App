using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Features;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
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

        return TryResolveRedumpProbePath(item, out _);
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

        if (!TryResolveRedumpProbePath(item, out string path))
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
                        SetFooterStatus(UiFormat(
                            "LocRedumpV2_AllProgressFormat",
                            SaturatingAdd(scannedCount, 1),
                            eligibleCount,
                            GetSafeRedumpProbeDisplayName(candidate.Path)));

                        await RunDeepIntegrityValidationAsync(
                                item,
                                candidate.Path)
                            .ConfigureAwait(true);

                        scannedCount = SaturatingAdd(scannedCount, 1);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex) when (IsExpectedRedumpRuntimeException(ex))
                    {
                        failedCount = SaturatingAdd(failedCount, 1);
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
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

                if (!TryResolveRedumpProbePath(item, out string path))
                {
                    continue;
                }

                if (!seenPaths.Add(path))
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
        return TryResolveRedumpProbePath(item, out string path)
            ? path
            : string.Empty;
    }

    private static bool TryResolveRedumpProbePath(
        TaskQueueItemViewModel item,
        out string path)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (TryNormalizeRedumpProbePath(item.SourcePath, out path))
        {
            return true;
        }

        if (TryNormalizeRedumpProbePath(item.OriginalPath, out path))
        {
            return true;
        }

        path = string.Empty;
        return false;
    }

    private static bool IsRedumpProbePathAvailable(string path)
    {
        return TryNormalizeRedumpProbePath(path, out _);
    }

    private static bool TryNormalizeRedumpProbePath(
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

            FileAttributes attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            bool isFile = (attributes & FileAttributes.Directory) == 0;
            bool isDirectory = (attributes & FileAttributes.Directory) != 0;

            if (!isFile && !isDirectory)
            {
                return false;
            }

            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (IsExpectedRedumpPathException(ex))
        {
            return false;
        }
    }

    private static string GetSafeRedumpProbeDisplayName(string path)
    {
        try
        {
            string fileName = Path.GetFileName(path);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }

            string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            fileName = Path.GetFileName(trimmed);

            return string.IsNullOrWhiteSpace(fileName)
                ? path
                : fileName;
        }
        catch (Exception ex) when (IsExpectedRedumpPathException(ex))
        {
            return path;
        }
    }

    private static bool IsExpectedRedumpPathException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or SecurityException;
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