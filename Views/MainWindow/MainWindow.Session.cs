using HakamiqChdTool.App.Coordination;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Features;
using HakamiqChdTool.App.Services.M3u;
using HakamiqChdTool.App.Services.StorageAdvisor;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using HakamiqChdTool.App.Views;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private readonly StorageAdvisorService _storageAdvisorService = new();

    private sealed class SessionAdapter : IMainWindowSession
    {
        private readonly MainWindow _w;

        public SessionAdapter(MainWindow window)
        {
            ArgumentNullException.ThrowIfNull(window);
            _w = window;
        }

        public Window Owner => _w;

        public Dispatcher UiDispatcher => _w.Dispatcher;

        public bool IsQueueInteractionLocked => _w.IsQueueInteractionLocked;

        public bool IsProcessing => _w._coordinator?.IsProcessing ?? false;

        public bool CancellationRequested => _w._coordinator?.CancellationRequested ?? false;

        public bool IncludeSubfolders => _w._settings.IncludeSubfolders;

        public AppSettings GetSettings() => _w._settings;

        public IAppFeatureService AppFeatures => _w._appFeatureService;

        public bool RequireAppFeature(AppFeature feature) =>
            _w.RequireAppFeature(feature);

        public QueueRowStore QueueRows => _w._queueRowStore;

        public (bool IsCompliant, string SuggestedStandardName) AnalyzeNamingForPath(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return (true, string.Empty);
            }

            AppSettings settings = _w._settings;
            if (!settings.EnableDeepIntegrityCheck
                || !_w._appFeatureService.IsEnabled(AppFeature.StandardNamingSuggestion))
            {
                return (true, string.Empty);
            }

            RedumpSqliteManager database = RedumpSqliteManager.Default;
            database.EnsureInitialized();

            if (!database.HasAnyRows())
            {
                return (true, string.Empty);
            }

            (bool compliant, string suggested) = NamingCorrectionEngine.Analyze(sourcePath);
            return (compliant, suggested);
        }

        public void SetFooterStatus(string messageEnglish) =>
            _w.SetFooterStatus(messageEnglish);

        public void SetFooterIntakeProgress(
            string stageText,
            int scannedCount,
            int totalCount,
            int acceptedCount,
            bool hasKnownTotal) =>
            _w.SetFooterIntakeProgress(
                stageText,
                scannedCount,
                totalCount,
                acceptedCount,
                hasKnownTotal);

        public void UpdateUiState() =>
            _w.UpdateUiState();

        public void RequestSelectFirstQueueRowIfNone()
        {
            if (_w._queueView.Count <= 0 || _w.TasksDataGrid.SelectedItem is not null)
            {
                return;
            }

            _w.TasksDataGrid.SelectedIndex = 0;
            _w._viewModel.SelectedTask = _w.TasksDataGrid.SelectedItem as TaskQueueItemViewModel;
        }

        public void OnQueueClearedUi()
        {
            _w._executionLogLines.Clear();
            _w._loggedExecutionSignatures.Clear();
            SetFooterStatus(MainWindowMessages.QueueClearedFooter);
        }

        public void ShowError(string titleResourceKey, string messageBody)
        {
            _w.ShowNoticeDialog(titleResourceKey, messageBody);
        }

        public void OpenExplorerForSelectedQueueItem(TaskQueueItemViewModel? item) =>
            _w.OpenFolderForQueueItem(item);

        public Task OpenOperationLogForQueueItemAsync(TaskQueueItemViewModel? item) =>
            _w.OpenOperationLogForQueueItemAsync(item);

        public bool CanOpenOperationLogTarget(TaskQueueItemViewModel? item) =>
            _w.TryGetQueueItemOperationLogTarget(item, out _);

        public Task RunRedumpIntegrityForSelectedQueueItemAsync(TaskQueueItemViewModel? item) =>
            _w.RunRedumpIntegrityForSelectedQueueItemAsync(item);

        public bool CanRunRedumpIntegrityForSelectedQueueItem(TaskQueueItemViewModel? item) =>
            _w.CanRunRedumpIntegrityForSelectedQueueItem(item);

        public Task RunRedumpIntegrityForAllQueueItemsAsync() =>
            _w.RunRedumpIntegrityForAllQueueItemsAsync();

        public bool CanRunRedumpIntegrityForAnyQueueItem() =>
            _w.CanRunRedumpIntegrityForAnyQueueItem();

        public void ShowRedumpDetails(TaskQueueItemViewModel? item) =>
            _w.ShowRedumpDetails(item);

        public Task ApplyRedumpSuggestedNameAsync(TaskQueueItemViewModel? item) =>
            _w.ApplyRedumpSuggestedNameAsync(item);

        public bool CanApplyRedumpSuggestedName(TaskQueueItemViewModel? item) =>
            _w.CanApplyRedumpSuggestedName(item);

        public bool CanOpenExplorerTarget(TaskQueueItemViewModel? item) =>
            _w.TryGetQueueItemExplorerTarget(item, out _);

        public void RemoveQueueItem(TaskQueueItemViewModel? item) =>
            _w.RemoveQueueItemFromSession(item);

        public void RetryQueueItem(TaskQueueItemViewModel? item) =>
            _w.RetryQueueItemFromSession(item);

        public void CancelQueueJob(TaskQueueItemViewModel? item) =>
            _w.CancelQueueJobFromSession(item);

        public void OpenAdvancedOptions() =>
            _w.OpenAdvancedOptionsDialog();

        public void OpenAbout() =>
            _w.OpenAboutDialog();

    }

    private sealed class UiPortAdapter : IAppSessionUiPort
    {
        private readonly MainWindow _w;

        public UiPortAdapter(MainWindow window)
        {
            ArgumentNullException.ThrowIfNull(window);
            _w = window;
        }

        public Window Owner => _w;

        public bool IncludeSubfolders => _w._settings.IncludeSubfolders;

        public bool IsQueueInteractionLocked => _w.IsQueueInteractionLocked;

        public bool CanUseAppFeature(AppFeature feature) =>
            _w._appFeatureService.IsEnabled(feature);

        public bool RequireAppFeature(AppFeature feature) =>
            _w.RequireAppFeature(feature);

        public QueueExecutionProfile GetSelectedInputExecutionProfile() =>
            _w.GetSelectedInputExecutionProfileFromUi();

        public bool IsSelectedScanMode() =>
            _w.IsSelectedScanModeFromUi();

        public string GetSelectedInputDialogTitle(QueueExecutionProfile profile) =>
            _w.GetSelectedInputDialogTitleFromUi(profile);

        public string GetSelectedInputDialogFilter(QueueExecutionProfile profile) =>
            _w.GetSelectedInputDialogFilterFromUi(profile);

        public string GetSelectedFolderDialogDescription() =>
            _w.GetSelectedFolderDialogDescriptionFromUi();

        public void SetFooterStatus(string messageEnglish) =>
            _w.SetFooterStatus(messageEnglish);

        public void UpdateUiState() =>
            _w.UpdateUiState();

        public void ShowError(string title, string body)
        {
            _w.ShowNoticeDialog(title, body);
        }

        public void CaptureThemeIntoSettings() =>
            _w.CaptureThemeIntoSettings();

        public void PersistSettings() =>
            _w.PersistSettings();

        public bool ConfirmStorageAdvisorBeforeProcessing(
            IReadOnlyList<TaskQueueItemViewModel> items,
            bool processedSelectionOnly)
        {
            ArgumentNullException.ThrowIfNull(items);

            if (items.Count == 0 || _w._settings.SuppressStorageAdvisorDialog)
            {
                return true;
            }

            return _w.ConfirmStorageAdvisorBeforeProcessing(items, processedSelectionOnly);
        }

        public void ResetRunSummary() =>
            _w.ResetRunSummary();

        public void BuildRunSummary(
            bool showCompletionDialog,
            bool wasCancelled,
            bool processedSelectionOnly,
            PostConversionArtifactResult sessionArtifacts) =>
            _w.BuildRunSummary(showCompletionDialog, wasCancelled, processedSelectionOnly, sessionArtifacts);

        public Task<PostConversionArtifactResult> GenerateM3uPlaylistsForCompletedChdOutputsAsync(IReadOnlyList<string> outputPaths) =>
            _w.GenerateM3uPlaylistsForCompletedChdOutputsAsync(outputPaths);

        public Task ApplyAutoStandardizedNameIfEnabledAsync(TaskQueueItemViewModel item) =>
            _w.ApplyAutoStandardizedNameIfEnabled(item);

        public void SetHasActiveQueueBindingAndSync(TaskQueueItemViewModel item, bool value) =>
            _w.SetHasActiveQueueBindingAndSync(item, value);

        public bool IsRowQueuedForProcessing(Guid itemId) =>
            _w._queueRowStore.GetById(itemId) is { } row
            && _w.IsRowQueuedForProcessingForSelectedMode(row);

        public Task IngestPathsAsync(
            IEnumerable<string> paths,
            QueueIngestKind inputKind,
            QueueIntakeSource intakeSource) =>
            _w._viewModel.IngestPathsAsync(paths, inputKind, intakeSource);

        public Task<IReadOnlyList<Guid>> IngestQuickPathsAsync(
            IEnumerable<string> paths,
            QueueIngestKind inputKind,
            QueueExecutionProfile executionProfile,
            QueueIntakeSource intakeSource) =>
            _w._viewModel.IngestQuickPathsAsync(paths, inputKind, executionProfile, intakeSource);

        public TaskQueueItemViewModel? TryMaterializeOrGet(Guid id)
        {
            TaskQueueItemViewModel? vm = _w._viewport.TryGetMaterialized(id);
            if (vm is not null)
            {
                return vm;
            }

            int index = _w._queueRowStore.IndexOf(id);
            return index >= 0 ? _w._viewport.Realize(index) : null;
        }

        public IReadOnlyList<Guid> EnumerateQueuedItemIds()
        {
            var ids = new List<Guid>();

            foreach (QueueRowData row in _w._queueRowStore.Rows)
            {
                if (_w.IsRowQueuedForProcessingForSelectedMode(row))
                {
                    ids.Add(row.ItemId);
                }
            }

            return ids;
        }
    }

    private bool ConfirmStorageAdvisorBeforeProcessing(
        IReadOnlyList<TaskQueueItemViewModel> items,
        bool processedSelectionOnly)
    {
        ArgumentNullException.ThrowIfNull(items);
        _ = processedSelectionOnly;

        if (items.Count == 0 || _settings.SuppressStorageAdvisorDialog)
        {
            return true;
        }

        try
        {
            foreach (TaskQueueItemViewModel item in items)
            {
                if (!TryBuildStorageAdvisorRequest(item, out StorageAdvisorRequest? request)
                    || request is null)
                {
                    continue;
                }

                StorageAdvisorResult result = _storageAdvisorService.Analyze(
                    request,
                    _settings.SuppressStorageAdvisorDialog);

                if (!result.ShouldShowDialog)
                {
                    continue;
                }

                StorageAdvisorDialogResult dialogResult = ShowStorageAdvisorDialog(result);
                return HandleStorageAdvisorDialogResult(dialogResult);
            }

            return true;
        }
        catch (Exception ex) when (IsExpectedStorageAdvisorFailure(ex))
        {
            Log.Warning(ex, "Storage Advisor pre-processing check failed. Processing will continue.");
            return true;
        }
    }

    private bool TryBuildStorageAdvisorRequest(
        TaskQueueItemViewModel item,
        out StorageAdvisorRequest? request)
    {
        ArgumentNullException.ThrowIfNull(item);

        request = null;

        if (string.IsNullOrWhiteSpace(item.SourcePath))
        {
            return false;
        }

        string sourcePath = item.SourcePath.Trim();
        StorageAdvisorOperationKind operationKind = ResolveStorageAdvisorOperationKind(item);

        if (operationKind is StorageAdvisorOperationKind.Unknown or StorageAdvisorOperationKind.Verification)
        {
            return false;
        }

        string outputDirectoryPath = ResolveStorageAdvisorOutputDirectory(sourcePath);
        string pendingWorkspaceRoot = ResolveStorageAdvisorPendingWorkspaceRoot(sourcePath, operationKind);
        bool usesCustomPendingWorkspace = _settings.UseCustomPendingWorkspace
            && !string.IsNullOrWhiteSpace(_settings.PendingWorkspaceCustomRoot);

        request = new StorageAdvisorRequest(
            operationKind,
            sourcePath,
            outputDirectoryPath,
            pendingWorkspaceRoot,
            usesCustomPendingWorkspace);

        return true;
    }

    private static StorageAdvisorOperationKind ResolveStorageAdvisorOperationKind(TaskQueueItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        string mode = QueueOperationModeResolver.QueueModeFromRequestedAction(item.RequestedAction);
        if (string.Equals(mode, "Extract", StringComparison.OrdinalIgnoreCase))
        {
            return StorageAdvisorOperationKind.Extraction;
        }

        if (!string.Equals(mode, "Convert", StringComparison.OrdinalIgnoreCase))
        {
            return StorageAdvisorOperationKind.Unknown;
        }

        return IsBinCueRescueCandidate(item.SourcePath)
            ? StorageAdvisorOperationKind.BinCueRescue
            : StorageAdvisorOperationKind.StandardConversion;
    }

    private string ResolveStorageAdvisorOutputDirectory(string sourcePath)
    {
        if (_settings.UseCustomOutputRoot && !string.IsNullOrWhiteSpace(_settings.CustomOutputRoot))
        {
            return _settings.CustomOutputRoot.Trim();
        }

        return ResolveExistingOrParentDirectory(sourcePath);
    }

    private string ResolveStorageAdvisorPendingWorkspaceRoot(
        string sourcePath,
        StorageAdvisorOperationKind operationKind)
    {
        if (_settings.UseCustomPendingWorkspace && !string.IsNullOrWhiteSpace(_settings.PendingWorkspaceCustomRoot))
        {
            return _settings.PendingWorkspaceCustomRoot.Trim();
        }

        if (operationKind == StorageAdvisorOperationKind.BinCueRescue)
        {
            return ResolveExistingOrParentDirectory(sourcePath);
        }

        string outputDirectory = ResolveStorageAdvisorOutputDirectory(sourcePath);
        return PendingWorkspacePathPolicy.ResolvePendingWorkspaceRoot(outputDirectory, _settings);
    }

    private bool HandleStorageAdvisorDialogResult(StorageAdvisorDialogResult result)
    {
        if (result == StorageAdvisorDialogResult.ContinueRecommended)
        {
            return true;
        }

        if (result == StorageAdvisorDialogResult.OpenAdvancedOptions)
        {
            OpenAdvancedOptionsDialog();
            return false;
        }

        return false;
    }

    private static bool IsBinCueRescueCandidate(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetExtension(sourcePath.Trim()),
                ".bin",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsExpectedStorageAdvisorFailure(ex))
        {
            return false;
        }
    }

    private static string ResolveExistingOrParentDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            string fullPath = Path.GetFullPath(path.Trim());

            if (Directory.Exists(fullPath))
            {
                return fullPath;
            }

            return Path.GetDirectoryName(fullPath) ?? string.Empty;
        }
        catch (Exception ex) when (IsExpectedStorageAdvisorFailure(ex))
        {
            return string.Empty;
        }
    }

    private static bool IsExpectedStorageAdvisorFailure(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or InvalidOperationException
            or System.Security.SecurityException;
    }

    private Task<PostConversionArtifactResult> GenerateM3uPlaylistsForCompletedChdOutputsAsync(IReadOnlyList<string> outputPaths)
    {
        ArgumentNullException.ThrowIfNull(outputPaths);

        if (!_settings.EnableAutoM3uGeneration || outputPaths.Count == 0)
        {
            return Task.FromResult(PostConversionArtifactResult.Empty);
        }

        string[] completedChdOutputs =
        [
            .. outputPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Where(path => string.Equals(Path.GetExtension(path), ".chd", StringComparison.OrdinalIgnoreCase)
                && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
        ];

        if (completedChdOutputs.Length == 0)
        {
            Log.Information(
                "M3U playlist generation skipped because no completed CHD workflow outputs were available. CompletedWorkflowOutputs={CompletedWorkflowOutputs}; CompletedChdOutputs={CompletedChdOutputs}",
                outputPaths.Count,
                completedChdOutputs.Length);

            return Task.FromResult(PostConversionArtifactResult.Empty);
        }

        try
        {
            PostConversionArtifactResult result = _postConversionArtifacts.GenerateM3uPlaylists(
                completedChdOutputs,
                _settings.OverwriteExistingM3uPlaylists);

            if (result.M3uGeneratedCount > 0)
            {
                SetFooterStatus(ArabicUi.Format(MainWindowMessages.Fmt_M3uGeneratedFooter, result.M3uGeneratedCount));
            }

            if (result.FailedArtifactCount > 0)
            {
                Log.Warning(
                    "M3U playlist generation completed with failures. Generated={GeneratedCount}; Failed={FailedCount}; SkippedExisting={SkippedExistingCount}",
                    result.M3uGeneratedCount,
                    result.FailedArtifactCount,
                    result.M3uSkippedExistingCount);
            }

            return Task.FromResult(result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            Log.Warning(ex, "M3U playlist generation failed after session completion.");
            return Task.FromResult(PostConversionArtifactResult.WithFailure(
                "M3U",
                "LocPostProcessing_M3uGenerationFailed"));
        }
    }

    private static string ResolveDialogText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return ArabicUi.ResolveDisplayString(value.Trim());
    }
}
