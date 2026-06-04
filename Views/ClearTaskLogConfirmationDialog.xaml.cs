using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
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

    internal ClearTaskLogConfirmationDialog(string title, string body, string confirmText, string cancelText)
    {
        InitializeComponent();
        AppLanguageService.ApplyToWindow(this);
        DataContext = new ConfirmationDialogModel(
            title.Trim(),
            body.Trim(),
            confirmText.Trim(),
            cancelText.Trim());
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        Dispatcher dispatcher = Dispatcher;
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private sealed record ConfirmationDialogModel(
        string Title,
        string Body,
        string ConfirmText,
        string CancelText);
}
