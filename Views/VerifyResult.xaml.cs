using System;
using System.Windows;
using System.Windows.Input;

using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Services;

namespace HakamiqChdTool.App.Views;

public partial class VerificationResultDialog : Window
{
    public VerificationResultDialog(QueueVerifyView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        InitializeComponent();
        HakamiqChdTool.App.Ui.Shell.WindowBackdrop.ApplyDialog(this);
        AppLanguageService.ApplyToWindow(this);

        DataContext = view;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseAsConfirmed();
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
}