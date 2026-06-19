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
    private const string ExternalToolsSetupInstructionsKey = "LocExternalTools_SetupInstructions";
    private const string OpenFolderFailedBodyKey = "LocDialog_OpenFolderFailedBody";

    public void RecheckExternalTools(object? sender, EventArgs e) => QueueExternalToolsRefresh();

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

    private void QueueExternalToolsRefresh()
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

        _owner.ViewModel.SetCsoKitExternalToolStatus(
            ResolveUiText(statusKey),
            string.IsNullOrWhiteSpace(result.VersionText) ? "-" : result.VersionText,
            string.IsNullOrWhiteSpace(result.ToolPath) ? new CsoToolLocator().BundledToolsFolderPath : result.ToolPath);
    }
}
