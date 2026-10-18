using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace HakamiqChdTool.App.Views;

public partial class LibraryAnalysisWindow : Window
{
    private readonly LibraryAnalysisViewModel _viewModel;
    private bool _closeWhenIdle;

    public LibraryAnalysisWindow(LibraryAnalysisViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        // WindowBackdrop clamps the designed size to the work area of the monitor the window is
        // actually on and re-clamps on DPI or monitor changes; do not pre-clamp to the primary screen.
        HakamiqChdTool.App.Ui.Shell.WindowBackdrop.ApplyDialog(this);
        AppLanguageService.ApplyToWindow(this);

        _viewModel = viewModel;
        DataContext = viewModel;
        Closing += LibraryAnalysisWindow_Closing;
        Closed += LibraryAnalysisWindow_Closed;
        AppLanguageService.Instance.LanguageChanged += AppLanguageService_LanguageChanged;
    }

    // Strings the view model builds (filters, outcome, status, details) are rendered in code,
    // so they are re-rendered here; XAML-bound resources already follow the language switch.
    private void AppLanguageService_LanguageChanged(object? sender, EventArgs e) => _viewModel.RefreshLanguage();

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
        Close();
    }

    // Closing while an analysis runs asks it to stop and closes once it has actually ended,
    // so the worker never outlives its window's view model state.
    private void LibraryAnalysisWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_viewModel.IsRunning)
        {
            return;
        }

        e.Cancel = true;

        if (_closeWhenIdle)
        {
            return;
        }

        _closeWhenIdle = true;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.Cancel();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LibraryAnalysisViewModel.IsRunning) || _viewModel.IsRunning)
        {
            return;
        }

        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        Dispatcher.BeginInvoke(new Action(Close));
    }

    private void LibraryAnalysisWindow_Closed(object? sender, EventArgs e)
    {
        AppLanguageService.Instance.LanguageChanged -= AppLanguageService_LanguageChanged;
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.Cancel();
    }
}
