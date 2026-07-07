using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Configuration;
using Microsoft.Win32;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Ui.Shell;

internal sealed partial class HqOptionsShell
{
    private const string ExternalToolsStatusAvailableKey = "LocExternalTools_CsoKitStatusAvailable";
    private const string ExternalToolsStatusMissingKey = "LocExternalTools_CsoKitStatusMissing";
    private const string ExternalToolsStatusFailedKey = "LocExternalTools_CsoKitStatusFailed";
    private const string ExternalToolsStatusCheckingKey = "LocExternalTools_CsoKitStatusChecking";
    private const string OpenFolderFailedBodyKey = "LocDialog_OpenFolderFailedBody";
    private const string ValueUnavailableKey = "LocValue_Unavailable";
    private const string SelectCsoKitToolTitleKey = "LocExternalTools_SelectCsoKitToolTitle";
    private const string CsoKitToolFilterKey = "LocExternalTools_CsoKitToolFilter";
    private const string InvalidCsoKitToolSelectionKey = "LocExternalTools_InvalidCsoKitToolSelection";

    public void RecheckExternalTools(object? sender, EventArgs e) => QueueExternalToolsRefresh(showCheckingState: true);

    public void OpenExternalToolsFolder(object? sender, EventArgs e)
    {
        string folderPath = ResolvePreferredToolsFolder();

        try
        {
            folderPath = NormalizeSafeFolderPathForCreate(folderPath);
            Directory.CreateDirectory(folderPath);

            if (HasReparsePointInExistingPathFromVolumeRoot(folderPath))
            {
                throw new InvalidOperationException("The selected tools folder resolves through a reparse point.");
            }
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            Logger.Debug(ex, "Could not create or open Hakamiq CsoKit tools folder. Path={Path}", folderPath);
            ShowNoticeDialog(OperationErrorTitleKey, OpenFolderFailedBodyKey);
            return;
        }

        if (!ExplorerLaunchHelper.TrySelectPathInExplorer(folderPath))
        {
            ShowNoticeDialog(OperationErrorTitleKey, OpenFolderFailedBodyKey);
        }
    }

    public void BrowseCsoKitTool(object? sender, EventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = ResolveUiText(SelectCsoKitToolTitleKey),
            Filter = ResolveUiText(CsoKitToolFilterKey),
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
            FileName = CsoToolLocator.ToolExecutableName
        };

        string currentPath = _currentSettings.ExternalCsoKitPath;
        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            try
            {
                string currentFullPath = Path.GetFullPath(currentPath.Trim());
                string? currentDirectory = Path.GetDirectoryName(currentFullPath);
                if (TryGetSafeExistingDirectory(currentDirectory, out string safeCurrentDirectory))
                {
                    dialog.InitialDirectory = safeCurrentDirectory;
                }
            }
            catch (Exception ex) when (IsExpectedPathException(ex))
            {
                Logger.Debug(ex, "Ignored invalid Hakamiq CsoKit preferred path before browse. Path={Path}", currentPath);
            }
        }

        if (string.IsNullOrWhiteSpace(dialog.InitialDirectory))
        {
            string bundledFolder = new CsoToolLocator(_currentSettings.ExternalCsoKitPath).BundledToolsFolderPath;
            if (TryGetSafeExistingDirectory(bundledFolder, out string safeBundledFolder))
            {
                dialog.InitialDirectory = safeBundledFolder;
            }
        }

        bool? result = dialog.ShowDialog(_owner);
        if (result != true)
        {
            return;
        }

        if (!TryGetSafeExistingCsoToolPath(dialog.FileName, out string selectedPath))
        {
            Logger.Debug("Invalid Hakamiq CsoKit selected path. Path={Path}", dialog.FileName);
            ShowNoticeDialog(OperationErrorTitleKey, InvalidCsoKitToolSelectionKey);
            return;
        }

        _currentSettings.ExternalCsoKitPath = selectedPath;
        _owner.ResultSettings.ExternalCsoKitPath = selectedPath;

        try
        {
            using AppSettingsService settingsService = new();
            settingsService.Save(_currentSettings);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            Logger.Warning(ex, "Failed to persist Hakamiq CsoKit selected path. Path={Path}", selectedPath);
        }

        string unavailableText = ResolveUiText(ValueUnavailableKey);
        _owner.ViewModel.SetCsoKitExternalToolStatus(
            ResolveUiText(ExternalToolsStatusCheckingKey),
            unavailableText,
            selectedPath,
            showSetupNote: false);

        QueueExternalToolsRefresh(showCheckingState: false);
    }

    private void QueueExternalToolsRefresh() => QueueExternalToolsRefresh(showCheckingState: false);

    private void QueueExternalToolsRefresh(bool showCheckingState)
    {
        if (_isClosed)
        {
            return;
        }

        _externalToolsRefreshCts?.Cancel();
        _externalToolsRefreshCts?.Dispose();
        _externalToolsRefreshCts = CancellationTokenSource.CreateLinkedTokenSource(_windowLifetimeCts.Token);

        int generation = ++_externalToolsRefreshGeneration;
        CancellationToken token = _externalToolsRefreshCts.Token;

        if (showCheckingState)
        {
            string unavailableText = ResolveUiText(ValueUnavailableKey);
            _owner.ViewModel.SetCsoKitExternalToolStatus(
                ResolveUiText(ExternalToolsStatusCheckingKey),
                unavailableText,
                unavailableText,
                showSetupNote: false);
        }

        _ = Task.Run(
            async () =>
            {
                CsoToolProbeResult result;
                try
                {
                    result = await new CsoToolProbe(_currentSettings.ExternalCsoKitPath).CheckAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (ex is IOException
                                          or UnauthorizedAccessException
                                          or ArgumentException
                                          or InvalidOperationException
                                          or NotSupportedException
                                          or PathTooLongException
                                          or System.ComponentModel.Win32Exception
                                          or System.Security.SecurityException)
                {
                    Logger.Debug(ex, "Hakamiq CsoKit external tools refresh failed.");
                    result = new CsoToolProbeResult(
                        CsoToolStatus.Failed,
                        string.Empty,
                        string.Empty,
                        CsoToolProbe.ToolFailedMessageKey,
                        1,
                        ex.Message);
                }

                await _owner.Dispatcher.InvokeAsync(
                    () =>
                    {
                        if (_isClosed || generation != _externalToolsRefreshGeneration)
                        {
                            return;
                        }

                        ApplyCsoKitProbeResult(result);
                    });
            },
            token);
    }

    private void ApplyCsoKitProbeResult(CsoToolProbeResult result)
    {
        string statusKey = result.Status switch
        {
            CsoToolStatus.Available => ExternalToolsStatusAvailableKey,
            CsoToolStatus.Failed => ExternalToolsStatusFailedKey,
            _ => ExternalToolsStatusMissingKey
        };

        string unavailableText = ResolveUiText(ValueUnavailableKey);
        bool isAvailable = result.Status == CsoToolStatus.Available;

        string versionDisplayText = isAvailable && !string.IsNullOrWhiteSpace(result.VersionText)
            ? FormatCsoKitVersionForSettings(result.VersionText)
            : unavailableText;

        _owner.ViewModel.SetCsoKitExternalToolStatus(
            ResolveUiText(statusKey),
            versionDisplayText,
            isAvailable && !string.IsNullOrWhiteSpace(result.ToolPath) ? result.ToolPath : ResolvePreferredToolsFolder(),
            showSetupNote: !isAvailable);
    }

    private static string FormatCsoKitVersionForSettings(string versionText)
    {
        string value = versionText.Trim();
        const string productName = "Hakamiq.CsoKit";

        if (value.StartsWith(productName, StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring(productName.Length).Trim();
        }

        if (value.StartsWith("version ", StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring("version ".Length).Trim();
        }

        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            && value.Length > 1
            && char.IsDigit(value[1]))
        {
            value = value.Substring(1).Trim();
        }

        return string.IsNullOrWhiteSpace(value)
            ? versionText.Trim()
            : value;
    }

    private string ResolvePreferredToolsFolder()
    {
        string preferredPath = _currentSettings.ExternalCsoKitPath;
        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            try
            {
                string fullPath = Path.GetFullPath(preferredPath.Trim());
                string? directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    return directory;
                }
            }
            catch (Exception ex) when (IsExpectedPathException(ex))
            {
                Logger.Debug(ex, "Ignored invalid Hakamiq CsoKit preferred folder path. Path={Path}", preferredPath);
            }
        }

        return new CsoToolLocator(preferredPath).BundledToolsFolderPath;
    }

    private static bool TryGetSafeExistingCsoToolPath(string path, out string selectedPath)
    {
        selectedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path.Trim());

            if (!string.Equals(Path.GetFileName(fullPath), CsoToolLocator.ToolExecutableName, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(fullPath)
                || HasReparsePointInExistingPathFromVolumeRoot(fullPath))
            {
                return false;
            }

            ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(path));

            selectedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }
    }

    private static bool TryGetSafeExistingDirectory(string? directory, out string fullDirectory)
    {
        fullDirectory = string.Empty;

        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            string candidate = Path.GetFullPath(directory.Trim());

            if (!Directory.Exists(candidate)
                || HasReparsePointInExistingPathFromVolumeRoot(candidate))
            {
                return false;
            }

            ConversionPathValidator.ThrowIfUnsafeForChdman(candidate, nameof(directory));

            fullDirectory = candidate;
            return true;
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }
    }

    private static string NormalizeSafeFolderPathForCreate(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("A tools folder path is required.", nameof(folderPath));
        }

        string fullPath = Path.GetFullPath(folderPath.Trim());
        string? root = Path.GetPathRoot(fullPath);

        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("A rooted tools folder path is required.", nameof(folderPath));
        }

        if (HasReparsePointInExistingPath(fullPath, root))
        {
            throw new InvalidOperationException("The tools folder path resolves through a reparse point.");
        }

        return fullPath;
    }

    private static bool HasReparsePointInExistingPathFromVolumeRoot(string candidatePath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string? root = Path.GetPathRoot(candidate);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return HasReparsePointInExistingPath(candidate, root);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return true;
        }
    }

    private static bool HasReparsePointInExistingPath(string candidatePath, string rootPath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string root = Path.GetFullPath(rootPath);

            if (!IsSamePathOrChild(root, candidate))
            {
                return true;
            }

            string current = candidate;

            while (true)
            {
                if ((File.Exists(current) || Directory.Exists(current)) && IsReparsePoint(current))
                {
                    return true;
                }

                if (PathsEqual(current, root))
                {
                    return false;
                }

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent) || PathsEqual(parent, current))
                {
                    return true;
                }

                current = parent;
            }
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return true;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return true;
        }
    }

    private static bool IsSamePathOrChild(string rootPath, string candidatePath)
    {
        string root = TrimDirectorySeparators(Path.GetFullPath(rootPath));
        string candidate = TrimDirectorySeparators(Path.GetFullPath(candidatePath));

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(EnsureDirectorySeparatorSuffix(root), StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            TrimDirectorySeparators(Path.GetFullPath(left)),
            TrimDirectorySeparators(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureDirectorySeparatorSuffix(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string TrimDirectorySeparators(string path)
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

    private static bool IsExpectedPathException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or InvalidOperationException
        or NotSupportedException
        or PathTooLongException
        or System.Security.SecurityException;
}