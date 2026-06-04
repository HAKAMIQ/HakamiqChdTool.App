using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.ViewModels.Virtualization;
using System;
using System.ComponentModel;
using System.IO;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class TaskQueueItemViewModel
{
    private bool _disposed;

    public TaskQueueItemViewModel()
    {
        _queueProgressSmooth.DisplayValueChanged += OnQueueProgressSmoothDisplayChanged;
        Pipeline.PropertyChanged += OnPipelinePropertyChanged;
    }

    public TaskQueueItemViewModel(QueueRowData row) : this()
    {
        ArgumentNullException.ThrowIfNull(row);
        QueueItemId = row.ItemId;
        SeedFromRow(row);
    }

    public void ApplyQueueItemSnapshot(ChdQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        string nextMode = string.IsNullOrWhiteSpace(item.Mode) ? "Convert" : item.Mode;
        bool modeChanged = !string.Equals(_queueItemMode, nextMode, StringComparison.OrdinalIgnoreCase);
        _queueItemMode = nextMode;

        _queueSnapshotStatus = item.Status;
        ErrorMessage = item.Error ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(item.Error) &&
            (item.Status is "Failed" or "Cancelled" || string.IsNullOrWhiteSpace(StatusDetail)))
        {
            StatusDetail = item.Error;
        }

        AnimateProgressTo(item.Progress);
        OnPropertyChanged(nameof(ProcessingPhase));
        OnPropertyChanged(nameof(StatePhaseArabic));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusHeadlineIsolated));
        OnPropertyChanged(nameof(ProgressRegionPhaseIsolated));
        OnPropertyChanged(nameof(QueueRowDisplayState));
        OnPropertyChanged(nameof(QueueRowDisplayFinalResult));
        OnPropertyChanged(nameof(QueueRowDisplayDetailArabic));
        OnPropertyChanged(nameof(QueueRowDisplayPhaseIsolated));
        OnPropertyChanged(nameof(ProgressBarDisplayValue));
        OnPropertyChanged(nameof(ProgressPercentDisplay));
        OnPropertyChanged(nameof(StatusDetailUiArabic));
        OnPropertyChanged(nameof(StatusDetailUiLatin));
        OnPropertyChanged(nameof(HasStatusDetailLatin));
        OnPropertyChanged(nameof(CurrentState));
        OnPropertyChanged(nameof(FinalResult));
        OnPropertyChanged(nameof(StateIconGlyph));
        RefreshPipelinePresentation();
        NotifyUiCardLayoutProperties();

        if (modeChanged)
        {
            NotifyMetadataStripChanged();
        }
    }

    public void ApplyRowMutation(QueueRowData row)
    {
        ArgumentNullException.ThrowIfNull(row);
        ApplySinkFlowFields(row);
    }

    public void SetIntegrityPresentation(
        IntegrityValidationState state,
        string statusMessage,
        string detailTooltip)
    {
        _integrityTooltip = detailTooltip ?? string.Empty;
        _integrityStatusMessage = string.IsNullOrEmpty(statusMessage) ? "-" : statusMessage;
        _integrityState = state;

        OnPropertyChanged(nameof(IntegrityDetailTooltip));
        OnPropertyChanged(nameof(IntegrityStatusMessage));
        OnPropertyChanged(nameof(IntegrityState));
        OnPropertyChanged(nameof(IntegrityGlyph));
        OnPropertyChanged(nameof(IntegrityForegroundBrush));
        OnPropertyChanged(nameof(IsRedumpVerified));
        OnPropertyChanged(nameof(IntegrityColumnDisplayArabic));
        OnPropertyChanged(nameof(HasIntegrityColumnDetail));
        OnPropertyChanged(nameof(RedumpStatusDisplay));
        OnPropertyChanged(nameof(RedumpDetailsDisplay));
        OnPropertyChanged(nameof(HasRedumpResult));
        OnPropertyChanged(nameof(CanApplyRedumpSuggestedName));
        OnPropertyChanged(nameof(ProgressRegionPhaseIsolated));
    }

    public void ResetIntegrityPresentation()
    {
        SetIntegrityPresentation(IntegrityValidationState.None, "-", string.Empty);
    }

    public void InitializeFromPath(string path, string requestedAction, string detectedPlatform)
    {
        _queueItemMode = "Convert";

        OriginalPath = path;
        WorkingPath = path;
        OutputPath = string.Empty;
        TempWorkingDirectory = string.Empty;
        LogPath = string.Empty;

        RequestedAction = requestedAction;
        DetectedPlatform = detectedPlatform;

        DetectionReason = string.Empty;
        DisplayName = Path.GetFileNameWithoutExtension(path);
        CanonicalName = string.Empty;
        TitleId = string.Empty;
        Region = string.Empty;
        Version = string.Empty;
        VerifiedHash = string.Empty;
        IsNameVerified = false;
        NamingConfidence = NamingConfidence.Raw;

        RebuildOperationCatalog();

        CurrentState = RequestedAction switch
        {
            TaskActionCodes.PendingSelection => TaskQueueStateCodes.AwaitingOperationSelection,
            TaskActionCodes.Unsupported => TaskQueueStateCodes.Failed,
            _ => TaskQueueStateCodes.Pending
        };

        _queueSnapshotStatus = null;
        ErrorMessage = string.Empty;
        StatusDetail = RequestedAction switch
        {
            TaskActionCodes.PendingSelection => MainWindowMessages.ChooseOperationForItem,
            TaskActionCodes.Unsupported => MainWindowMessages.UnsupportedQueueFile,
            _ => MainWindowMessages.ReadyForProcessing
        };
        FinalResult = TaskFinalResultCodes.None;
        ProgressValue = 0;
        IsProgressActive = false;
        IsIndeterminate = false;
        ClearRuntimeProgressPresentation();
        InputBytes = 0;
        OutputBytes = 0;
        CleanupDeletedBytes = 0;
        SbiCopiedCount = 0;
        PostProcessingFailureCount = 0;
        UpdateResultBadgeBrushes();
        ResetIntegrityPresentation();
        RefreshPipelinePresentation();
    }

    public void ApplyCanonicalNaming(
        string? canonicalName,
        string? titleId,
        string? region,
        string? version,
        string? verifiedHash,
        bool isVerified,
        NamingConfidence confidence)
    {
        if (!string.IsNullOrWhiteSpace(canonicalName))
        {
            CanonicalName = canonicalName;
        }

        TitleId = titleId ?? string.Empty;
        Region = region ?? string.Empty;
        Version = version ?? string.Empty;
        VerifiedHash = verifiedHash ?? string.Empty;
        IsNameVerified = isVerified;
        NamingConfidence = confidence;
    }

    public void ResetProgress()
    {
        _queueProgressSmooth.SnapTo(0);
        _progressValue = 0;
        Pipeline.Progress = 0;
        ProgressText = FormatPercentStatic(0);
        IsProgressActive = false;
        IsIndeterminate = false;
        ClearRuntimeProgressPresentation();
        InputBytes = 0;
        OutputBytes = 0;
        CleanupDeletedBytes = 0;
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(ProgressBarDisplayValue));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ProgressPercentDisplay));
    }

    public void ResetForRetry(string readyStatusDetail)
    {
        _queueItemMode = "Convert";
        _queueSnapshotStatus = null;
        ErrorMessage = string.Empty;
        CurrentState = TaskQueueStateCodes.Pending;
        StatusDetail = readyStatusDetail;
        FinalResult = TaskFinalResultCodes.None;
        ResetProgress();
        UpdateResultBadgeBrushes();
        RefreshPipelinePresentation();
        NotifyMetadataStripChanged();
    }

    private void ClearRuntimeProgressPresentation()
    {
        RuntimeProgressKind = QueueRuntimeProgressKind.None;
        RuntimeProgressPrimaryMessageKey = string.Empty;
        RuntimeProgressCurrentBytes = 0;
        RuntimeProgressTotalBytes = 0;
        RuntimeProgressBytesPerSecond = 0d;
        RuntimeProgressPercent = 0d;
        RuntimeProgressElapsedTicks = 0;
        RuntimeProgressEstimatedRemainingTicks = 0;
        RuntimeProgressNextStageMessageKey = string.Empty;
        RuntimeProgressShowActivitySpinner = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queueProgressSmooth.DisplayValueChanged -= OnQueueProgressSmoothDisplayChanged;
        Pipeline.PropertyChanged -= OnPipelinePropertyChanged;
        _queueProgressSmooth.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnPipelinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(QueueItemViewModel.ProcessingState) or nameof(QueueItemViewModel.Progress))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Pipeline)));
        }
    }

    private void SeedFromRow(QueueRowData row)
    {
        ApplySinkFlowFields(row);
        SetIntegrityPresentation(row.IntegrityState, row.IntegrityMessage, string.Empty);
        IsNamingCompliant = row.IsNamingCompliant;
        SuggestedStandardName = row.SuggestedStandardName;
        IntakeAdvisory = row.IntakeAdvisory;
        ExecutionProfile = row.ExecutionProfile;
        HasActiveQueueBinding = row.HasActiveQueueBinding;
    }

    private void ApplySinkFlowFields(QueueRowData row)
    {
        OriginalPath = row.OriginalPath;
        WorkingPath = row.SourcePath;
        FileName = row.FileName;
        DetectedPlatform = row.DetectedPlatform;
        DetectionReason = row.DetectionReason;
        RequestedAction = row.RequestedAction;
        ExecutionProfile = row.ExecutionProfile;

        CurrentState = row.CurrentState;
        FinalResult = row.FinalResult;
        StatusDetail = row.StatusDetail;

        IsIndeterminate = row.IsIndeterminate;
        IsProgressActive = row.IsProgressActive;
        ProgressValue = row.Progress;
        RuntimeProgressKind = row.RuntimeProgressKind;
        RuntimeProgressPrimaryMessageKey = row.RuntimeProgressPrimaryMessageKey;
        RuntimeProgressCurrentBytes = row.RuntimeProgressCurrentBytes;
        RuntimeProgressTotalBytes = row.RuntimeProgressTotalBytes;
        RuntimeProgressBytesPerSecond = row.RuntimeProgressBytesPerSecond;
        RuntimeProgressPercent = row.RuntimeProgressPercent;
        RuntimeProgressElapsedTicks = row.RuntimeProgressElapsedTicks;
        RuntimeProgressEstimatedRemainingTicks = row.RuntimeProgressEstimatedRemainingTicks;
        RuntimeProgressNextStageMessageKey = row.RuntimeProgressNextStageMessageKey;
        RuntimeProgressShowActivitySpinner = row.RuntimeProgressShowActivitySpinner;

        OutputPath = row.OutputPath;
        LogPath = row.LogPath;
        TempWorkingDirectory = row.TempWorkingDirectory;

        InputBytes = row.InputBytes;
        OutputBytes = row.OutputBytes;
        CleanupDeletedBytes = row.CleanupDeletedBytes;
        SbiCopiedCount = row.SbiCopiedCount;
        PostProcessingFailureCount = row.PostProcessingFailureCount;
    }
}