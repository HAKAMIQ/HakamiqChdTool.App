using HakamiqChdTool.App.Core.Session;
using HakamiqChdTool.App.Core.Workflow.Paths;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Ui.Queue;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Features;
using HakamiqChdTool.App.Services.M3u;
using HakamiqChdTool.App.Services.StorageAdvisor;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using HakamiqChdTool.App.Views;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private readonly StorageAdvisorService _storageAdvisorService = new();


    private bool ConfirmStorageAdvisorBeforeProcessing(
        IReadOnlyList<TaskQueueItemViewModel> items,
        bool processedSelectionOnly)
    {
        ArgumentNullException.ThrowIfNull(items);
        _ = processedSelectionOnly;

        if (items.Count == 0 || _settings.SuppressStorageAdvisorDialog)
        {
            return true;
        }

        try
        {
            foreach (TaskQueueItemViewModel item in items)
            {
                if (!TryBuildStorageAdvisorRequest(item, out StorageAdvisorRequest? request) ||
                    request is null)
                {
                    continue;
                }

                StorageAdvisorResult result = _storageAdvisorService.Analyze(
                    request,
                    _settings.SuppressStorageAdvisorDialog);

                if (!result.ShouldShowDialog)
                {
                    continue;
                }

                StorageAdvisorDialogResult dialogResult = ShowStorageAdvisorDialog(result);
                return HandleStorageAdvisorDialogResult(dialogResult);
            }

            return true;
        }
        catch (Exception ex) when (IsExpectedStorageAdvisorFailure(ex))
        {
            Log.Warning(ex, "Storage Advisor pre-processing check failed. Processing will continue.");
            return true;
        }
    }


    private bool TryBuildStorageAdvisorRequest(
        TaskQueueItemViewModel item,
        out StorageAdvisorRequest? request)
    {
        ArgumentNullException.ThrowIfNull(item);

        request = null;

        if (string.IsNullOrWhiteSpace(item.SourcePath))
        {
            return false;
        }

        string sourcePath = item.SourcePath.Trim();
        StorageAdvisorOperationKind operationKind = ResolveStorageAdvisorOperationKind(item);

        if (operationKind is StorageAdvisorOperationKind.Unknown or StorageAdvisorOperationKind.Verification)
        {
            return false;
        }

        string outputDirectoryPath = ResolveStorageAdvisorOutputDirectory(sourcePath);
        string pendingWorkspaceRoot = ResolveStorageAdvisorPendingWorkspaceRoot(
            sourcePath,
            operationKind);

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
        if (_settings.UseCustomOutputRoot && !string.IsNullOrWhiteSpace(_settings.CustomOutputRoot))
        {
            return _settings.CustomOutputRoot.Trim();
        }

        return ResolveExistingOrParentDirectory(sourcePath);
    }


    private string ResolveStorageAdvisorPendingWorkspaceRoot(
        string sourcePath,
        StorageAdvisorOperationKind operationKind)
    {
        if (_settings.UseCustomPendingWorkspace &&
            !string.IsNullOrWhiteSpace(_settings.PendingWorkspaceCustomRoot))
        {
            return _settings.PendingWorkspaceCustomRoot.Trim();
        }

        if (operationKind == StorageAdvisorOperationKind.BinCueRescue)
        {
            return ResolveExistingOrParentDirectory(sourcePath);
        }

        string outputDirectory = ResolveStorageAdvisorOutputDirectory(sourcePath);
        return PendingWorkspacePathPolicy.ResolvePendingWorkspaceRoot(
            outputDirectory,
            _settings);
    }


    private bool HandleStorageAdvisorDialogResult(StorageAdvisorDialogResult result)
    {
        if (result == StorageAdvisorDialogResult.ContinueRecommended)
        {
            return true;
        }

        if (result == StorageAdvisorDialogResult.OpenOptions)
        {
            OpenOptionsDialog();
            return false;
        }

        return false;
    }


    private static bool IsBinCueRescueCandidate(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetExtension(sourcePath.Trim()),
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
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            string fullPath = Path.GetFullPath(path.Trim());

            if (Directory.Exists(fullPath))
            {
                return fullPath;
            }

            return Path.GetDirectoryName(fullPath) ?? string.Empty;
        }
        catch (Exception ex) when (IsExpectedStorageAdvisorFailure(ex))
        {
            return string.Empty;
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
            or System.Security.SecurityException;
    }
}
