using System;
using System.Windows;
using System.Windows.Input;

using HakamiqChdTool.App.Localization;

namespace HakamiqChdTool.App.Views;

public partial class ClearTaskLogConfirmationDialog : Window
{
    internal ClearTaskLogConfirmationDialog()
        : this(
            ArabicUi.Get(MainWindowMessages.ClearQueueTitle),
            ArabicUi.Get(MainWindowMessages.ClearQueuePrompt),
            ArabicUi.Get("LocUi_Hub_ClearQueue"),
            ArabicUi.Get(MainWindowMessages.RenameDialogCancel))
    {
    }

    internal ClearTaskLogConfirmationDialog(
        string title,
        string body,
        string confirmText,
        string cancelText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmText);
        ArgumentException.ThrowIfNullOrWhiteSpace(cancelText);

        InitializeComponent();
        HakamiqChdTool.App.Ui.Shell.WindowBackdrop.ApplyDialog(this);
        AppLanguageService.ApplyToWindow(this);

        DataContext = new ConfirmationDialogModel(
            title.Trim(),
            body.Trim(),
            confirmText.Trim(),
            cancelText.Trim());
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        CloseWithDialogResult(true);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
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

    private sealed record ConfirmationDialogModel(
        string Title,
        string Body,
        string ConfirmText,
        string CancelText);
}