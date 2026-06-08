using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services.Conversion;
using System;

namespace HakamiqChdTool.App.ViewModels.Virtualization;

public sealed class QueueRowData
{
    public required Guid ItemId { get; init; }
    public required string OriginalPath { get; set; }

    public string SourcePath { get; set; } = string.Empty;
    public string InputType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string DetectedPlatform { get; set; } = string.Empty;
    public string DetectionReason { get; set; } = string.Empty;
    public string RequestedAction { get; set; } = string.Empty;
    public QueueExecutionProfile ExecutionProfile { get; set; } = QueueExecutionProfile.Standard;
    public QueueIntakeSource IntakeSource { get; set; } = QueueIntakeSource.UserInitiatedAdd;

    public string CurrentState { get; set; } = TaskQueueStateCodes.Pending;
    public string FinalResult { get; set; } = TaskFinalResultCodes.None;
    public string StatusDetail { get; set; } = string.Empty;

    public double Progress { get; set; }
    public bool IsIndeterminate { get; set; }
    public bool IsProgressActive { get; set; }

    public QueueRuntimeProgressKind RuntimeProgressKind { get; set; } = QueueRuntimeProgressKind.None;
    public string RuntimeProgressPrimaryMessageKey { get; set; } = string.Empty;
    public long RuntimeProgressCurrentBytes { get; set; }
    public long RuntimeProgressTotalBytes { get; set; }
    public double RuntimeProgressBytesPerSecond { get; set; }
    public double RuntimeProgressPercent { get; set; }
    public long RuntimeProgressElapsedTicks { get; set; }
    public long RuntimeProgressEstimatedRemainingTicks { get; set; }
    public string RuntimeProgressNextStageMessageKey { get; set; } = string.Empty;
    public bool RuntimeProgressShowActivitySpinner { get; set; }

    public string OutputPath { get; set; } = string.Empty;
    public string LogPath { get; set; } = string.Empty;
    public string TempWorkingDirectory { get; set; } = string.Empty;

    public long InputBytes { get; set; }
    public long OutputBytes { get; set; }
    public long CleanupDeletedBytes { get; set; }
    public int SbiCopiedCount { get; set; }
    public int PostProcessingFailureCount { get; set; }
    public ConversionPerformanceReport? ConversionPerformanceReport { get; set; }

    public IntegrityValidationState IntegrityState { get; set; } = IntegrityValidationState.None;
    public string IntegrityMessage { get; set; } = "-";

    public bool IsNamingCompliant { get; set; } = true;
    public string SuggestedStandardName { get; set; } = string.Empty;

    public bool HasActiveQueueBinding { get; set; }
    public bool IsVisibleInCurrentOperationMode { get; set; } = true;

    public QueueIntakeAdvisory? IntakeAdvisory { get; init; }

    public QueueItemSnapshot ToSnapshot() => new()
    {
        ItemId = ItemId,
        OriginalPath = OriginalPath,
        SourcePath = string.IsNullOrWhiteSpace(SourcePath) ? OriginalPath : SourcePath,
        FileName = FileName,
        DetectedPlatform = DetectedPlatform,
            RequestedAction = RequestedAction,
    };
}
