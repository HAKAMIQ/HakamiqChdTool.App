using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace HakamiqChdTool.App.Coordination;

public interface IAppSessionUiPort
{
    Window Owner { get; }

    bool IncludeSubfolders { get; }

    bool IsQueueInteractionLocked { get; }

    bool CanUsePremiumFeature(PremiumFeature feature);

    bool RequirePremiumFeature(PremiumFeature feature);

    QueueExecutionProfile GetSelectedInputExecutionProfile();

    bool IsSelectedScanMode();

    string GetSelectedInputDialogTitle(QueueExecutionProfile profile);

    string GetSelectedInputDialogFilter(QueueExecutionProfile profile);

    string GetSelectedFolderDialogDescription();

    void SetFooterStatus(string message);

    void UpdateUiState();

    void ShowError(string title, string body);

    void CaptureThemeIntoSettings();

    void PersistSettings();

    bool ConfirmStorageAdvisorBeforeProcessing(
        IReadOnlyList<TaskQueueItemViewModel> items,
        bool processedSelectionOnly);

    void ResetRunSummary();

    void BuildRunSummary(
        bool showCompletionDialog,
        bool wasCancelled,
        bool processedSelectionOnly,
        PostConversionArtifactResult sessionArtifacts);

    Task<PostConversionArtifactResult> GenerateM3uPlaylistsForCompletedChdOutputsAsync(IReadOnlyList<string> outputPaths);

    Task ApplyAutoStandardizedNameIfEnabledAsync(TaskQueueItemViewModel item);

    void SetHasActiveQueueBindingAndSync(TaskQueueItemViewModel item, bool value);

    bool IsRowQueuedForProcessing(Guid itemId);

    Task IngestPathsAsync(
        IEnumerable<string> paths,
        QueueIngestKind inputKind,
        QueueIntakeSource intakeSource);

    Task<IReadOnlyList<Guid>> IngestQuickPathsAsync(
        IEnumerable<string> paths,
        QueueIngestKind inputKind,
        QueueExecutionProfile executionProfile,
        QueueIntakeSource intakeSource);

    TaskQueueItemViewModel? TryMaterializeOrGet(Guid id);

    IReadOnlyList<Guid> EnumerateQueuedItemIds();
}