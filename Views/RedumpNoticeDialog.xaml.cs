using System;
using System.Windows;
using System.Windows.Input;

namespace HakamiqChdTool.App.Views;

public partial class RedumpNoticeDialog : Window
{
    public RedumpNoticeDialog(string title, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        InitializeComponent();

        DataContext = new RedumpNoticeDialogViewModel(
            title.Trim(),
            message.Trim());
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
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

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private sealed record RedumpNoticeDialogViewModel(
        string Title,
        string Message);
}