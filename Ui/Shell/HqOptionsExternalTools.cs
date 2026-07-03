using HakamiqChdTool.App.Services;
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
    private const string ExternalToolsSetupInstructionsKey = "LocExternalTools_SetupInstructions";
    private const string OpenFolderFailedBodyKey = "LocDialog_OpenFolderFailedBody";
    private const string ValueUnavailableKey = "LocValue_Unavailable";

    public void RecheckExternalTools(object? sender, EventArgs e) => QueueExternalToolsRefresh(showCheckingState: true);

    public void OpenExternalToolsFolder(object? sender, EventArgs e)
    {
        string folderPath = new CsoToolLocator().BundledToolsFolderPath;

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

    public void CopyExternalToolsSetupInstructions(object? sender, EventArgs e)
    {
        string text = ResolveUiText(ExternalToolsSetupInstructionsKey);
        if (!new ClipboardService().TrySetText(text))
        {
            ShowNoticeDialog(OperationErrorTitleKey, ExternalToolsSetupInstructionsKey);
        }
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
                unavailableText);
        }

        _ = Task.Run(
            async () =>
            {
                CsoToolProbeResult result;
                try
                {
                    result = await new CsoToolProbe().CheckAsync(token).ConfigureAwait(false);
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

        _owner.ViewModel.SetCsoKitExternalToolStatus(
            ResolveUiText(statusKey),
            isAvailable && !string.IsNullOrWhiteSpace(result.VersionText) ? result.VersionText : unavailableText,
            isAvailable && !string.IsNullOrWhiteSpace(result.ToolPath) ? result.ToolPath : unavailableText);
    }
}
