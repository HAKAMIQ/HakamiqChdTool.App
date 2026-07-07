using System;
using System.Collections.Generic;
using System.IO;
using System.Security;

using HakamiqChdTool.App.Core.Workflow.Paths;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.StorageAdvisor;
using HakamiqChdTool.App.Ui.Queue;
using HakamiqChdTool.App.ViewModels;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private readonly StorageAdvisorService _storageAdvisorService = new();

    private bool ConfirmStorageAdvisorBeforeProcessing(
        IReadOnlyList<TaskQueueItemViewModel> items,
        bool processedSelectionOnly)
    {
        _ = items;
        _ = processedSelectionOnly;

        return true;
    }

    private bool TryBuildStorageAdvisorRequest(
        TaskQueueItemViewModel item,
        out StorageAdvisorRequest? request)
    {
        ArgumentNullException.ThrowIfNull(item);

        request = null;

        if (!TryNormalizeExistingStorageAdvisorPath(
            item.SourcePath,
            out string sourcePath,
            out _))
        {
            return false;
        }

        StorageAdvisorOperationKind operationKind = ResolveStorageAdvisorOperationKind(item);

        if (operationKind is StorageAdvisorOperationKind.Unknown or StorageAdvisorOperationKind.Verification)
        {
            return false;
        }

        string outputDirectoryPath = ResolveStorageAdvisorOutputDirectory(sourcePath);
        if (string.IsNullOrWhiteSpace(outputDirectoryPath))
        {
            return false;
        }

        string pendingWorkspaceRoot = ResolveStorageAdvisorPendingWorkspaceRoot(
            sourcePath,
            operationKind);

        if (string.IsNullOrWhiteSpace(pendingWorkspaceRoot))
        {
            return false;
        }

        bool usesCustomPendingWorkspace = _settings.UseCustomPendingWorkspace &&
            !string.IsNullOrWhiteSpace(_settings.PendingWorkspaceCustomRoot);

        request = new StorageAdvisorRequest(
            operationKind,
            sourcePath,
            outputDirectoryPath,
            pendingWorkspaceRoot,
            usesCustomPendingWorkspace);

        return true;
    }

    private static StorageAdvisorOperationKind ResolveStorageAdvisorOperationKind(
        TaskQueueItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        string mode = QueueModeResolver.QueueModeFromRequestedAction(item.RequestedAction);
        if (string.Equals(mode, "Extract", StringComparison.OrdinalIgnoreCase))
        {
            return StorageAdvisorOperationKind.Extraction;
        }

        if (!string.Equals(mode, "Convert", StringComparison.OrdinalIgnoreCase))
        {
            return StorageAdvisorOperationKind.Unknown;
        }

        return IsBinCueRescueCandidate(item.SourcePath)
            ? StorageAdvisorOperationKind.BinCueRescue
            : StorageAdvisorOperationKind.StandardConversion;
    }

    private string ResolveStorageAdvisorOutputDirectory(string sourcePath)
    {
        if (_settings.UseCustomOutputRoot &&
            !string.IsNullOrWhiteSpace(_settings.CustomOutputRoot) &&
            TryNormalizeStorageAdvisorDirectory(
                _settings.CustomOutputRoot,
                allowMissingLeaf: true,
                out string customOutputRoot))
        {
            return customOutputRoot;
        }

        return ResolveExistingOrParentDirectory(sourcePath);
    }

    private string ResolveStorageAdvisorPendingWorkspaceRoot(
        string sourcePath,
        StorageAdvisorOperationKind operationKind)
    {
        if (_settings.UseCustomPendingWorkspace &&
            !string.IsNullOrWhiteSpace(_settings.PendingWorkspaceCustomRoot) &&
            TryNormalizeStorageAdvisorDirectory(
                _settings.PendingWorkspaceCustomRoot,
                allowMissingLeaf: true,
                out string customPendingRoot))
        {
            return customPendingRoot;
        }

        if (operationKind == StorageAdvisorOperationKind.BinCueRescue)
        {
            return ResolveExistingOrParentDirectory(sourcePath);
        }

        string outputDirectory = ResolveStorageAdvisorOutputDirectory(sourcePath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return string.Empty;
        }

        try
        {
            string pendingRoot = PendingWorkspacePathPolicy.ResolvePendingWorkspaceRoot(
                outputDirectory,
                _settings);

            return TryNormalizeStorageAdvisorDirectory(
                pendingRoot,
                allowMissingLeaf: true,
                out string normalizedPendingRoot)
                ? normalizedPendingRoot
                : string.Empty;
        }
        catch (Exception ex) when (IsExpectedStorageAdvisorFailure(ex))
        {
            return string.Empty;
        }
    }

    private static bool IsBinCueRescueCandidate(string? sourcePath)
    {
        if (!TryNormalizeStorageAdvisorCandidatePath(sourcePath, out string normalizedPath))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetExtension(normalizedPath),
                ".bin",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsExpectedStorageAdvisorFailure(ex))
        {
            return false;
        }
    }

    private static string ResolveExistingOrParentDirectory(string path)
    {
        if (!TryNormalizeExistingStorageAdvisorPath(
            path,
            out string fullPath,
            out FileAttributes attributes))
        {
            return string.Empty;
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            return fullPath;
        }

        string? parentDirectory;

        try
        {
            parentDirectory = Path.GetDirectoryName(fullPath);
        }
        catch (Exception ex) when (IsExpectedStorageAdvisorFailure(ex))
        {
            return string.Empty;
        }

        return TryNormalizeStorageAdvisorDirectory(
            parentDirectory,
            allowMissingLeaf: false,
            out string normalizedParent)
            ? normalizedParent
            : string.Empty;
    }

    private static bool TryNormalizeExistingStorageAdvisorPath(
        string? path,
        out string normalizedPath,
        out FileAttributes attributes)
    {
        normalizedPath = string.Empty;
        attributes = default;

        if (!TryNormalizeStorageAdvisorCandidatePath(path, out string fullPath))
        {
            return false;
        }

        if (!TryGetStorageAdvisorAttributes(fullPath, out attributes))
        {
            return false;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        normalizedPath = fullPath;
        return true;
    }

    private static bool TryNormalizeStorageAdvisorDirectory(
        string? path,
        bool allowMissingLeaf,
        out string normalizedPath)
    {
        normalizedPath = string.Empty;

        if (!TryNormalizeStorageAdvisorCandidatePath(path, out string fullPath))
        {
            return false;
        }

        if (TryGetStorageAdvisorAttributes(fullPath, out FileAttributes attributes))
        {
            if ((attributes & FileAttributes.Directory) == 0 ||
                (attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            normalizedPath = fullPath;
            return true;
        }

        if (!allowMissingLeaf)
        {
            return false;
        }

        string? parentDirectory;

        try
        {
            parentDirectory = Path.GetDirectoryName(fullPath);
        }
        catch (Exception ex) when (IsExpectedStorageAdvisorFailure(ex))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            return false;
        }

        if (!TryNormalizeStorageAdvisorDirectory(
            parentDirectory,
            allowMissingLeaf: false,
            out _))
        {
            return false;
        }

        normalizedPath = fullPath;
        return true;
    }

    private static bool TryNormalizeStorageAdvisorCandidatePath(
        string? path,
        out string normalizedPath)
    {
        normalizedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path.Trim());
            ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(path));

            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (IsExpectedStorageAdvisorFailure(ex))
        {
            return false;
        }
    }

    private static bool TryGetStorageAdvisorAttributes(
        string path,
        out FileAttributes attributes)
    {
        attributes = default;

        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (Exception ex) when (IsExpectedStorageAdvisorFailure(ex))
        {
            return false;
        }
    }

    private static bool IsExpectedStorageAdvisorFailure(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or InvalidOperationException
            or SecurityException;
    }
}