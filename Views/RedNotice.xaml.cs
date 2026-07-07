using System;
using System.Windows;
using System.Windows.Input;

using HakamiqChdTool.App.Localization;

namespace HakamiqChdTool.App.Views;

public partial class RedumpNoticeDialog : Window
{
    public RedumpNoticeDialog(string title, string message)
        : this(
            title,
            message,
            ArabicUi.Get("LocCommon_Close"),
            string.Empty)
    {
    }

    public RedumpNoticeDialog(
        string title,
        string message,
        string closeText,
        string confirmText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(closeText);

        InitializeComponent();
        HakamiqChdTool.App.Ui.Shell.WindowBackdrop.ApplyDialog(this);
        AppLanguageService.ApplyToWindow(this);

        DataContext = new RedumpNoticeDialogViewModel(
            title.Trim(),
            message.Trim(),
            closeText.Trim(),
            confirmText?.Trim() ?? string.Empty);
    }

    private bool IsConfirmation =>
        DataContext is RedumpNoticeDialogViewModel viewModel &&
        !string.IsNullOrWhiteSpace(viewModel.ConfirmText);

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

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseAsConfirmed(!IsConfirmation);
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        CloseAsConfirmed(true);
    }

    private void CloseAsConfirmed(bool result)
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

    private sealed record RedumpNoticeDialogViewModel(
        string Title,
        string Message,
        string CloseText,
        string ConfirmText)
    {
        public Visibility ConfirmVisibility =>
            string.IsNullOrWhiteSpace(ConfirmText) ? Visibility.Collapsed : Visibility.Visible;
    }
}
