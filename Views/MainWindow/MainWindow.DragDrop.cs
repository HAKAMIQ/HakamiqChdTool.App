using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Ui.Queue;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private const double WorkspaceDropHighlightOpacity = 0.14;

    private const string DragDropCancelledFooterKey = "LocDragDrop_AddCancelledFooter";
    private const string DragDropFailedFooterKey = "LocDragDrop_AddFailedFooter";
    private const string InputDialogTitleVerifyKey = "LocInputDialog_Title_VerifyChd";
    private const string InputDialogTitleExtractKey = "LocInputDialog_Title_AddChdForExtract";
    private const string InputDialogTitleConvertKey = "LocInputDialog_Title_AddFilesForConvert";
    private const string InputDialogFilterChdKey = "LocInputDialog_Filter_Chd";
    private const string InputDialogFilterConvertKey = "LocInputDialog_Filter_Convert";
    private const string InputDialogFilterSupportedKey = "LocInputDialog_Filter_Supported";
    private const string FolderScopeSelectedKey = "LocFolderDialog_Scope_Selected";
    private const string FolderScopeWithSubfoldersKey = "LocFolderDialog_Scope_WithSubfolders";
    private const string FolderDialogDescriptionExtractFormatKey = "LocFolderDialog_Description_ExtractFormat";
    private const string FolderDialogDescriptionVerifyFormatKey = "LocFolderDialog_Description_VerifyFormat";
    private const string FolderDialogDescriptionConvertFormatKey = "LocFolderDialog_Description_ConvertFormat";

    private void WorkspaceCardRoot_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (CanAcceptWorkspaceFileDrop(e))
        {
            e.Effects = DragDropEffects.Copy;
            DropHighlightOverlay.Opacity = WorkspaceDropHighlightOpacity;
            e.Handled = false;
            return;
        }

        e.Effects = DragDropEffects.None;
        DropHighlightOverlay.Opacity = 0;
        e.Handled = false;
    }

    private void WorkspaceCardRoot_PreviewDragLeave(object sender, DragEventArgs e)
    {
        DropHighlightOverlay.Opacity = 0;
        e.Effects = DragDropEffects.None;
        e.Handled = false;
    }

    private void WorkspaceCardRoot_PreviewDrop(object sender, DragEventArgs e)
    {
        DropHighlightOverlay.Opacity = 0;
        e.Effects = DragDropEffects.None;
        e.Handled = false;
    }

    private void OnWorkspaceFilesDropped(object sender, DragEventArgs e)
    {
        DropHighlightOverlay.Opacity = 0;
        e.Effects = DragDropEffects.None;

        if (IsQueueInteractionLocked)
        {
            e.Handled = false;
            return;
        }

        if (!TryGetDroppedFilePaths(e, out string[] normalized))
        {
            e.Handled = false;
            return;
        }

        e.Handled = true;
        e.Effects = DragDropEffects.Copy;

        QueueExecutionProfile profile = GetSelectedInputExecutionProfileFromUi();
        _ = HandleWorkspaceFilesDroppedAsync(normalized, profile);
    }

    private async Task HandleWorkspaceFilesDroppedAsync(
        string[] normalized,
        QueueExecutionProfile profile)
    {
        try
        {
            if (profile == QueueExecutionProfile.Standard)
            {
                await _viewModel
                    .IngestPathsAsync(
                        normalized,
                        QueueIngestKind.Mixed,
                        QueueIntakeSource.DragDrop)
                    .ConfigureAwait(true);

                return;
            }

            _ = await _viewModel
                .IngestQuickPathsAsync(
                    normalized,
                    QueueIngestKind.Mixed,
                    profile,
                    QueueIntakeSource.DragDrop)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException ex)
        {
            Log.Debug(ex, "Workspace drag-and-drop ingestion was cancelled.");
            SetFooterStatus(ArabicUi.Get(DragDropCancelledFooterKey));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Workspace drag-and-drop handling failed.");
            SetFooterStatus(ArabicUi.Get(DragDropFailedFooterKey));
        }
    }

    private QueueExecutionProfile GetSelectedInputExecutionProfileFromUi()
    {
        if (ActionRail.IsVerifyMode)
        {
            return QueueExecutionProfile.QuickVerify;
        }

        if (ActionRail.IsExtractMode)
        {
            return QueueExecutionProfile.QuickExtract;
        }

        if (ActionRail.IsConvertMode)
        {
            return QueueExecutionProfile.QuickConvert;
        }

        return QueueExecutionProfile.Standard;
    }

    private bool IsSelectedScanModeFromUi()
    {
        return false;
    }

    private string GetSelectedInputDialogTitleFromUi(QueueExecutionProfile profile)
    {
        return profile switch
        {
            QueueExecutionProfile.QuickVerify => ArabicUi.Get(InputDialogTitleVerifyKey),
            QueueExecutionProfile.QuickExtract => ArabicUi.Get(InputDialogTitleExtractKey),
            QueueExecutionProfile.QuickConvert => ArabicUi.Get(InputDialogTitleConvertKey),
            _ => ArabicUi.Get(MainWindowMessages.AddFilesDialogTitle)
        };
    }

    private string GetSelectedInputDialogFilterFromUi(QueueExecutionProfile profile)
    {
        return profile switch
        {
            QueueExecutionProfile.QuickExtract => ArabicUi.Get(InputDialogFilterChdKey),
            QueueExecutionProfile.QuickVerify => ArabicUi.Get(InputDialogFilterChdKey),
            QueueExecutionProfile.QuickConvert => ArabicUi.Get(InputDialogFilterConvertKey),
            _ => ArabicUi.Get(InputDialogFilterSupportedKey)
        };
    }

    private string GetSelectedFolderDialogDescriptionFromUi()
    {
        QueueExecutionProfile profile = GetSelectedInputExecutionProfileFromUi();

        string scope = _settings.IncludeSubfolders
            ? ArabicUi.Get(FolderScopeWithSubfoldersKey)
            : ArabicUi.Get(FolderScopeSelectedKey);

        return profile switch
        {
            QueueExecutionProfile.QuickExtract => ArabicUi.Format(
                FolderDialogDescriptionExtractFormatKey,
                scope),

            QueueExecutionProfile.QuickVerify => ArabicUi.Format(
                FolderDialogDescriptionVerifyFormatKey,
                scope),

            QueueExecutionProfile.QuickConvert => ArabicUi.Format(
                FolderDialogDescriptionConvertFormatKey,
                scope),

            _ => ArabicUi.Get(MainWindowMessages.SelectFolderDialogDescription)
        };
    }

    private bool CanAcceptWorkspaceFileDrop(DragEventArgs e)
    {
        if (IsQueueInteractionLocked)
        {
            return false;
        }

        try
        {
            return e.Data.GetDataPresent(DataFormats.FileDrop, autoConvert: true);
        }
        catch (Exception ex) when (IsExpectedDragDropDataException(ex))
        {
            Log.Debug(ex, "Drag-and-drop data probing failed.");
            return false;
        }
    }

    private bool TryGetDroppedFilePaths(DragEventArgs e, out string[] normalized)
    {
        normalized = [];

        try
        {
            if (e.Data.GetData(DataFormats.FileDrop, autoConvert: true) is not string[] rawPaths ||
                rawPaths.Length == 0)
            {
                return false;
            }

            List<string> normalizedPaths = [];
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            foreach (string rawPath in rawPaths)
            {
                if (TaskQueueDropPathNormalizer.TryNormalizeRootPath(rawPath, out string? normalizedPath) &&
                    seen.Add(normalizedPath))
                {
                    normalizedPaths.Add(normalizedPath);
                }
            }

            normalized = [.. normalizedPaths];
            return normalized.Length > 0;
        }
        catch (Exception ex) when (IsExpectedDragDropDataException(ex))
        {
            Log.Debug(ex, "Reading dropped file paths failed.");
            return false;
        }
        catch (Exception ex) when (IsExpectedDroppedPathException(ex))
        {
            Log.Debug(ex, "Normalizing dropped file paths failed.");
            return false;
        }
    }

    private static bool IsExpectedDragDropDataException(Exception ex)
    {
        return ex is COMException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException;
    }

    private static bool IsExpectedDroppedPathException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException;
    }
}