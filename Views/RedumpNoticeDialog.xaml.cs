using System;
using System.Windows;
using System.Windows.Input;

using HakamiqChdTool.App.Localization;

namespace HakamiqChdTool.App.Views;

public partial class RedumpNoticeDialog : Window
{
    public RedumpNoticeDialog(string title, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        InitializeComponent();
        HakamiqChdTool.App.Ui.Shell.WindowBackdrop.ApplyDialog(this);
        AppLanguageService.ApplyToWindow(this);

        DataContext = new RedumpNoticeDialogViewModel(
            title.Trim(),
            message.Trim());
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

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseAsConfirmed();
    }

    private void CloseAsConfirmed()
    {
        try
        {
            DialogResult = true;
        }
        catch (InvalidOperationException)
        {
            Close();
        }
    }

    private sealed record RedumpNoticeDialogViewModel(
        string Title,
        string Message);
}