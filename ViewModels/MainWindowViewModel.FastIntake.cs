using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.ViewModels.Virtualization;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace HakamiqChdTool.App.ViewModels;

public partial class MainWindowViewModel
{
    private bool TryBuildFastDirectFileCandidates(
        IReadOnlyList<string> rawList,
        QueueIngestKind inputKind,
        QueueExecutionProfile executionProfile,
        out IReadOnlyList<PreparedIntakeCandidate> candidates)
    {
        candidates = Array.Empty<PreparedIntakeCandidate>();

        if (rawList.Count == 0)
        {
            return false;
        }

        var prepared = new List<PreparedIntakeCandidate>(rawList.Count);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string rawPath in rawList)
        {
            if (!System.IO.File.Exists(rawPath))
            {
                return false;
            }

            var mediaDecision = global::HakamiqChdTool.App.Services.MediaInputPolicy.MediaInputPolicy.Evaluate(rawPath);
            if (mediaDecision.IsBlocked)
            {
                return false;
            }

            string effectivePath = mediaDecision.EffectivePath;
            if (!System.IO.File.Exists(effectivePath))
            {
                return false;
            }

            var classification = QueueInputClassifier.Classify(effectivePath);
            if (!classification.IsSupported || classification.IsArchiveContainer)
            {
                return false;
            }

            string normalizedPath = NormalizePathForAdvisoryKey(effectivePath);
            if (!seenPaths.Add(normalizedPath))
            {
                continue;
            }

            string action = ResolveRequestedAction(effectivePath, executionProfile);
            if (string.Equals(action, TaskActionCodes.Unsupported, StringComparison.Ordinal))
            {
                return false;
            }

            prepared.Add(new PreparedIntakeCandidate(
                new PreparedQueueCandidate(
                    effectivePath,
                    action,
                    "Unknown Platform",
                    string.Empty),
                null));
        }

        if (prepared.Count == 0)
        {
            return false;
        }

        candidates = prepared;
        return true;
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

                    QueueRowData row = BuildRowFromPath(
                        candidate.Path,
                        candidate.Action,
                        candidate.DetectedPlatform,
                        candidate.DetectionReason,
                        executionProfile,
                        intakeSource,
                        prepared.Advisory);

                    _session.QueueRows.Append(row);
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
}
