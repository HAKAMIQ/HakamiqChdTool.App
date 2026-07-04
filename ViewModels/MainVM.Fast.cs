using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.ViewModels.Virtualization;
using HakamiqChdTool.App.Ui.Queue;
using System;
using System.Collections.Generic;
using System.IO;
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

            string normalizedPath = NormalizePathForAdvisoryKey(effectivePath);
            if (!seenPaths.Add(normalizedPath))
            {
                continue;
            }

            if (!TryResolveFastKnownDirectFileAction(effectivePath, executionProfile, out string action))
            {
                var classification = QueueInputClassifier.Classify(effectivePath);
                if (!classification.IsSupported || classification.IsArchiveContainer)
                {
                    return false;
                }

                action = ResolveRequestedAction(effectivePath, executionProfile);
                if (string.Equals(action, TaskActionCodes.Unsupported, StringComparison.Ordinal))
                {
                    return false;
                }
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

                    QueueRowData row = BuildFastRowFromPath(
                        candidate.Path,
                        candidate.Action,
                        executionProfile,
                        intakeSource);

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

    private static bool TryResolveFastKnownDirectFileAction(
        string path,
        QueueExecutionProfile executionProfile,
        out string action)
    {
        action = string.Empty;

        string extension = Path.GetExtension(path).ToLowerInvariant();
        QueueOperationMode selectedMode = QueueModeResolver.FromExecutionProfile(executionProfile);

        if (extension is ".iso" or ".cso" or ".cue" or ".gdi" or ".toc" or ".nrg")
        {
            if (selectedMode is QueueOperationMode.None or QueueOperationMode.Convert)
            {
                action = TaskActionCodes.ConvertToChd;
                return true;
            }

            return false;
        }

        if (extension == ".chd")
        {
            if (selectedMode == QueueOperationMode.Extract)
            {
                action = TaskActionCodes.RestoreDiscImageFromChd;
                return true;
            }

            if (selectedMode == QueueOperationMode.Verify)
            {
                action = TaskActionCodes.VerifyChd;
                return true;
            }
        }

        return false;
    }
    private static QueueRowData BuildFastRowFromPath(
        string path,
        string action,
        QueueExecutionProfile executionProfile,
        QueueIntakeSource intakeSource)
    {
        string trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

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

        return new QueueRowData
        {
            ItemId = Guid.NewGuid(),
            OriginalPath = path,
            SourcePath = path,
            InputType = string.IsNullOrWhiteSpace(extension) ? "FILE" : extension,
            FileName = Path.GetFileName(trimmedPath),
            DetectedPlatform = ArabicUi.Get("LocCommon_Unknown"),
            DetectionReason = string.Empty,
            RequestedAction = action,
            ExecutionProfile = executionProfile,
            IntakeSource = intakeSource,
            IntakeAdvisory = null,
            CurrentState = initialState,
            StatusDetail = initialDetail,
            IsNamingCompliant = true,
            SuggestedStandardName = string.Empty,
            IsVisibleInCurrentOperationMode = string.Equals(action, TaskActionCodes.Unsupported, StringComparison.Ordinal)
                || QueueModeResolver.IsPathVisibleForExecutionProfile(path, executionProfile)
        };
    }
}
