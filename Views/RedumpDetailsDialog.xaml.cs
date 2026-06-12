using System;
using System.Windows;
using System.Windows.Input;

using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.ViewModels.Dialogs;

namespace HakamiqChdTool.App.Views;

public sealed partial class RedumpDetailsDialog : Window
{
    private readonly RedumpDetailsDialogViewModel _viewModel;

    public RedumpDetailsDialog(RedumpDetailsDialogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;

        InitializeComponent();
        HakamiqChdTool.App.Ui.Shell.WindowBackdrop.ApplyDialog(this);
        AppLanguageService.ApplyToWindow(this);

        DataContext = _viewModel;

        _viewModel.CloseRequested += RedumpDetailsDialog_CloseRequested;
        Closed += RedumpDetailsDialog_Closed;
    }

    public bool ApplyNameRequested { get; private set; }

    private void RedumpDetailsDialog_CloseRequested(bool applyNameRequested)
    {
        ApplyNameRequested = applyNameRequested;
        CloseWithDialogResult(applyNameRequested);
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

    private void RedumpDetailsDialog_Closed(object? sender, EventArgs e)
    {
        Closed -= RedumpDetailsDialog_Closed;
        _viewModel.CloseRequested -= RedumpDetailsDialog_CloseRequested;
        _viewModel.DisposeFeedbackTimer();
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