using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;

namespace HakamiqChdTool.App.Views;

public partial class OrphanedCleanupDialog : Window
{
    internal OrphanedCleanupDialog(
        OrphanedWorkItemScanResult scanResult,
        string reclaimableSizeText,
        string processTempRoot)
    {
        ArgumentNullException.ThrowIfNull(scanResult);

        SummaryText = string.Format(
            CultureInfo.CurrentCulture,
            ArabicUi.Get("LocOrphanedCleanup_PromptBody"),
            scanResult.Items.Count,
            scanResult.TotalFiles,
            reclaimableSizeText);

        ItemCountText = scanResult.Items.Count.ToString("N0", CultureInfo.CurrentCulture);
        FileCountText = scanResult.TotalFiles.ToString("N0", CultureInfo.CurrentCulture);
        ReclaimableSizeText = reclaimableSizeText;

        ProcessTempRootText = string.IsNullOrWhiteSpace(processTempRoot)
            ? ArabicUi.Get("LocOrphanedCleanup_TempRootUnavailable")
            : processTempRoot;

        InitializeComponent();
        HakamiqChdTool.App.Ui.Shell.WindowBackdrop.ApplyDialog(this);
        AppLanguageService.ApplyToWindow(this);

        DataContext = this;
    }

    public string SummaryText { get; }

    public string ItemCountText { get; }

    public string FileCountText { get; }

    public string ReclaimableSizeText { get; }

    public string ProcessTempRootText { get; }

    private void CleanButton_Click(object sender, RoutedEventArgs e)
    {
        CloseWithDialogResult(true);
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        CloseWithDialogResult(false);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
            e.Handled = true;
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void CloseWithDialogResult(bool result)
    {
        try
        {
            DialogResult = result;
        }
        catch (InvalidOperationException)
        {
            Close();
        }
    }
}