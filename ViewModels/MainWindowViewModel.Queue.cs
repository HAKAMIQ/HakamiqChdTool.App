using HakamiqChdTool.App.Ui.Queue;
using CommunityToolkit.Mvvm.Input;
using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.MediaInputPolicy;
using HakamiqChdTool.App.Services.PlayStation.Ps2;
using HakamiqChdTool.App.ViewModels.Virtualization;
using HakamiqChdTool.App.Views;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace HakamiqChdTool.App.ViewModels;

public partial class MainWindowViewModel
{
    private static readonly IInputResolver InputResolverStatic = new Core.Input.InputResolver();
    private static readonly IMediaInputPipeline MediaInputPipelineStatic = new MediaInputPipeline(MediaInputClassifier.Shared, InputResolverStatic);
    private static readonly ArchiveContentPreviewService ArchivePreviewService = new();
    private static readonly FormatSafetyAdvisor FormatSafetyAdvisorStatic = new();

    private readonly record struct QueuePlatformView(string PlatformName, string Reason);

    private readonly record struct PreparedQueueCandidate(
        string Path,
        string Action,
        string DetectedPlatform,
        string DetectionReason);

    private sealed record PreparedQueueBatchResult(
        IReadOnlyList<PreparedQueueCandidate> Candidates,
        int AcceptedArchives,
        IReadOnlyList<string> RejectedArchiveMessageKeys,
        bool SkippedCorruptArchives = false,
        bool SkippedUnsupportedInputs = false,
        bool WasCancelled = false);

    public async Task IngestPathsAsync(
        IEnumerable<string> rawPaths,
        QueueIngestKind inputKind,
        QueueIntakeSource intakeSource,
        CancellationToken cancellationToken = default)
    {
        _ = await AddPathsCoreAsync(
            rawPaths,
            inputKind,
            QueueExecutionProfile.Standard,
            intakeSource,
            cancellationToken).ConfigureAwait(true);
    }

    public Task<IReadOnlyList<Guid>> IngestQuickPathsAsync(
        IEnumerable<string> rawPaths,
        QueueIngestKind inputKind,
        QueueExecutionProfile executionProfile,
        QueueIntakeSource intakeSource,
        CancellationToken cancellationToken = default)
    {
        return AddPathsCoreAsync(
            rawPaths,
            inputKind,
            executionProfile,
            intakeSource,
            cancellationToken);
    }

    private async Task<IReadOnlyList<Guid>> AddPathsCoreAsync(
        IEnumerable<string> rawPaths,
        QueueIngestKind inputKind,
        QueueExecutionProfile executionProfile,
        QueueIntakeSource intakeSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rawPaths);

        List<string> rawList = rawPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Dispatcher dispatcher = _session.UiDispatcher;

        if (IsAddingFiles)
        {
            await dispatcher.InvokeAsync(() =>
            {
                _session.SetFooterStatus(MainWindowMessages.WaitForBackgroundOp);
                _session.UpdateUiState();
            }, DispatcherPriority.Background);

            return Array.Empty<Guid>();
        }

        if (rawList.Count == 0)
        {
            await dispatcher.InvokeAsync(() =>
            {
                _session.SetFooterStatus(MainWindowMessages.NothingNewAdded);
                _session.UpdateUiState();
            }, DispatcherPriority.Background);

            return Array.Empty<Guid>();
        }

        using CancellationTokenSource intakeCancellationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _intakeCancellationCts = intakeCancellationCts;
        IsAddingFiles = true;
        long intakeUiVersion = 0;
        bool intakeUiEnded = false;

        try
        {
            CancellationToken intakeToken = intakeCancellationCts.Token;
            int queueCountBefore = await dispatcher.InvokeAsync(
                () => _session.QueueRows.Count,
                DispatcherPriority.Background);

            intakeUiVersion = await BeginIntakeOperationAsync(
                    dispatcher,
                    intakeSource,
                    rawList.Count)
                .ConfigureAwait(false);

            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);

            SearchOption searchOption = await dispatcher.InvokeAsync(
                () => _session.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly,
                DispatcherPriority.Normal);

            HashSet<string> existingPaths = await dispatcher.InvokeAsync(
                () => BuildExistingPathSet(_session.QueueRows),
                DispatcherPriority.Background);

            HashSet<string> directRawFilePaths = BuildDirectRawFilePathSet(rawList);

            var seenImportedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var preparedCandidates = new List<PreparedIntakeCandidate>();
            var rejectedArchiveMessageKeys = new List<string>();
            var archivePreviewCache = new Dictionary<ArchiveInspectionCacheKey, ArchiveContentPreviewResult>();
            var sevenZipListingCache = new Dictionary<ArchiveInspectionCacheKey, SevenZipProcessResult>();

            int archiveFileCount = 0;
            int unsupportedFileCount = 0;
            int duplicateFileCount = 0;
            int missingPathCount = 0;
            int acceptedArchiveTotal = 0;
            int directoryCount = CountExistingDirectories(rawList);

            var progress = new QueueAddProgressState
            {
                TotalCount = Math.Max(rawList.Count, 1),
                HasKnownTotal = directoryCount == 0
            };

            await UpdateQueueAddProgressAsync(dispatcher, intakeUiVersion, progress, force: true).ConfigureAwait(false);

            foreach (string discoveredPath in EnumerateInputPaths(rawList, inputKind, searchOption))
            {
                intakeToken.ThrowIfCancellationRequested();


                progress.ScannedCount++;
                progress.TotalCount = Math.Max(progress.TotalCount, progress.ScannedCount);
                progress.PhaseKey = "LocQueueAdd_ScanningFiles";
                await UpdateQueueAddProgressAsync(dispatcher, intakeUiVersion, progress).ConfigureAwait(false);

                if (!IsExistingQueueInputPath(discoveredPath))
                {
                    missingPathCount++;
                    continue;
                }

                MediaInputDecision mediaDecision = global::HakamiqChdTool.App.Services.MediaInputPolicy.MediaInputPolicy.Evaluate(discoveredPath);
                if (mediaDecision.IsBlocked)
                {
                    unsupportedFileCount++;
                    progress.SkippedUnsupportedOrDuplicate = true;

                    if (ShouldCreateFailedIntakeRow(discoveredPath, directRawFilePaths)
                        && TryAddUnsupportedIntakeCandidate(
                            discoveredPath,
                            mediaDecision.MessageKey,
                            existingPaths,
                            seenImportedPaths,
                            preparedCandidates,
                            ref duplicateFileCount))
                    {
                        progress.AcceptedCount = preparedCandidates.Count;
                    }

                    continue;
                }

                string effectiveDiscoveredPath = mediaDecision.EffectivePath;
                QueueInputClassification classification = QueueInputClassifier.Classify(effectiveDiscoveredPath);
                if (!classification.IsSupported)
                {
                    unsupportedFileCount++;
                    progress.SkippedUnsupportedOrDuplicate = true;

                    if (ShouldCreateFailedIntakeRow(effectiveDiscoveredPath, directRawFilePaths)
                        && TryAddUnsupportedIntakeCandidate(
                            effectiveDiscoveredPath,
                            ChdWorkflowProfilePlanner.UnsupportedMessageKey,
                            existingPaths,
                            seenImportedPaths,
                            preparedCandidates,
                            ref duplicateFileCount))
                    {
                        progress.AcceptedCount = preparedCandidates.Count;
                    }

                    continue;
                }

                string normalizedPath = NormalizePathForAdvisoryKey(effectiveDiscoveredPath);
                if (existingPaths.Contains(normalizedPath) || !seenImportedPaths.Add(normalizedPath))
                {
                    duplicateFileCount++;
                    progress.SkippedUnsupportedOrDuplicate = true;
                    continue;
                }

                if (classification.IsArchiveContainer)
                {
                    archiveFileCount++;
                    progress.PhaseKey = "LocQueueAdd_ScanningArchives";
                    await UpdateQueueAddProgressAsync(dispatcher, intakeUiVersion, progress, force: true).ConfigureAwait(false);
                }

                PreparedQueueBatchResult prepared = await Task.Run(
                    () => PrepareQueueBatch(
                        [effectiveDiscoveredPath],
                        executionProfile,
                        intakeSource,
                        intakeToken,
                        archivePreviewCache,
                        sevenZipListingCache),
                    intakeToken).ConfigureAwait(false);

                if (prepared.WasCancelled)
                {
                    await EndIntakeOperationAsync(dispatcher, intakeUiVersion).ConfigureAwait(false);
                    intakeUiEnded = true;
                    await CompleteAddPathsCancellationAsync(dispatcher).ConfigureAwait(false);
                    await ShowIntakeResultAsync(
                        dispatcher,
                        actualAddedDelta: 0,
                        skippedCorruptArchives: progress.SkippedCorruptArchives,
                        skippedUnsupportedOrDuplicate: progress.SkippedUnsupportedOrDuplicate,
                        wasCancelled: true).ConfigureAwait(false);
                    return Array.Empty<Guid>();
                }

                acceptedArchiveTotal += prepared.AcceptedArchives;
                rejectedArchiveMessageKeys.AddRange(prepared.RejectedArchiveMessageKeys);
                progress.SkippedCorruptArchives |= prepared.SkippedCorruptArchives;
                progress.SkippedUnsupportedOrDuplicate |= prepared.SkippedUnsupportedInputs;

                foreach (PreparedQueueCandidate candidate in prepared.Candidates)
                {
                    QueueIntakeAdvisory? advisory = Ps2CompatibilityAdvisoryService.BuildQueueAdvisory(
                        candidate.Path,
                        candidate.DetectedPlatform);

                    preparedCandidates.Add(new PreparedIntakeCandidate(candidate, advisory));
                    progress.AcceptedCount = preparedCandidates.Count;
                }

                progress.PhaseKey = "LocQueueAdd_ScanningFiles";
                await UpdateQueueAddProgressAsync(dispatcher, intakeUiVersion, progress, force: prepared.Candidates.Count > 0).ConfigureAwait(false);

            }


            if (preparedCandidates.Count == 0)
            {
                await dispatcher.InvokeAsync(() =>
                {
                    string footer = _session.QueueRows.Count == 0
                        ? MainWindowMessages.NothingNewAdded
                        : MainWindowMessages.NoSupportedFiles;

                    _session.SetFooterStatus(footer);
                    _session.UpdateUiState();
                }, DispatcherPriority.Background);

                await EndIntakeOperationAsync(dispatcher, intakeUiVersion).ConfigureAwait(false);
                intakeUiEnded = true;
                await ShowIntakeResultAsync(
                    dispatcher,
                    actualAddedDelta: 0,
                    skippedCorruptArchives: progress.SkippedCorruptArchives || rejectedArchiveMessageKeys.Any(IsCorruptOrUnreadableArchiveMessageKey),
                    skippedUnsupportedOrDuplicate: progress.SkippedUnsupportedOrDuplicate || unsupportedFileCount > 0 || duplicateFileCount > 0 || missingPathCount > 0,
                    wasCancelled: false).ConfigureAwait(false);
                return Array.Empty<Guid>();
            }

            int addedTotal = 0;
            var addedIds = new List<Guid>(preparedCandidates.Count);

            IReadOnlyList<PreparedQueueCandidate> consensusCandidates = ApplySiblingPlatformConsensus(
                preparedCandidates.Select(static item => item.Candidate).ToList());
            Dictionary<string, QueueIntakeAdvisory?> advisoryByPath = BuildPreparedAdvisoryMap(preparedCandidates);

            await dispatcher.InvokeAsync(
                () =>
                {
                    HashSet<string> currentExistingPaths = BuildExistingPathSet(_session.QueueRows);

                    foreach (PreparedQueueCandidate candidate in consensusCandidates)
                    {
                        intakeToken.ThrowIfCancellationRequested();

                        string normalizedCandidatePath = NormalizePathForAdvisoryKey(candidate.Path);
                        if (currentExistingPaths.Contains(normalizedCandidatePath) || !IsExistingQueueInputPath(candidate.Path))
                        {
                            continue;
                        }

                        QueueRowData row = BuildRowFromPath(
                            candidate.Path,
                            candidate.Action,
                            candidate.DetectedPlatform,
                            candidate.DetectionReason,
                            executionProfile,
                            intakeSource,
                            intakeAdvisory: TryGetPreparedAdvisory(advisoryByPath, candidate.Path));

                        _session.QueueRows.Append(row);
                        currentExistingPaths.Add(normalizedCandidatePath);
                        addedIds.Add(row.ItemId);
                        addedTotal++;
                    }

                    _session.RequestSelectFirstQueueRowIfNone();
                    _session.UpdateUiState();
                },
                DispatcherPriority.Background);

            int queueCountAfter = await dispatcher.InvokeAsync(
                () => _session.QueueRows.Count,
                DispatcherPriority.Background);
            int actualAddedDelta = Math.Max(0, queueCountAfter - queueCountBefore);

            Log.ForContext<MainWindowViewModel>().Debug(
                "Intake enqueue completed. QueueBefore={QueueBefore}, QueueAfter={QueueAfter}, ActualAddedDelta={ActualAddedDelta}",
                queueCountBefore,
                queueCountAfter,
                actualAddedDelta);

            await dispatcher.InvokeAsync(
                () =>
                {
                    string footer;

                    if (actualAddedDelta == 0)
                    {
                        footer = ArabicUi.Get("LocQueueActivity_AddSkippedTitle");

                        _session.SetFooterStatus(footer);
                    }
                    else if (acceptedArchiveTotal > 0)
                    {
                        string addedText = actualAddedDelta == 1
                            ? ArabicUi.Get(MainWindowMessages.AddedOne)
                            : ArabicUi.Format(MainWindowMessages.Fmt_AddedMany, actualAddedDelta);

                        footer = addedText + " " + ArabicUi.Get(MainWindowMessages.ArchiveWillUnpackThenConvertFooter);
                        _session.SetFooterStatus(footer);
                    }
                    else if (actualAddedDelta == 1)
                    {
                        footer = ArabicUi.Get(MainWindowMessages.AddedOne);
                        _session.SetFooterStatus(MainWindowMessages.AddedOne);
                    }
                    else
                    {
                        footer = ArabicUi.Format(MainWindowMessages.Fmt_AddedMany, actualAddedDelta);
                        _session.SetFooterStatus(footer);
                    }

                    _session.UpdateUiState();
                },
                DispatcherPriority.Background);

            await EndIntakeOperationAsync(dispatcher, intakeUiVersion).ConfigureAwait(false);
            intakeUiEnded = true;
            await ShowIntakeResultAsync(
                dispatcher,
                actualAddedDelta,
                progress.SkippedCorruptArchives || rejectedArchiveMessageKeys.Any(IsCorruptOrUnreadableArchiveMessageKey),
                progress.SkippedUnsupportedOrDuplicate || unsupportedFileCount > 0 || duplicateFileCount > 0 || missingPathCount > 0,
                wasCancelled: false).ConfigureAwait(false);

            return addedIds;
        }
        catch (OperationCanceledException ex)
        {
            Log.ForContext<MainWindowViewModel>().Debug(ex, "Add paths cancelled.");
            if (intakeUiVersion != 0)
            {
                await EndIntakeOperationAsync(dispatcher, intakeUiVersion).ConfigureAwait(false);
                intakeUiEnded = true;
            }

            await CompleteAddPathsCancellationAsync(dispatcher).ConfigureAwait(false);
            await ShowIntakeResultAsync(
                dispatcher,
                actualAddedDelta: 0,
                skippedCorruptArchives: false,
                skippedUnsupportedOrDuplicate: false,
                wasCancelled: true).ConfigureAwait(false);
            return Array.Empty<Guid>();
        }
        catch (Exception ex)
        {
            Log.ForContext<MainWindowViewModel>().Debug(ex, "Add paths failed.");

            await dispatcher.InvokeAsync(() =>
            {
                string body = ArabicUi.Get(MainWindowMessages.AddFilesErrorBody);
                _session.SetFooterStatus(body);
                _session.UpdateUiState();
                _session.ShowError(MainWindowMessages.AddFilesErrorTitle, body);
            }, DispatcherPriority.Normal);

            return Array.Empty<Guid>();
        }
        finally
        {
            try
            {
                if (ReferenceEquals(_intakeCancellationCts, intakeCancellationCts))
                {
                    _intakeCancellationCts = null;
                }

                IsAddingFiles = false;

                if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished
                    && !intakeUiEnded
                    && intakeUiVersion != 0)
                {
                    await EndIntakeOperationAsync(dispatcher, intakeUiVersion).ConfigureAwait(false);
                }
            }
            catch (TaskCanceledException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }
    }


    private static HashSet<string> BuildDirectRawFilePathSet(IEnumerable<string> rawList)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in rawList)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    set.Add(NormalizePathForAdvisoryKey(path));
                }
            }
            catch (Exception ex) when (IsExpectedPathNormalizationException(ex))
            {
            }
        }

        return set;
    }

    private static bool ShouldCreateFailedIntakeRow(
        string path,
        IReadOnlySet<string> directRawFilePaths)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string normalizedPath = NormalizePathForAdvisoryKey(path);
        return directRawFilePaths.Contains(normalizedPath);
    }

    private static Dictionary<string, QueueIntakeAdvisory?> BuildPreparedAdvisoryMap(
        IEnumerable<PreparedIntakeCandidate> preparedCandidates)
    {
        ArgumentNullException.ThrowIfNull(preparedCandidates);

        var map = new Dictionary<string, QueueIntakeAdvisory?>(StringComparer.OrdinalIgnoreCase);
        foreach (PreparedIntakeCandidate item in preparedCandidates)
        {
            string key = NormalizePathForAdvisoryKey(item.Candidate.Path);
            if (key.Length == 0 || map.ContainsKey(key))
            {
                continue;
            }

            map.Add(key, item.Advisory);
        }

        return map;
    }

    private static QueueIntakeAdvisory? TryGetPreparedAdvisory(
        IReadOnlyDictionary<string, QueueIntakeAdvisory?> advisories,
        string path)
    {
        ArgumentNullException.ThrowIfNull(advisories);

        string key = NormalizePathForAdvisoryKey(path);
        return advisories.TryGetValue(key, out QueueIntakeAdvisory? advisory)
            ? advisory
            : null;
    }

    private static bool TryAddUnsupportedIntakeCandidate(
        string path,
        string reasonKey,
        HashSet<string> existingPaths,
        HashSet<string> seenImportedPaths,
        List<PreparedIntakeCandidate> preparedCandidates,
        ref int duplicateFileCount)
    {
        string normalizedPath = NormalizePathForAdvisoryKey(path);
        if (existingPaths.Contains(normalizedPath) || !seenImportedPaths.Add(normalizedPath))
        {
            duplicateFileCount++;
            return false;
        }

        QueuePlatformView platform = BuildQueuePlatformView(path);
        string detail = string.IsNullOrWhiteSpace(reasonKey)
            ? MainWindowMessages.UnsupportedQueueFile
            : reasonKey;

        preparedCandidates.Add(new PreparedIntakeCandidate(
            new PreparedQueueCandidate(
                path,
                TaskActionCodes.Unsupported,
                platform.PlatformName,
                detail),
            Advisory: null));

        return true;
    }

    private static IReadOnlyList<PreparedQueueCandidate> ApplySiblingPlatformConsensus(
        IReadOnlyList<PreparedQueueCandidate> candidates)
    {
        if (candidates.Count < 2)
        {
            return candidates;
        }

        PreparedQueueCandidate[] result = candidates.ToArray();
        Dictionary<string, List<int>> groups = [];

        for (int index = 0; index < result.Length; index++)
        {
            if (!TryBuildSiblingConsensusKey(result[index].Path, out string key))
            {
                continue;
            }

            if (!groups.TryGetValue(key, out List<int>? indexes))
            {
                indexes = [];
                groups[key] = indexes;
            }

            indexes.Add(index);
        }

        foreach (List<int> indexes in groups.Values)
        {
            if (indexes.Count < 2)
            {
                continue;
            }

            string[] platforms =
            [
                .. indexes
                    .Select(index => result[index].DetectedPlatform)
                    .Where(PlatformDetectionService.IsOrganizablePlatformName)
                    .Select(static platform => platform.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            ];

            if (platforms.Length == 0)
            {
                continue;
            }

            if (platforms.Length > 1)
            {
                foreach (int index in indexes)
                {
                    result[index] = result[index] with
                    {
                        DetectedPlatform = string.Empty,
                        DetectionReason = "LocPlatformDetect_CueMultiTrackAmbiguous"
                    };
                }

                continue;
            }

            string consensusPlatform = platforms[0];
            foreach (int index in indexes)
            {
                if (PlatformDetectionService.IsOrganizablePlatformName(result[index].DetectedPlatform))
                {
                    continue;
                }

                result[index] = result[index] with
                {
                    DetectedPlatform = consensusPlatform,
                    DetectionReason = "LocPlatformDetect_CueMultiTrackPathHint"
                };
            }
        }

        return result;
    }

    private static bool TryBuildSiblingConsensusKey(string path, out string key)
    {
        key = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string directory;
        string name;

        try
        {
            directory = Path.GetFullPath(Path.GetDirectoryName(path) ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            name = Path.GetFileNameWithoutExtension(path);
        }
        catch (Exception ex) when (IsExpectedPathNormalizationException(ex))
        {
            return false;
        }

        if (!TryExtractDiscNumber(name, out _))
        {
            return false;
        }

        string normalizedTitle = NormalizeSiblingTitle(RemoveDiscToken(name));
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return false;
        }

        string region = NamingCorrectionEngine.TryExtractRegion(path, out string extractedRegion)
            ? extractedRegion.Trim().ToUpperInvariant()
            : "-";

        key = string.Join("|", directory.ToUpperInvariant(), normalizedTitle, region);
        return true;
    }

    private static bool TryExtractDiscNumber(string value, out int discNumber)
    {
        discNumber = 0;
        Match match = DiscNumberRegex().Match(value ?? string.Empty);
        return match.Success
            && int.TryParse(match.Groups["disc"].Value, out discNumber)
            && discNumber > 0;
    }

    private static string RemoveDiscToken(string value) =>
        DiscNumberRegex().Replace(value ?? string.Empty, " ");

    private static string NormalizeSiblingTitle(string value)
    {
        string normalized = SiblingSeparatorRegex()
            .Replace(value ?? string.Empty, " ")
            .Trim()
            .ToUpperInvariant();

        return normalized;
    }

    [GeneratedRegex(
        @"(?:^|[\s._\-\(\[])(?:disc|disk|cd)[\s._\-]*(?<disc>\d{1,2})(?:\s*of\s*(?<total>\d{1,2}))?(?:[\s._\-\)\]]|$)",
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant)]
    private static partial Regex DiscNumberRegex();

    [GeneratedRegex(
        @"[\s._\-]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex SiblingSeparatorRegex();

    private static QueuePlatformView BuildQueuePlatformView(string path)
    {
        try
        {
            PlatformDetectionResult detection = PlatformDetectionService.Detect(path);
            if (PlatformDetectionService.IsActionablePlatformName(detection.PlatformName))
            {
                return new QueuePlatformView(detection.PlatformName, detection.Reason);
            }
        }
        catch (Exception ex) when (IsExpectedPathNormalizationException(ex))
        {
        }

        return new QueuePlatformView("Unknown Platform", string.Empty);
    }

    private static bool IsExistingQueueInputPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return File.Exists(path);
    }

    private static string NormalizePathForAdvisoryKey(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (IsExpectedPathNormalizationException(ex))
        {
            return path.Trim()
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static bool IsExpectedPathNormalizationException(Exception ex)
    {
        return ex is ArgumentException
            or IOException
            or NotSupportedException
            or PathTooLongException;
    }

    private static QueueIntakeAdvisory? TryGetAdvisoryForPath(
        IReadOnlyDictionary<string, QueueIntakeAdvisory> advisories,
        string path)
    {
        ArgumentNullException.ThrowIfNull(advisories);

        string key = NormalizePathForAdvisoryKey(path);

        return advisories.TryGetValue(key, out QueueIntakeAdvisory? advisory)
            ? advisory
            : null;
    }

    private static string ResolveRequestedAction(string path, QueueExecutionProfile executionProfile)
    {
        return QueueModeResolver.ResolveInitialRequestedAction(path, executionProfile);
    }

    private QueueRowData BuildRowFromPath(
        string path,
        string action,
        string detectedPlatform,
        string detectionReason,
        QueueExecutionProfile executionProfile,
        QueueIntakeSource intakeSource,
        QueueIntakeAdvisory? intakeAdvisory = null)
    {
        QueueIntakeAdvisory? resolvedIntakeAdvisory = FormatSafetyAdvisorStatic.BuildQueueAdvisory(
            path,
            detectedPlatform,
            intakeAdvisory);

        (bool isCompliant, string suggestedName) = executionProfile == QueueExecutionProfile.Standard
            ? _session.AnalyzeNamingForPath(path)
            : (true, string.Empty);

        string initialState = action switch
        {
            TaskActionCodes.PendingSelection => TaskQueueStateCodes.AwaitingOperationSelection,
            TaskActionCodes.Unsupported => TaskQueueStateCodes.Failed,
            _ => TaskQueueStateCodes.Pending
        };

        string initialDetail = action switch
        {
            TaskActionCodes.PendingSelection => MainWindowMessages.ChooseOperationForItem,
            TaskActionCodes.Unsupported => string.IsNullOrWhiteSpace(detectionReason)
                ? MainWindowMessages.UnsupportedQueueFile
                : detectionReason,
            TaskActionCodes.StageArchiveForConversion when !ArchivePreviewIntakePolicy.AllowsArchivePreview(intakeSource) => MainWindowMessages.ArchiveAwaitingPreviewAtStartup,
            TaskActionCodes.StageArchiveForConversion => MainWindowMessages.ArchiveWillUnpackThenConvertDetail,
            _ => MainWindowMessages.ReadyForProcessing
        };

        return new QueueRowData
        {
            ItemId = Guid.NewGuid(),
            OriginalPath = path,
            SourcePath = path,
            InputType = ResolveInputTypeDisplay(path),
            FileName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            DetectedPlatform = string.IsNullOrWhiteSpace(detectedPlatform)
                ? ArabicUi.Get("LocCommon_Unknown")
                : detectedPlatform,
            DetectionReason = detectionReason ?? string.Empty,
            RequestedAction = action,
            ExecutionProfile = executionProfile,
            IntakeSource = intakeSource,
            IntakeAdvisory = resolvedIntakeAdvisory,
            CurrentState = initialState,
            StatusDetail = initialDetail,
            IsNamingCompliant = isCompliant,
            SuggestedStandardName = suggestedName,
            IsVisibleInCurrentOperationMode = string.Equals(action, TaskActionCodes.Unsupported, StringComparison.Ordinal)
                || QueueModeResolver.IsPathVisibleForExecutionProfile(path, executionProfile)
        };
    }

    private static string ResolveInputTypeDisplay(string path)
    {

        string extension = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        return string.IsNullOrWhiteSpace(extension) ? "FOLDER" : extension;
    }

    private static HashSet<string> BuildExistingPathSet(QueueRowStore store)
    {
        IReadOnlyList<QueueRowData> rows = store.Rows;
        var set = new HashSet<string>(rows.Count, StringComparer.OrdinalIgnoreCase);

        foreach (QueueRowData row in rows)
        {
            set.Add(NormalizePathForAdvisoryKey(row.OriginalPath));
        }

        return set;
    }

    [RelayCommand(CanExecute = nameof(CanAcceptQueueInput))]
    private async Task SelectFiles()
    {
        await _coordinator.SelectFilesAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void ClearQueue()
    {
        if (_session.IsQueueInteractionLocked || _session.QueueRows.Count == 0)
        {
            return;
        }

        var dialog = new ClearTaskLogConfirmationDialog
        {
            Owner = _session.Owner
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _session.QueueRows.Clear();
        _session.OnQueueClearedUi();
        _session.UpdateUiState();
    }
}
