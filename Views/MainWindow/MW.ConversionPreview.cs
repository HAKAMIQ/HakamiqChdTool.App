using HakamiqChdTool.App.Core.Workflow;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.ViewModels;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private readonly ChdConversionService _decisionPreviewService = new();

    private async void ConversionDecisionPreviewRequested(object sender, RoutedEventArgs e)
    {
        if (TasksDataGrid.SelectedItem is not TaskQueueItemViewModel item)
        {
            QueueWorkspace.ConversionPreviewText.Text = ArabicUi.Get("LocConversionPreview_SelectInput");
            return;
        }

        string inputPath = item.SourcePath;
        string extension = Path.GetExtension(inputPath);
        string atRun = ArabicUi.ResolveDisplayString("LocConversionPreview_AtRun");
        string archiveText = ArabicUi.ResolveDisplayString("LocConversionPreview_Archive");
        bool isArchive = extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".rar", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".7z", StringComparison.OrdinalIgnoreCase);
        bool isCso = extension.Equals(".cso", StringComparison.OrdinalIgnoreCase);

        try
        {
            string chdmanPath = _chdmanPathResolver.ResolvePath(_settings);
            string outputPath = isArchive
                ? atRun
                : WorkflowPathUtilities.BuildFinalChdOutputPath(
                    item.DetectedPlatform,
                    item.OriginalPath,
                    inputPath,
                    _settings);

            if (isArchive)
            {
                QueueWorkspace.ConversionPreviewText.Text = ArabicUi.Format(
                    "LocConversionPreview_Format",
                    archiveText,
                    inputPath,
                    atRun,
                    item.DetectedPlatform,
                    atRun,
                    atRun,
                    outputPath,
                    PreviewVerifyValue(_settings.VerifyAfterConversion),
                    ArabicUi.ResolveDisplayString("LocConversionPreview_CapabilityDeferred"),
                    ArabicUi.ResolveDisplayString("LocConversionPreview_ArchiveDeferred"));
                return;
            }

            ChdConversionDecisionPreview preview = await _decisionPreviewService.PreviewDecisionAsync(
                chdmanPath,
                inputPath,
                outputPath,
                _settings);

            string media = preview.MediaKind;
            string codec = string.IsNullOrWhiteSpace(preview.ResolvedCompression)
                ? ArabicUi.ResolveDisplayString("LocConversionPreview_Default")
                : preview.ResolvedCompression;
            string hunk = preview.ResolvedHunkSizeBytes > 0
                ? preview.ResolvedHunkSizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : ArabicUi.ResolveDisplayString("LocConversionPreview_Default");
            string command = preview.Arguments.Count == 0
                ? ArabicUi.ResolveDisplayString(preview.Message)
                : preview.DisplayCommandLine;
            if (isCso)
            {
                command = archiveText;
            }
            string capability = preview.Capabilities.IsAvailable
                ? $"{preview.Capabilities.Version} ({preview.Capabilities.DiagnosticSummary})"
                : ArabicUi.ResolveDisplayString(preview.Capabilities.MessageKey);
            string notes = string.Join("; ", new[]
            {
                ArabicUi.ResolveDisplayString(preview.Message),
                ArabicUi.ResolveDisplayString(preview.CapabilityWarning),
                isCso ? archiveText : string.Empty
            }.Where(static value => !string.IsNullOrWhiteSpace(value)));

            QueueWorkspace.ConversionPreviewText.Text = ArabicUi.Format(
                "LocConversionPreview_Format",
                media,
                isCso
                ? $"{inputPath} — {atRun}"
                    : $"{inputPath} — {ArabicUi.ResolveDisplayString(preview.MediaEvidence)}",
                command,
                isCso
                    ? atRun
                    : string.IsNullOrWhiteSpace(preview.PlatformProfile) ? item.DetectedPlatform : preview.PlatformProfile,
                isCso ? $"{codec} ({atRun})" : codec,
                isCso ? $"{hunk} ({atRun})" : hunk,
                outputPath,
                PreviewVerifyValue(preview.VerifyAfterConversion),
                capability,
                notes);
        }
        catch (OperationCanceledException)
        {
            QueueWorkspace.ConversionPreviewText.Text = ArabicUi.ResolveDisplayString("LocOperation_Cancelled");
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Conversion decision preview failed for selected queue item. Input={InputPath}", inputPath);
            QueueWorkspace.ConversionPreviewText.Text = ArabicUi.Format(
                "LocConversionPreview_Format",
                ArabicUi.ResolveDisplayString("LocConversionPreview_Unknown"),
                inputPath,
                ArabicUi.ResolveDisplayString("LocConversionPreview_NotAvailable"),
                item.DetectedPlatform,
                ArabicUi.ResolveDisplayString("LocConversionPreview_NotAvailable"),
                ArabicUi.ResolveDisplayString("LocConversionPreview_NotAvailable"),
                ArabicUi.ResolveDisplayString("LocConversionPreview_NotAvailable"),
                PreviewVerifyValue(_settings.VerifyAfterConversion),
                ex.GetType().Name,
                RuntimeDiagnosticFormatter.SummarizeException(ex));
        }
    }

    private static string PreviewVerifyValue(bool enabled) =>
        ArabicUi.ResolveDisplayString(enabled ? "LocConversionPreview_Yes" : "LocConversionPreview_No");
}
