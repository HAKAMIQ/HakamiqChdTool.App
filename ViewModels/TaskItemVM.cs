using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.ViewModels.Virtualization;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using MediaBrush = System.Windows.Media.Brush;

namespace HakamiqChdTool.App.ViewModels;

public sealed class QueueOperationChoice
{
    public string Code { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;

    public override string ToString() => Label;
}

public sealed partial class TaskQueueItemViewModel : INotifyPropertyChanged, IDisposable
{
    private QueueIntakeAdvisory? _intakeAdvisory;

    private string _originalPath = string.Empty;
    private string _workingPath = string.Empty;
    private string _outputPath = string.Empty;
    private string _tempWorkingDirectory = string.Empty;
    private string _logPath = string.Empty;

    private string _fileName = string.Empty;
    private string _inputType = string.Empty;
    private string _fileSizeDisplay = "-";
    private string _sourceDirectoryDisplay = "-";
    private string _shortSourcePathDisplay = "-";

    private string _detectedPlatform = string.Empty;
    private string _detectionReason = string.Empty;

    private string _requestedAction = string.Empty;
    private string _currentState = TaskQueueStateCodes.Pending;
    private string _statusDetail = string.Empty;
    private string _finalResult = TaskFinalResultCodes.None;
    private bool _isNamingCompliant = true;
    private string _suggestedStandardName = string.Empty;
    private string _canonicalName = string.Empty;
    private string _displayName = string.Empty;
    private string _titleId = string.Empty;
    private string _region = string.Empty;
    private string _version = string.Empty;
    private string _verifiedHash = string.Empty;
    private bool _isNameVerified;
    private NamingConfidence _namingConfidence = NamingConfidence.Raw;
    private string _deepHashCachePath = string.Empty;
    private DeepHashAnalysisResult? _deepHashCachedResult;
    private string? _queueSnapshotStatus;
    private string _errorMessage = string.Empty;

    private string[] _supportedOperationCodes = Array.Empty<string>();
    private QueueOperationChoice[] _operationChoices = Array.Empty<QueueOperationChoice>();
    private string _queueItemMode = "Convert";

    private double _progressValue;
    private string _progressText = FormatPercentStatic(0);
    private readonly QueueProgressSmoothDriver _queueProgressSmooth = new();
    private MediaBrush _resultBadgeBrush = CreateBrush(243, 244, 246);
    private MediaBrush _resultBadgeForegroundBrush = CreateBrush(55, 65, 81);
    private bool _isProgressActive;
    private bool _isIndeterminate;

    private IntegrityValidationState _integrityState = IntegrityValidationState.None;
    private string _integrityStatusMessage = "-";
    private string _integrityTooltip = string.Empty;
    private long _inputBytes;
    private long _outputBytes;
    private long _cleanupDeletedBytes;
    private int _sbiCopiedCount;
    private int _postProcessingFailureCount;

    private bool _hasActiveQueueBinding;

    public Guid QueueItemId { get; private set; } = Guid.NewGuid();

    public QueueItemSnapshot CreateSnapshot() => new()
    {
        ItemId = QueueItemId,
        OriginalPath = OriginalPath,
        SourcePath = SourcePath,
        FileName = FileName,
        DetectedPlatform = DetectedPlatform,
        RequestedAction = RequestedAction,
    };

    public QueueItemViewModel Pipeline { get; } = new();

    public bool HasActiveQueueBinding
    {
        get => _hasActiveQueueBinding;
        set
        {
            if (SetField(ref _hasActiveQueueBinding, value))
            {
                OnPropertyChanged(nameof(HasActiveQueueBinding));
            }
        }
    }

    public string OriginalPath
    {
        get => _originalPath;
        set
        {
            if (!SetField(ref _originalPath, value))
            {
                return;
            }

            RefreshSourceMetadata();
            OnPropertyChanged(nameof(SourcePath));
            OnPropertyChanged(nameof(SourceFilePath));
            OnPropertyChanged(nameof(SourceExtensionDisplay));
            OnPropertyChanged(nameof(QueueTitleDisplay));
        }
    }

    public string WorkingPath
    {
        get => _workingPath;
        set
        {
            if (SetField(ref _workingPath, value))
            {
                RefreshSourceMetadata();
                OnPropertyChanged(nameof(SourcePath));
                OnPropertyChanged(nameof(IsDirectChd));
                OnPropertyChanged(nameof(FullPathTooltip));
                OnPropertyChanged(nameof(SourceFilePath));
                OnPropertyChanged(nameof(SourceExtensionDisplay));
                OnPropertyChanged(nameof(QueueTitleDisplay));
            }
        }
    }

    public string SourcePath
    {
        get => string.IsNullOrWhiteSpace(WorkingPath) ? OriginalPath : WorkingPath;
        set
        {
            if (string.IsNullOrWhiteSpace(OriginalPath))
            {
                OriginalPath = value;
            }

            WorkingPath = value;
        }
    }

    public bool IsDirectChd => string.Equals(Path.GetExtension(SourcePath), ".chd", StringComparison.OrdinalIgnoreCase);

    public string OutputPath
    {
        get => _outputPath;
        set
        {
            if (SetField(ref _outputPath, value))
            {
                OnPropertyChanged(nameof(OutputDisplay));
                OnPropertyChanged(nameof(OutputPathColumnDisplay));
                OnPropertyChanged(nameof(OutputPathColumnTooltip));
                OnPropertyChanged(nameof(HasOutputPathColumnTooltip));
            }
        }
    }

    public string OutputDisplay => string.IsNullOrWhiteSpace(OutputPath) ? "-" : Path.GetFileName(OutputPath);

    public string OutputPathColumnDisplay =>
        string.IsNullOrWhiteSpace(OutputPath)
            ? string.Empty
            : "\u2066" + Path.GetFileName(OutputPath.Trim()) + "\u2069";

    public string OutputPathColumnTooltip =>
        string.IsNullOrWhiteSpace(OutputPath) ? string.Empty : OutputPath;

    public bool HasOutputPathColumnTooltip => !string.IsNullOrWhiteSpace(OutputPath);

    public string TempWorkingDirectory
    {
        get => _tempWorkingDirectory;
        set => SetField(ref _tempWorkingDirectory, value);
    }

    public string LogPath
    {
        get => _logPath;
        set
        {
            if (SetField(ref _logPath, value))
            {
                OnPropertyChanged(nameof(LogPathDisplay));
                OnPropertyChanged(nameof(HasLogPath));
                OnPropertyChanged(nameof(OperationLogDisplay));
                OnPropertyChanged(nameof(HasOperationReport));
                OnPropertyChanged(nameof(OperationReportTitle));
                OnPropertyChanged(nameof(OperationReportMessage));
                OnPropertyChanged(nameof(HasVerificationResult));
                OnPropertyChanged(nameof(VerificationResultBadgeText));
            }
        }
    }

    public string LogPathDisplay => string.IsNullOrWhiteSpace(LogPath) ? "-" : Path.GetFileName(LogPath);

    public bool HasLogPath => !string.IsNullOrWhiteSpace(LogPath);

    public string FileName
    {
        get => _fileName;
        set
        {
            if (SetField(ref _fileName, value))
            {
                Pipeline.FileName = value;
                OnPropertyChanged(nameof(FullPathTooltip));
                OnPropertyChanged(nameof(FileTitleDisplay));
                OnPropertyChanged(nameof(FilePathSubtitleDisplay));
                OnPropertyChanged(nameof(QueueTitleDisplay));
                OnPropertyChanged(nameof(DisplayFileName));
            }
        }
    }

    public bool HasSingleSupportedOperation => _supportedOperationCodes.Length == 1;

    public bool HasMultipleSupportedOperations => _supportedOperationCodes.Length > 1;

    public bool ShowInlineFixedAction =>
        HasSingleSupportedOperation
        && !TaskQueueStateCodes.IsActiveRunning(QueueRowDisplayState)
        && !TaskQueueStateCodes.IsTerminal(QueueRowDisplayState);

    public bool ShowOperationCombo =>
        HasMultipleSupportedOperations
        && !TaskQueueStateCodes.IsTerminal(QueueRowDisplayState)
        && !TaskQueueStateCodes.IsActiveRunning(QueueRowDisplayState);

    public bool ShowActionPlaceholder =>
        ShowOperationCombo
        && !HasConcreteProcessingPlan;

    public bool UiShowIdleOperationPicker => ShowActionPlaceholder;

    public bool UiShowIdleStartRow =>
        ShowOperationCombo
        && HasConcreteProcessingPlan
        && !TaskQueueStateCodes.IsActiveRunning(QueueRowDisplayState)
        && !TaskQueueStateCodes.IsTerminal(QueueRowDisplayState);

    public bool UiShowIdleSurface =>
        (ShowInlineFixedAction || ShowOperationCombo)
        && !TaskQueueStateCodes.IsActiveRunning(QueueRowDisplayState)
        && !TaskQueueStateCodes.IsTerminal(QueueRowDisplayState);

    public bool UiShowProgressRow => TaskQueueStateCodes.IsActiveRunning(QueueRowDisplayState);

    public bool UiShowRunningActionsRow => TaskQueueStateCodes.IsActiveRunning(QueueRowDisplayState);

    public bool UiShowTerminalActionsRow => TaskQueueStateCodes.IsTerminal(QueueRowDisplayState);

    public bool UiShowMetadataExtendedChips => HasConcreteProcessingPlan;

    public string ProgressRegionPhaseIsolated => QueueRowDisplayPhaseIsolated;

    public string InputType
    {
        get => _inputType;
        set => SetField(ref _inputType, value);
    }

    public string FileSizeDisplay
    {
        get => _fileSizeDisplay;
        private set
        {
            if (SetField(ref _fileSizeDisplay, value))
            {
                OnPropertyChanged(nameof(FileSize));
                OnPropertyChanged(nameof(HasFileSizeBadge));
            }
        }
    }

    public string FileSize => FileSizeDisplay;

    public bool HasFileSizeBadge =>
        !string.IsNullOrWhiteSpace(FileSizeDisplay)
        && !string.Equals(FileSizeDisplay, "-", StringComparison.Ordinal);

    public string SourceDirectoryDisplay
    {
        get => _sourceDirectoryDisplay;
        private set => SetField(ref _sourceDirectoryDisplay, value);
    }

    public string ShortSourcePathDisplay
    {
        get => _shortSourcePathDisplay;
        private set => SetField(ref _shortSourcePathDisplay, value);
    }

    public string FullPathTooltip => string.IsNullOrWhiteSpace(OriginalPath) ? SourcePath : OriginalPath;

    public string FileTitleDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FileName))
            {
                return "-";
            }

            string noExtension = Path.GetFileNameWithoutExtension(FileName);
            return string.IsNullOrEmpty(noExtension) ? FileName : noExtension;
        }
    }

    public string FilePathSubtitleDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FileName))
            {
                return "-";
            }

            return ShortSourcePathDisplay == "-"
                ? FileName
                : $"{ShortSourcePathDisplay}\\{FileName}";
        }
    }

    public string DetectedPlatform
    {
        get => _detectedPlatform;
        set
        {
            if (SetField(ref _detectedPlatform, value))
            {
                OnPropertyChanged(nameof(PlatformDisplayText));
                OnPropertyChanged(nameof(PlatformNameGridDisplay));
                OnPropertyChanged(nameof(ConsoleType));
                OnPropertyChanged(nameof(HasPlatformBadge));
                OnPropertyChanged(nameof(PlatformBadgeDisplay));
            }
        }
    }

    public string DetectionReason
    {
        get => _detectionReason;
        set
        {
            if (SetField(ref _detectionReason, value))
            {
                OnPropertyChanged(nameof(HasPlatformBadge));
                OnPropertyChanged(nameof(PlatformBadgeDisplay));
                OnPropertyChanged(nameof(PlatformNameGridDisplay));
            }
        }
    }

    public string PlatformDisplayText => string.IsNullOrWhiteSpace(DetectedPlatform)
        ? ArabicUi.Get("LocCommon_Unknown")
        : DetectedPlatform;

    public string PlatformNameGridDisplay => PlatformBadgeDisplay;

    public bool HasPlatformBadge => !string.IsNullOrWhiteSpace(PlatformBadgeDisplay);

    public string PlatformBadgeDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DetectedPlatform))
            {
                return string.Empty;
            }

            string value = DetectedPlatform.Trim();
            if (IsRawConflictBadge(value, DetectionReason))
            {
                return ArabicUi.Get("LocQueue_RawConflictBadge");
            }

            if (IsLowConfidencePlatformDetection(value, DetectionReason) || IsAmbiguousPlatformName(value))
            {
                return string.Empty;
            }

            if (IsGenericOrDuplicatePlatformBadge(value))
            {
                return string.Empty;
            }

            int slash = value.IndexOf('/');
            if (slash > 0)
            {
                value = value[..slash].Trim();
            }

            if (value.Length > 32)
            {
                value = value[..32].TrimEnd() + "…";
            }

            return value;
        }
    }

    public string SourceFilePath => string.IsNullOrWhiteSpace(SourcePath) ? "-" : SourcePath;

    public string QueueTitleDisplay
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(FileName))
            {
                return FileName;
            }

            string path = SourcePath;
            return string.IsNullOrWhiteSpace(path) ? "-" : Path.GetFileName(path);
        }
    }

    public string ConsoleType => string.IsNullOrWhiteSpace(DetectedPlatform) ? "-" : DetectedPlatform;

    public string SourceExtensionDisplay
    {
        get
        {
            string path = SourcePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return "-";
            }

            string extension = Path.GetExtension(path);
            return string.IsNullOrEmpty(extension) ? "-" : extension.ToLowerInvariant();
        }
    }

    public string RequestedAction
    {
        get => _requestedAction;
        set
        {
            if (SetField(ref _requestedAction, value))
            {
                OnPropertyChanged(nameof(TaskActionShortLabel));
                OnPropertyChanged(nameof(TaskActionIconGlyph));
                OnPropertyChanged(nameof(ActionLabelArabic));
                OnPropertyChanged(nameof(ActionLabelTechnical));
                OnPropertyChanged(nameof(HasActionLabelTechnical));
                OnPropertyChanged(nameof(OperationReportTitle));
                OnPropertyChanged(nameof(OperationReportMessage));
                OnPropertyChanged(nameof(HasVerificationResult));
                OnPropertyChanged(nameof(VerificationResultBadgeText));
                OnPropertyChanged(nameof(SelectedOperationBinding));
                OnPropertyChanged(nameof(HasConcreteProcessingPlan));
                OnPropertyChanged(nameof(ShowInlineFixedAction));
                OnPropertyChanged(nameof(ShowOperationCombo));
                OnPropertyChanged(nameof(ShowActionPlaceholder));
                NotifyMetadataStripChanged();
            }
        }
    }

    public string CurrentState
    {
        get => _currentState;
        set
        {
            string normalized = value == TaskQueueStateCodes.Ready ? TaskQueueStateCodes.Pending : value;
            if (SetField(ref _currentState, normalized))
            {
                OnPropertyChanged(nameof(StateDisplay));
                OnPropertyChanged(nameof(StateIconGlyph));
                OnPropertyChanged(nameof(ProcessingPhase));
                OnPropertyChanged(nameof(StatePhaseArabic));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusHeadlineIsolated));
                OnPropertyChanged(nameof(ProgressRegionPhaseIsolated));
                OnPropertyChanged(nameof(QueueRowDisplayState));
                OnPropertyChanged(nameof(QueueRowDisplayFinalResult));
                OnPropertyChanged(nameof(QueueRowDisplayDetailArabic));
                OnPropertyChanged(nameof(QueueRowExtendedTooltip));
                OnPropertyChanged(nameof(QueueRowDisplayDetailIsVisible));
                OnPropertyChanged(nameof(QueueRowDisplayPhaseIsolated));
                OnPropertyChanged(nameof(ProgressBarDisplayValue));
                OnPropertyChanged(nameof(ProgressPercentDisplay));
                OnPropertyChanged(nameof(ShowRuntimeActivitySpinner));
                OnPropertyChanged(nameof(ShowProgressPercent));
                OnPropertyChanged(nameof(HasVerificationResult));
                OnPropertyChanged(nameof(VerificationResultBadgeText));
                OnPropertyChanged(nameof(ShowInlineFixedAction));
                OnPropertyChanged(nameof(ShowOperationCombo));
                OnPropertyChanged(nameof(ShowActionPlaceholder));
                RefreshPipelineView();
                NotifyUiCardLayoutProperties();
            }
        }
    }

    public ProcessingState ProcessingPhase => ProcessingStateMapper.Map(CurrentState, _queueSnapshotStatus);

    public string StatePhaseArabic => ArabicUi.ProcessingPhaseHeadline(ProcessingPhase);

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string StateDisplay => CurrentState == TaskQueueStateCodes.Ready ? TaskQueueStateCodes.Pending : CurrentState;

    public string StatusDetail
    {
        get => _statusDetail;
        set
        {
            if (SetField(ref _statusDetail, value))
            {
                ApplySubStatusProgressHint(value);
                OnPropertyChanged(nameof(StatusDetailDisplay));
                OnPropertyChanged(nameof(SubStatusDisplay));
                OnPropertyChanged(nameof(StatusDetailUiArabic));
                OnPropertyChanged(nameof(StatusDetailUiLatin));
                OnPropertyChanged(nameof(HasStatusDetailLatin));
                OnPropertyChanged(nameof(ProgressRegionPhaseIsolated));
                OnPropertyChanged(nameof(QueueRowDisplayState));
                OnPropertyChanged(nameof(QueueRowDisplayFinalResult));
                OnPropertyChanged(nameof(QueueRowDisplayDetailArabic));
                OnPropertyChanged(nameof(QueueRowExtendedTooltip));
                OnPropertyChanged(nameof(QueueRowDisplayDetailIsVisible));
                OnPropertyChanged(nameof(QueueRowDisplayPhaseIsolated));
                OnPropertyChanged(nameof(ProgressBarDisplayValue));
                OnPropertyChanged(nameof(ProgressPercentDisplay));
                OnPropertyChanged(nameof(OperationReportMessage));
            }
        }
    }

    public string StatusDetailUiArabic => QueueRowDisplayDetailArabic;

    public string StatusDetailUiLatin =>
        ArabicUi.QueueCardTechnicalTokens(ChdmanOutputParser.StripPercentTokensForNarrative(StatusDetail));

    public bool HasStatusDetailLatin => StatusDetailUiLatin.Length > 0;

    public string ActionLabelArabic => ArabicUi.GetActionArabicLabel(RequestedAction);

    public string ActionLabelTechnical => ArabicUi.GetActionTechnicalToken(RequestedAction);

    public bool HasActionLabelTechnical => ActionLabelTechnical.Length > 0;

    public string DisplaySourceFormat => BuildDisplaySourceFormat();

    public string DisplayTargetFormat => BuildDisplayTargetFormat();

    public string DisplayAvailableOperationsText => BuildDisplayAvailableOperationsText();

    public bool HasConcreteProcessingPlan =>
        RequestedAction != TaskActionCodes.PendingSelection
        && RequestedAction != TaskActionCodes.Unsupported;

    public string StatusDetailDisplay => string.IsNullOrWhiteSpace(StatusDetail) ? "-" : ArabicUi.ResolveDisplayString(StatusDetail);

    public string SubStatusDisplay => string.IsNullOrWhiteSpace(StatusDetail)
        ? ArabicUi.Get("LocState_Idle")
        : ArabicUi.ResolveDisplayString(StatusDetail);

    public bool IsNamingCompliant
    {
        get => _isNamingCompliant;
        set
        {
            if (SetField(ref _isNamingCompliant, value))
            {
                OnPropertyChanged(nameof(ShowNamingWarning));
                OnPropertyChanged(nameof(CanApplyRedumpSuggestedName));
            }
        }
    }

    public bool ShowNamingWarning => !IsNamingCompliant;

    public bool CanApplyRedumpSuggestedName =>
        IsRedumpVerified
        && !string.IsNullOrWhiteSpace(SuggestedStandardName)
        && !IsNamingCompliant
        && !string.IsNullOrWhiteSpace(SourcePath);

    public string RedumpSuggestedNameDisplay => string.IsNullOrWhiteSpace(SuggestedStandardName)
        ? ArabicUi.Get("LocRedump_NoSuggestedName")
        : SuggestedStandardName;

    public string RedumpStatusDisplay => IntegrityColumnDisplayArabic;

    public string RedumpDetailsDisplay => string.IsNullOrWhiteSpace(IntegrityDetailTooltip)
        ? ArabicUi.Get("LocRedump_NoDetails")
        : IntegrityDetailTooltip;

    public bool HasRedumpResult => IntegrityState != IntegrityValidationState.None;

    public string SuggestedStandardName
    {
        get => _suggestedStandardName;
        set
        {
            if (SetField(ref _suggestedStandardName, value))
            {
                OnPropertyChanged(nameof(RedumpSuggestedNameDisplay));
                OnPropertyChanged(nameof(CanApplyRedumpSuggestedName));
            }
        }
    }

    public string CanonicalName
    {
        get => _canonicalName;
        set
        {
            if (SetField(ref _canonicalName, value))
            {
                OnPropertyChanged(nameof(ResolvedDisplayName));
            }
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetField(ref _displayName, value))
            {
                OnPropertyChanged(nameof(ResolvedDisplayName));
            }
        }
    }

    public string TitleId
    {
        get => _titleId;
        set => SetField(ref _titleId, value);
    }

    public string Region
    {
        get => _region;
        set => SetField(ref _region, value);
    }

    public string Version
    {
        get => _version;
        set => SetField(ref _version, value);
    }

    public string VerifiedHash
    {
        get => _verifiedHash;
        set => SetField(ref _verifiedHash, value);
    }

    public bool IsNameVerified
    {
        get => _isNameVerified;
        set => SetField(ref _isNameVerified, value);
    }

    public NamingConfidence NamingConfidence
    {
        get => _namingConfidence;
        set => SetField(ref _namingConfidence, value);
    }

    public string ResolvedDisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CanonicalName))
            {
                return CanonicalName;
            }

            if (!string.IsNullOrWhiteSpace(DisplayName))
            {
                return DisplayName;
            }

            if (!string.IsNullOrWhiteSpace(FileTitleDisplay) && FileTitleDisplay != "-")
            {
                return FileTitleDisplay;
            }

            return FileName;
        }
    }

    public string DeepHashCachePath
    {
        get => _deepHashCachePath;
        set => SetField(ref _deepHashCachePath, value);
    }

    public DeepHashAnalysisResult? DeepHashCachedResult
    {
        get => _deepHashCachedResult;
        set => SetField(ref _deepHashCachedResult, value);
    }

    public bool IsResultDetermined => FinalResult != TaskFinalResultCodes.None;

    public string FinalResult
    {
        get => _finalResult;
        set
        {
            if (SetField(ref _finalResult, value))
            {
                UpdateResultBadgeBrushes();
                OnPropertyChanged(nameof(IsResultDetermined));
                OnPropertyChanged(nameof(ResultStatusIconGlyph));
                OnPropertyChanged(nameof(QueueCardBadgeTextIsolated));
                OnPropertyChanged(nameof(QueueRowDisplayState));
                OnPropertyChanged(nameof(QueueRowDisplayFinalResult));
                OnPropertyChanged(nameof(QueueRowDisplayDetailArabic));
                OnPropertyChanged(nameof(QueueRowExtendedTooltip));
                OnPropertyChanged(nameof(QueueRowDisplayDetailIsVisible));
                OnPropertyChanged(nameof(QueueRowDisplayPhaseIsolated));
                OnPropertyChanged(nameof(ProgressBarDisplayValue));
                OnPropertyChanged(nameof(ProgressPercentDisplay));
                OnPropertyChanged(nameof(UiShowIdleOperationPicker));
                OnPropertyChanged(nameof(UiShowIdleStartRow));
                OnPropertyChanged(nameof(UiShowIdleSurface));
                OnPropertyChanged(nameof(UiShowProgressRow));
                OnPropertyChanged(nameof(UiShowRunningActionsRow));
                OnPropertyChanged(nameof(UiShowTerminalActionsRow));
                OnPropertyChanged(nameof(OperationReportTitle));
                OnPropertyChanged(nameof(OperationReportMessage));
                OnPropertyChanged(nameof(HasVerificationResult));
                OnPropertyChanged(nameof(VerificationResultBadgeText));
            }
        }
    }

    public MediaBrush ResultBadgeBrush
    {
        get => _resultBadgeBrush;
        set => SetField(ref _resultBadgeBrush, value);
    }

    public MediaBrush ResultBadgeForegroundBrush
    {
        get => _resultBadgeForegroundBrush;
        set => SetField(ref _resultBadgeForegroundBrush, value);
    }

    public bool IsProgressActive
    {
        get => _isProgressActive;
        set
        {
            if (SetField(ref _isProgressActive, value))
            {
                OnPropertyChanged(nameof(ShowRuntimeActivitySpinner));
            }
        }
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set
        {
            if (SetField(ref _isIndeterminate, value))
            {
                OnPropertyChanged(nameof(ShowRuntimeActivitySpinner));
                OnPropertyChanged(nameof(ShowProgressPercent));
            }
        }
    }

    public IntegrityValidationState IntegrityState
    {
        get => _integrityState;
        private set
        {
            if (SetField(ref _integrityState, value))
            {
                OnPropertyChanged(nameof(IntegrityGlyph));
                OnPropertyChanged(nameof(IntegrityForegroundBrush));
                OnPropertyChanged(nameof(OperationReportTitle));
                OnPropertyChanged(nameof(OperationReportMessage));
                OnPropertyChanged(nameof(HasVerificationResult));
                OnPropertyChanged(nameof(VerificationResultBadgeText));
            }
        }
    }

    public string IntegrityStatusMessage
    {
        get => _integrityStatusMessage;
        private set
        {
            if (SetField(ref _integrityStatusMessage, value))
            {
                OnPropertyChanged(nameof(OperationReportMessage));
                OnPropertyChanged(nameof(VerificationResultBadgeText));
            }
        }
    }

    public string IntegrityDetailTooltip
    {
        get => _integrityTooltip;
        private set
        {
            if (SetField(ref _integrityTooltip, value))
            {
                OnPropertyChanged(nameof(OperationReportMessage));
                OnPropertyChanged(nameof(VerificationResultBadgeText));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public QueueIntakeAdvisory? IntakeAdvisory
    {
        get => _intakeAdvisory;
        set
        {
            if (SetField(ref _intakeAdvisory, value))
            {
                OnPropertyChanged(nameof(HasIntakeAdvisory));
                OnPropertyChanged(nameof(IntakeAdvisoryActionDisplay));
                OnPropertyChanged(nameof(IntakeAdvisoryConfidenceDisplay));
                OnPropertyChanged(nameof(IntakeAdvisoryPrimaryReasonDisplay));
                OnPropertyChanged(nameof(IntakeAdvisoryTooltip));
                OnPropertyChanged(nameof(QueueRowExtendedTooltip));
            }
        }
    }

    public void RefreshPipelineView()
    {
        Pipeline.FileName = FileName;
        Pipeline.Progress = (int)Math.Round(Math.Clamp(ProgressValue, 0, 100));
        Pipeline.ErrorMessage = string.IsNullOrWhiteSpace(ErrorMessage) ? null : ErrorMessage;
        Pipeline.ProcessingState = ProcessingStateMapper.Map(CurrentState, _queueSnapshotStatus);
    }

    public override string ToString() =>
        !string.IsNullOrEmpty(FileName) ? FileName : SourcePath ?? string.Empty;

    private bool IsRawConflictBadge(string value, string reason)
    {
        return value.Contains("تعارض RAW", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Raw CHD metadata conflict", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("Raw CHD metadata conflict", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("Automatic RAW extraction is blocked", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsLowConfidencePlatformDetection(string value, string reason)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (reason.Contains("اسم الملف أو المسار", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("filename hint", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("تخمين", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("مؤشرات على", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("typical", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("legacy", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private bool IsAmbiguousPlatformName(string value)
    {
        return value.Contains('/', StringComparison.Ordinal)
            || value.Contains("محتمل", StringComparison.OrdinalIgnoreCase)
            || value.Contains("uncertain", StringComparison.OrdinalIgnoreCase)
            || value.Contains("CD-Based", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsGenericOrDuplicatePlatformBadge(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        string normalized = value.Trim();
        string source = DisplaySourceFormat.Trim();

        if (string.Equals(normalized, source, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalized.Equals("CHD", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Compressed Disc", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Raw Media Image", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Unknown Platform", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Disc Image", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Archive", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("DirectFile", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Platform uncertain · Raw CHD metadata conflict", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Raw CHD metadata conflict", StringComparison.OrdinalIgnoreCase);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string FormatPercentStatic(double value) =>
        "\u200e" + $"{Math.Round(Math.Clamp(value, 0, 100)):0}%";
}