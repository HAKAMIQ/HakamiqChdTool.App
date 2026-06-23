using HakamiqChdTool.App.Core.Input;
using CommunityToolkit.Mvvm.Input;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.MediaInputPolicy;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace HakamiqChdTool.App.ViewModels;

public partial class MainWindowViewModel
{
    private static readonly TimeSpan IntakeResultAutoDismissDelay = TimeSpan.FromSeconds(4);

    private bool _isIntakeActive;
    private string _intakeTitle = string.Empty;
    private string _intakeStageText = string.Empty;
    private string _intakeAddedText = string.Empty;
    private string _intakeScannedText = string.Empty;
    private string _intakeWarningText = string.Empty;
    private bool _isIntakeWarningVisible;
    private bool _intakeCanCancel;
    private bool _isIntakeResultVisible;
    private string _intakeResultTitle = string.Empty;
    private string _intakeResultMessage = string.Empty;
    private string _intakeResultSeverity = string.Empty;
    private long _intakeUiVersion;
    private CancellationTokenSource? _intakeCancellationCts;
    private CancellationTokenSource? _intakeResultDismissCts;

    private sealed record PreparedIntakeCandidate(
        PreparedQueueCandidate Candidate,
        QueueIntakeAdvisory? Advisory);

    private sealed class QueueAddProgressState
    {
        private static readonly TimeSpan MinimumUpdateInterval = TimeSpan.FromMilliseconds(150);

        private DateTimeOffset _lastUiUpdate = DateTimeOffset.MinValue;
        private int _lastAcceptedCount = -1;
        private int _lastScannedCount = -1;
        private string _lastPhaseKey = string.Empty;
        private bool _lastSkippedCorruptArchives;
        private bool _lastSkippedUnsupportedOrDuplicate;


        public int AcceptedCount { get; set; }

        public int ScannedCount { get; set; }

        public int TotalCount { get; set; }

        public bool HasKnownTotal { get; init; }

        public string PhaseKey { get; set; } = "LocQueueAdd_ScanningFiles";


        public bool SkippedCorruptArchives { get; set; }

        public bool SkippedUnsupportedOrDuplicate { get; set; }

        public bool ShouldUpdate(bool force)
        {
            if (force)
            {
                return true;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            bool changed = AcceptedCount != _lastAcceptedCount
                || ScannedCount != _lastScannedCount
                || !string.Equals(PhaseKey, _lastPhaseKey, StringComparison.Ordinal)
                || SkippedCorruptArchives != _lastSkippedCorruptArchives
                || SkippedUnsupportedOrDuplicate != _lastSkippedUnsupportedOrDuplicate;

            return changed && now - _lastUiUpdate >= MinimumUpdateInterval;
        }

        public void MarkUpdated()
        {
            _lastUiUpdate = DateTimeOffset.UtcNow;
            _lastAcceptedCount = AcceptedCount;
            _lastScannedCount = ScannedCount;
            _lastPhaseKey = PhaseKey;
            _lastSkippedCorruptArchives = SkippedCorruptArchives;
            _lastSkippedUnsupportedOrDuplicate = SkippedUnsupportedOrDuplicate;
        }
    }

    public bool IsIntakeActive
    {
        get => _isIntakeActive;
        private set => SetProperty(ref _isIntakeActive, value);
    }

    public string IntakeTitle
    {
        get => _intakeTitle;
        private set => SetProperty(ref _intakeTitle, value);
    }

    public string IntakeStageText
    {
        get => _intakeStageText;
        private set => SetProperty(ref _intakeStageText, value);
    }

    public string IntakeAddedText
    {
        get => _intakeAddedText;
        private set => SetProperty(ref _intakeAddedText, value);
    }

    public string IntakeScannedText
    {
        get => _intakeScannedText;
        private set => SetProperty(ref _intakeScannedText, value);
    }

    public string IntakeWarningText
    {
        get => _intakeWarningText;
        private set => SetProperty(ref _intakeWarningText, value);
    }

    public bool IsIntakeWarningVisible
    {
        get => _isIntakeWarningVisible;
        private set => SetProperty(ref _isIntakeWarningVisible, value);
    }

    public bool IntakeCanCancel
    {
        get => _intakeCanCancel;
        private set => SetProperty(ref _intakeCanCancel, value);
    }

    public bool IsIntakeResultVisible
    {
        get => _isIntakeResultVisible;
        private set => SetProperty(ref _isIntakeResultVisible, value);
    }

    public string IntakeResultTitle
    {
        get => _intakeResultTitle;
        private set => SetProperty(ref _intakeResultTitle, value);
    }

    public string IntakeResultMessage
    {
        get => _intakeResultMessage;
        private set => SetProperty(ref _intakeResultMessage, value);
    }

    public string IntakeResultSeverity
    {
        get => _intakeResultSeverity;
        private set => SetProperty(ref _intakeResultSeverity, value);
    }

    public IRelayCommand CancelIntakeCommand => CancelAddingFilesCommand;

    private async Task<long> BeginIntakeOperationAsync(
        Dispatcher dispatcher,
        QueueIntakeSource intakeSource,
        int rawPathCount)
    {
        long version = Interlocked.Increment(ref _intakeUiVersion);
        CancelIntakeResultAutoDismiss();

        Log.ForContext<MainWindowViewModel>().Debug(
            "Intake started. Source={Source}, RawPathCount={RawPathCount}",
            intakeSource,
            rawPathCount);

        await dispatcher.InvokeAsync(
            () =>
            {
                IsIntakeResultVisible = false;
                IntakeResultTitle = string.Empty;
                IntakeResultMessage = string.Empty;
                IntakeResultSeverity = string.Empty;
                IsIntakeActive = true;
                IntakeCanCancel = true;
                IntakeTitle = ArabicUi.Get("LocQueueAdd_Title");
                IntakeStageText = ArabicUi.Get("LocQueueAdd_ScanningFiles");
                IntakeAddedText = string.Empty;
                IntakeScannedText = string.Empty;
                IntakeWarningText = string.Empty;
                IsIntakeWarningVisible = false;
            },
            DispatcherPriority.Send);

        return version;
    }

    private async Task EndIntakeOperationAsync(Dispatcher dispatcher, long version)
    {
        await dispatcher.InvokeAsync(
            () =>
            {
                if (version != _intakeUiVersion)
                {
                    return;
                }

                Interlocked.Increment(ref _intakeUiVersion);
                IsIntakeActive = false;
                IntakeCanCancel = false;
                IntakeTitle = string.Empty;
                IntakeStageText = string.Empty;
                IntakeAddedText = string.Empty;
                IntakeScannedText = string.Empty;
                IntakeWarningText = string.Empty;
                IsIntakeWarningVisible = false;
            },
            DispatcherPriority.Background);
    }

    private async Task UpdateQueueAddProgressAsync(
        Dispatcher dispatcher,
        long version,
        QueueAddProgressState progress,
        bool force = false)
    {
        if (version != _intakeUiVersion || !IsIntakeActive)
        {
            return;
        }

        if (!progress.ShouldUpdate(force))
        {
            return;
        }

        string stageText = ArabicUi.Get(progress.PhaseKey);
        string addedText = BuildIntakeAddedText(progress);
        string scannedText = ArabicUi.Format(
            "LocQueueAdd_ScannedItemsFormat",
            progress.ScannedCount,
            Math.Max(progress.TotalCount, progress.ScannedCount));
        string warningText = BuildIntakeWarningText(progress);

        await dispatcher.InvokeAsync(
            () =>
            {
                if (version != _intakeUiVersion || !IsIntakeActive)
                {
                    return;
                }

                IntakeStageText = stageText;
                IntakeAddedText = addedText;
                IntakeScannedText = scannedText;
                IntakeWarningText = warningText;
                IsIntakeWarningVisible = !string.IsNullOrWhiteSpace(warningText);
                IntakeCanCancel = CanCancelAddingFiles();
                _session.SetFooterIntakeProgress(
                    stageText,
                    progress.ScannedCount,
                    Math.Max(progress.TotalCount, progress.ScannedCount),
                    progress.AcceptedCount,
                    progress.HasKnownTotal);
            },
            DispatcherPriority.Background);

        progress.MarkUpdated();
    }

    private static string BuildIntakeAddedText(QueueAddProgressState progress)
    {
        return ArabicUi.Format("LocQueueAdd_AddedFilesInProgressFormat", progress.AcceptedCount);
    }

    private static string BuildIntakeWarningText(QueueAddProgressState progress)
    {
        var lines = new List<string>();

        if (progress.SkippedCorruptArchives)
        {
            lines.Add(ArabicUi.Get("LocQueueAdd_SkippedCorruptArchives"));
        }

        if (progress.SkippedUnsupportedOrDuplicate)
        {
            lines.Add(ArabicUi.Get("LocQueueAdd_SkippedUnsupportedOrDuplicate"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildNoValidFilesAddedMessage(
        bool skippedCorruptArchives,
        bool skippedUnsupportedOrDuplicate)
    {
        var lines = new List<string>();

        if (skippedCorruptArchives)
        {
            lines.Add(ArabicUi.Get("LocQueueAdd_SkippedCorruptArchives"));
        }

        if (skippedUnsupportedOrDuplicate || lines.Count == 0)
        {
            lines.Add(ArabicUi.Get("LocQueueAdd_SkippedUnsupportedOrDuplicate"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private async Task ShowIntakeResultAsync(
        Dispatcher dispatcher,
        int actualAddedDelta,
        bool skippedCorruptArchives,
        bool skippedUnsupportedOrDuplicate,
        bool wasCancelled)
    {
        string title;
        string message;
        string severity;

        if (wasCancelled)
        {
            title = ArabicUi.Get("LocQueueAdd_AddCancelled");
            message = ArabicUi.Get("LocQueueAdd_AddCancelled");
            severity = "Info";
        }
        else if (actualAddedDelta > 0)
        {
            title = ArabicUi.Get("LocQueueActivity_AddedTitle");
            message = ArabicUi.Format("LocQueueActivity_AddedMessage", actualAddedDelta);
            if (skippedCorruptArchives)
            {
                message += Environment.NewLine + ArabicUi.Get("LocQueueAdd_SkippedCorruptArchives");
            }

            if (skippedUnsupportedOrDuplicate)
            {
                message += Environment.NewLine + ArabicUi.Get("LocQueueAdd_SkippedUnsupportedOrDuplicate");
            }

            severity = skippedCorruptArchives || skippedUnsupportedOrDuplicate ? "Warning" : "Success";
        }
        else
        {
            title = ArabicUi.Get("LocQueueActivity_AddSkippedTitle");
            message = ArabicUi.Get("LocQueueActivity_AddSkippedTitle")
                + Environment.NewLine
                + BuildNoValidFilesAddedMessage(skippedCorruptArchives, skippedUnsupportedOrDuplicate);
            severity = "Warning";
        }

        await dispatcher.InvokeAsync(
            () =>
            {
                IntakeResultTitle = title;
                IntakeResultMessage = message;
                IntakeResultSeverity = severity;
                IsIntakeResultVisible = true;
            },
            DispatcherPriority.Background);

        Log.ForContext<MainWindowViewModel>().Debug(
            "Intake result displayed. Added={Added}, SkippedCorrupt={SkippedCorrupt}, SkippedUnsupported={SkippedUnsupported}, Cancelled={Cancelled}",
            actualAddedDelta,
            skippedCorruptArchives,
            skippedUnsupportedOrDuplicate,
            wasCancelled);

        ScheduleIntakeResultAutoDismiss(dispatcher);
    }

    private void ScheduleIntakeResultAutoDismiss(Dispatcher dispatcher)
    {
        CancelIntakeResultAutoDismiss();
        var dismissCts = new CancellationTokenSource();
        _intakeResultDismissCts = dismissCts;

        _ = DismissIntakeResultAfterDelayAsync(dispatcher, dismissCts);
    }

    private async Task DismissIntakeResultAfterDelayAsync(
        Dispatcher dispatcher,
        CancellationTokenSource dismissCts)
    {
        try
        {
            await Task.Delay(IntakeResultAutoDismissDelay, dismissCts.Token).ConfigureAwait(false);

            await dispatcher.InvokeAsync(
                () =>
                {
                    if (!ReferenceEquals(_intakeResultDismissCts, dismissCts))
                    {
                        return;
                    }

                    IsIntakeResultVisible = false;
                    IntakeResultTitle = string.Empty;
                    IntakeResultMessage = string.Empty;
                    IntakeResultSeverity = string.Empty;
                    Log.ForContext<MainWindowViewModel>().Debug("Intake result dismissed.");
                },
                DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_intakeResultDismissCts, dismissCts))
            {
                _intakeResultDismissCts = null;
            }

            dismissCts.Dispose();
        }
    }

    private void CancelIntakeResultAutoDismiss()
    {
        CancellationTokenSource? pending = Interlocked.Exchange(ref _intakeResultDismissCts, null);
        if (pending is null)
        {
            return;
        }

        try
        {
            pending.Cancel();
        }
        finally
        {
            pending.Dispose();
        }
    }

    [RelayCommand]
    private void DismissIntakeResult()
    {
        CancelIntakeResultAutoDismiss();
        IsIntakeResultVisible = false;
        IntakeResultTitle = string.Empty;
        IntakeResultMessage = string.Empty;
        IntakeResultSeverity = string.Empty;
        Log.ForContext<MainWindowViewModel>().Debug("Intake result dismissed.");
    }

    private static PreparedQueueBatchResult PrepareQueueBatch(
        IReadOnlyList<string> paths,
        QueueExecutionProfile executionProfile,
        QueueIntakeSource intakeSource,
        CancellationToken cancellationToken,
        IDictionary<ArchiveInspectionCacheKey, ArchiveContentPreviewResult>? archivePreviewCache = null,
        IDictionary<ArchiveInspectionCacheKey, SevenZipProcessResult>? sevenZipListingCache = null)
    {
        var candidates = new List<PreparedQueueCandidate>(paths.Count);
        var rejectedArchiveMessageKeys = new List<string>();
        int acceptedArchives = 0;
        bool skippedCorruptArchives = false;
        bool skippedUnsupportedInputs = false;

        foreach (string path in paths)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new PreparedQueueBatchResult(
                    Array.Empty<PreparedQueueCandidate>(),
                    acceptedArchives,
                    rejectedArchiveMessageKeys,
                    WasCancelled: true);
            }

            MediaInputDecision mediaDecision = global::HakamiqChdTool.App.Services.MediaInputPolicy.MediaInputPolicy.Evaluate(path);
            if (mediaDecision.IsBlocked)
            {
                skippedUnsupportedInputs = true;
                continue;
            }

            string effectivePath = mediaDecision.EffectivePath;
            QueueInputClassification classification = QueueInputClassifier.Classify(effectivePath);

            if (ArchivePreviewIntakePolicy.ShouldPreviewArchive(classification, intakeSource))
            {
                ArchiveContentPreviewResult preview = ArchivePreviewService.PreviewForIntake(
                    effectivePath,
                    intakeSource,
                    cancellationToken,
                    archivePreviewCache,
                    sevenZipListingCache);

                if (preview.WasCancelled)
                {
                    return new PreparedQueueBatchResult(
                        Array.Empty<PreparedQueueCandidate>(),
                        acceptedArchives,
                        rejectedArchiveMessageKeys,
                        WasCancelled: true);
                }

                if (!preview.CanUnpackThenConvert)
                {
                    string messageKey = string.IsNullOrWhiteSpace(preview.MessageResourceKey)
                        ? "LocArchive_NoConvertibleDiscImage"
                        : preview.MessageResourceKey;

                    rejectedArchiveMessageKeys.Add(messageKey);
                    skippedCorruptArchives |= IsCorruptOrUnreadableArchiveMessageKey(messageKey);

                    continue;
                }

                acceptedArchives++;
            }

            string action = ResolveRequestedAction(effectivePath, executionProfile);

            if (ArchivePreviewIntakePolicy.BlocksQueuedArchiveProcessing(effectivePath, intakeSource))
            {
                action = TaskActionCodes.StageArchiveForConversion;
            }

            if (string.Equals(action, TaskActionCodes.Unsupported, StringComparison.Ordinal))
            {
                continue;
            }

            QueuePlatformView platform = BuildQueuePlatformView(effectivePath);

            candidates.Add(new PreparedQueueCandidate(
                effectivePath,
                action,
                platform.PlatformName,
                platform.Reason));
        }

        return new PreparedQueueBatchResult(
            ApplySiblingPlatformConsensus(candidates),
            acceptedArchives,
            rejectedArchiveMessageKeys,
            skippedCorruptArchives,
            skippedUnsupportedInputs);
    }

    private static bool IsCorruptOrUnreadableArchiveMessageKey(string messageResourceKey)
    {
        return string.Equals(messageResourceKey, "LocArchive_PreviewUnreadable", StringComparison.Ordinal)
            || string.Equals(messageResourceKey, "LocQueueAdd_ArchivePreviewTimeout", StringComparison.Ordinal)
            || string.Equals(messageResourceKey, "LocQueueAdd_ArchivePreviewCancelled", StringComparison.Ordinal);
    }

    private static IEnumerable<string> EnumerateInputPaths(
        IReadOnlyList<string> rawList,
        QueueIngestKind inputKind,
        SearchOption searchOption)
    {
        foreach (MediaInputDescriptor descriptor in MediaInputPipelineStatic.Resolve(rawList, inputKind, searchOption))
        {
            if (descriptor.Kind == MediaInputKind.Folder)
            {
                continue;
            }

            yield return descriptor.FullPath;
        }
    }

    private static int CountExistingDirectories(IEnumerable<string> rawList)
    {
        int count = 0;
        foreach (string path in rawList)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                {
                    count++;
                }
            }
            catch (Exception ex) when (IsExpectedPathNormalizationException(ex))
            {
            }
        }

        return count;
    }

    private Task CompleteAddPathsCancellationAsync(Dispatcher dispatcher)
    {
        return dispatcher.InvokeAsync(
            () =>
            {
                _session.SetFooterStatus(ArabicUi.Get("LocQueueAdd_AddCancelled"));
                _session.UpdateUiState();
            },
            DispatcherPriority.Background).Task;
    }

    private bool CanAcceptQueueInput()
    {
        return !_session.IsQueueInteractionLocked && !IsAddingFiles;
    }

    private bool CanCancelAddingFiles()
    {
        return IsAddingFiles
            && _intakeCancellationCts is { IsCancellationRequested: false };
    }

    [RelayCommand(CanExecute = nameof(CanCancelAddingFiles))]
    private void CancelAddingFiles()
    {
        _intakeCancellationCts?.Cancel();
        IntakeCanCancel = false;
        IntakeStageText = ArabicUi.Get("LocQueueAdd_AddCancelled");
        CancelAddingFilesCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsAddingFilesChanged(bool value)
    {
        NotifyQueueCommandsCanExecuteChanged();
    }
}
