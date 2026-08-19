using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Ui.Queue;
using HakamiqChdTool.App.ViewModels.Virtualization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace HakamiqChdTool.App.ViewModels;

public partial class MainWindowViewModel
{
    private async ValueTask<(bool Success, IReadOnlyList<PreparedIntakeCandidate> Candidates)> TryBuildFastDirectFileCandidatesAsync(
        IReadOnlyList<string> rawList,
        QueueIngestKind inputKind,
        QueueExecutionProfile executionProfile,
        CancellationToken cancellationToken)
    {
        _ = inputKind;

        if (rawList.Count == 0)
        {
            return (false, Array.Empty<PreparedIntakeCandidate>());
        }

        var prepared = new List<PreparedIntakeCandidate>(rawList.Count);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string rawPath in rawList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            MediaInputPipelineDecision rawDecision = await MediaInputPipelineStatic
                .DecideAsync(rawPath, cancellationToken)
                .ConfigureAwait(false);

            MediaInputDescriptor rawDescriptor = rawDecision.Descriptor;
            if (!rawDescriptor.IsFile
                || string.IsNullOrWhiteSpace(rawDescriptor.FullPath)
                || !TryNormalizeExistingFilePathForIntake(
                    rawDescriptor.FullPath,
                    out string normalizedRawPath))
            {
                return (false, Array.Empty<PreparedIntakeCandidate>());
            }

            MediaInputPipelineDecision effectiveDecision = rawDecision;
            string effectivePath = normalizedRawPath;

            if (rawDecision.RequiresStandaloneBinPolicy)
            {
                var mediaDecision =
                    global::HakamiqChdTool.App.Services.MediaInputPolicy.MediaInputPolicy.Evaluate(
                        normalizedRawPath);

                if (mediaDecision.IsBlocked
                    || !TryNormalizeExistingFilePathForIntake(
                        mediaDecision.EffectivePath,
                        out effectivePath))
                {
                    return (false, Array.Empty<PreparedIntakeCandidate>());
                }

                if (mediaDecision.IsRedirectedToCue)
                {
                    effectiveDecision = await MediaInputPipelineStatic
                        .DecideAsync(effectivePath, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            MediaInputDescriptor effectiveDescriptor = effectiveDecision.Descriptor;
            if (!effectiveDescriptor.IsFile || effectiveDecision.IsBlocked)
            {
                return (false, Array.Empty<PreparedIntakeCandidate>());
            }

            string normalizedPath = NormalizePathForAdvisoryKey(effectivePath);
            if (!seenPaths.Add(normalizedPath))
            {
                continue;
            }

            QueueInputClassification classification =
                QueueInputClassifier.FromDecision(effectiveDecision);

            if (!classification.IsSupported || classification.IsArchiveContainer)
            {
                return (false, Array.Empty<PreparedIntakeCandidate>());
            }

            string action = QueueOperationModeProjection.ResolveInitialRequestedAction(
                classification,
                executionProfile);

            if (string.Equals(action, TaskActionCodes.Unsupported, StringComparison.Ordinal))
            {
                return (false, Array.Empty<PreparedIntakeCandidate>());
            }

            prepared.Add(new PreparedIntakeCandidate(
                new PreparedQueueCandidate(
                    effectivePath,
                    action,
                    "Unknown Platform",
                    string.Empty,
                    classification),
                null));
        }

        return prepared.Count == 0
            ? (false, Array.Empty<PreparedIntakeCandidate>())
            : (true, prepared);
    }

    private Task<IReadOnlyList<Guid>> AddPreparedCandidatesFastAsync(
        Dispatcher dispatcher,
        IReadOnlyList<PreparedIntakeCandidate> preparedCandidates,
        QueueExecutionProfile executionProfile,
        QueueIntakeSource intakeSource)
    {
        return dispatcher.InvokeAsync<IReadOnlyList<Guid>>(
            () =>
            {
                HashSet<string> currentExistingPaths = BuildExistingPathSet(_session.QueueRows);
                var addedIds = new List<Guid>(preparedCandidates.Count);

                foreach (PreparedIntakeCandidate prepared in preparedCandidates)
                {
                    PreparedQueueCandidate candidate = prepared.Candidate;
                    string normalizedCandidatePath = NormalizePathForAdvisoryKey(candidate.Path);

                    if (currentExistingPaths.Contains(normalizedCandidatePath) || !IsExistingQueueInputPath(candidate.Path))
                    {
                        continue;
                    }

                    QueueRowData row = BuildFastRowFromPath(
                        candidate.Path,
                        candidate.Action,
                        candidate.Classification,
                        executionProfile,
                        intakeSource);

                    _session.QueueRows.Append(row);
                    QueueConsoleIdentityEnrichment(row);
                    currentExistingPaths.Add(normalizedCandidatePath);
                    addedIds.Add(row.ItemId);
                }

                if (addedIds.Count == 0)
                {
                    _session.SetFooterStatus(ArabicUi.Get("LocQueueActivity_AddSkippedTitle"));
                }
                else if (addedIds.Count == 1)
                {
                    _session.SetFooterStatus(MainWindowMessages.AddedOne);
                }
                else
                {
                    _session.SetFooterStatus(ArabicUi.Format(MainWindowMessages.Fmt_AddedMany, addedIds.Count));
                }

                _session.RequestSelectFirstQueueRowIfNone();
                _session.UpdateUiState();

                return addedIds;
            },
            DispatcherPriority.Normal).Task;
    }

    private static QueueRowData BuildFastRowFromPath(
        string path,
        string action,
        QueueInputClassification? classification,
        QueueExecutionProfile executionProfile,
        QueueIntakeSource intakeSource)
    {
        string fileName = Path.GetFileName(path);

        string initialState = action switch
        {
            TaskActionCodes.PendingSelection => TaskQueueStateCodes.AwaitingOperationSelection,
            TaskActionCodes.Unsupported => TaskQueueStateCodes.Failed,
            _ => TaskQueueStateCodes.Pending
        };

        string initialDetail = action switch
        {
            TaskActionCodes.PendingSelection => MainWindowMessages.ChooseOperationForItem,
            TaskActionCodes.Unsupported => MainWindowMessages.UnsupportedQueueFile,
            _ => MainWindowMessages.ReadyForProcessing
        };

        string extension = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();

        bool isVisible = classification.HasValue
            ? QueueModeResolver.IsClassificationVisibleForExecutionProfile(
                classification.Value,
                executionProfile)
            : !string.Equals(action, TaskActionCodes.Unsupported, StringComparison.Ordinal);

        return new QueueRowData
        {
            ItemId = Guid.NewGuid(),
            OriginalPath = path,
            SourcePath = path,
            InputType = string.IsNullOrWhiteSpace(extension) ? "FILE" : extension,
            FileName = string.IsNullOrWhiteSpace(fileName) ? path : fileName,
            DetectedPlatform = "Unknown Platform",
            DetectionReason = string.Empty,
            RequestedAction = action,
            ExecutionProfile = executionProfile,
            IntakeSource = intakeSource,
            IntakeAdvisory = null,
            CurrentState = initialState,
            StatusDetail = initialDetail,
            IsNamingCompliant = true,
            SuggestedStandardName = string.Empty,
            IsVisibleInCurrentOperationMode = isVisible
        };
    }
}