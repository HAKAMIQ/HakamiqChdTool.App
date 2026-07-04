using HakamiqChdTool.App.Services;
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
            Directory.CreateDirectory(folderPath);
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException
                                  or System.Security.SecurityException)
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
                string currentFullPath = Path.GetFullPath(currentPath);
                string? currentDirectory = Path.GetDirectoryName(currentFullPath);
                if (!string.IsNullOrWhiteSpace(currentDirectory) && Directory.Exists(currentDirectory))
                {
                    dialog.InitialDirectory = currentDirectory;
                }
            }
            catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or ArgumentException
                                      or NotSupportedException
                                      or PathTooLongException
                                      or System.Security.SecurityException)
            {
                Logger.Debug(ex, "Ignored invalid Hakamiq CsoKit preferred path before browse. Path={Path}", currentPath);
            }
        }

        if (string.IsNullOrWhiteSpace(dialog.InitialDirectory))
        {
            string bundledFolder = new CsoToolLocator(_currentSettings.ExternalCsoKitPath).BundledToolsFolderPath;
            if (Directory.Exists(bundledFolder))
            {
                dialog.InitialDirectory = bundledFolder;
            }
        }

        bool? result = dialog.ShowDialog(_owner);
        if (result != true)
        {
            return;
        }

        string selectedPath;
        try
        {
            selectedPath = Path.GetFullPath(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException
                                  or System.Security.SecurityException)
        {
            Logger.Debug(ex, "Invalid Hakamiq CsoKit selected path. Path={Path}", dialog.FileName);
            ShowNoticeDialog(OperationErrorTitleKey, InvalidCsoKitToolSelectionKey);
            return;
        }

        if (!string.Equals(Path.GetFileName(selectedPath), CsoToolLocator.ToolExecutableName, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(selectedPath))
        {
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
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or InvalidOperationException
                                  or NotSupportedException
                                  or PathTooLongException
                                  or System.Security.SecurityException)
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
                string fullPath = Path.GetFullPath(preferredPath);
                string? directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    return directory;
                }
            }
            catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or ArgumentException
                                      or NotSupportedException
                                      or PathTooLongException
                                      or System.Security.SecurityException)
            {
                Logger.Debug(ex, "Ignored invalid Hakamiq CsoKit preferred folder path. Path={Path}", preferredPath);
            }
        }

        return new CsoToolLocator(preferredPath).BundledToolsFolderPath;
    }
}
