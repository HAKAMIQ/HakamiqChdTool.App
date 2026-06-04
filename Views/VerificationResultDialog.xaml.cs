using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Services;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace HakamiqChdTool.App.Views;

public partial class VerificationResultDialog : Window
{
    public VerificationResultDialog(QueueVerificationResultPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        InitializeComponent();
        AppLanguageService.ApplyToWindow(this);
        DataContext = presentation;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
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
}
