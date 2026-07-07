using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Ui.Queue;
using HakamiqChdTool.App.ViewModels.Virtualization;
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
            if (!TryNormalizeFastDirectExistingFilePath(rawPath, out string normalizedRawPath))
            {
                return false;
            }

            var mediaDecision = global::HakamiqChdTool.App.Services.MediaInputPolicy.MediaInputPolicy.Evaluate(normalizedRawPath);
            if (mediaDecision.IsBlocked)
            {
                return false;
            }

            if (!TryNormalizeFastDirectExistingFilePath(mediaDecision.EffectivePath, out string effectivePath))
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
            IsVisibleInCurrentOperationMode = string.Equals(action, TaskActionCodes.Unsupported, StringComparison.Ordinal)
                || QueueModeResolver.IsPathVisibleForExecutionProfile(path, executionProfile)
        };
    }

    private static bool TryNormalizeFastDirectExistingFilePath(string path, out string normalizedPath)
    {
        normalizedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path.Trim());

            if (!File.Exists(fullPath)
                || HasFastDirectReparsePointInExistingPathFromVolumeRoot(fullPath))
            {
                return false;
            }

            ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(path));

            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (IsExpectedFastDirectPathException(ex))
        {
            return false;
        }
    }

    private static bool HasFastDirectReparsePointInExistingPathFromVolumeRoot(string candidatePath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string? root = Path.GetPathRoot(candidate);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return HasFastDirectReparsePointInExistingPath(candidate, root);
        }
        catch (Exception ex) when (IsExpectedFastDirectPathException(ex))
        {
            return true;
        }
    }

    private static bool HasFastDirectReparsePointInExistingPath(string candidatePath, string rootPath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string root = Path.GetFullPath(rootPath);

            if (!IsFastDirectSamePathOrChild(root, candidate))
            {
                return true;
            }

            string current = candidate;

            while (true)
            {
                if ((File.Exists(current) || Directory.Exists(current)) && IsFastDirectReparsePoint(current))
                {
                    return true;
                }

                if (FastDirectPathsEqual(current, root))
                {
                    return false;
                }

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent) || FastDirectPathsEqual(parent, current))
                {
                    return true;
                }

                current = parent;
            }
        }
        catch (Exception ex) when (IsExpectedFastDirectPathException(ex))
        {
            return true;
        }
    }

    private static bool IsFastDirectReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (IsExpectedFastDirectPathException(ex))
        {
            return true;
        }
    }

    private static bool IsFastDirectSamePathOrChild(string rootPath, string candidatePath)
    {
        string root = TrimFastDirectDirectorySeparators(Path.GetFullPath(rootPath));
        string candidate = TrimFastDirectDirectorySeparators(Path.GetFullPath(candidatePath));

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(EnsureFastDirectDirectorySeparatorSuffix(root), StringComparison.OrdinalIgnoreCase);
    }

    private static bool FastDirectPathsEqual(string left, string right)
    {
        return string.Equals(
            TrimFastDirectDirectorySeparators(Path.GetFullPath(left)),
            TrimFastDirectDirectorySeparators(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureFastDirectDirectorySeparatorSuffix(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string TrimFastDirectDirectorySeparators(string path)
    {
        string? root = Path.GetPathRoot(path);

        if (!string.IsNullOrWhiteSpace(root)
            && path.Length <= root.Length)
        {
            return root;
        }

        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.IsNullOrEmpty(trimmed) && !string.IsNullOrWhiteSpace(root)
            ? root
            : trimmed;
    }

    private static bool IsExpectedFastDirectPathException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException;
    }
}