using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.ViewModels;

namespace HakamiqChdTool.App.Views;

/// <summary>
/// The small window an Explorer menu launch opens instead of the main window. Closing it while files are running stops
/// them first (the service cleans up), then closes.
/// </summary>
public partial class QuickRunWindow : Window
{
    private QuickRunViewModel? _viewModel;
    private bool _closeRequested;
    private bool _allowClose;

    public QuickRunWindow()
    {
        InitializeComponent();
        HakamiqChdTool.App.Ui.Shell.WindowBackdrop.ApplyDialog(this);
        AppLanguageService.ApplyToWindow(this);
        AppLanguageService.Instance.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Raised once the window may close: nothing is running any more.</summary>
    internal event Action? ClosedWhenIdle;

    internal void Attach(QuickRunViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        viewModel.DetailsRequested += ShowDetails;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (_closeRequested && e.PropertyName == nameof(QuickRunViewModel.IsBusy) && !viewModel.IsBusy)
            {
                Dispatcher.BeginInvoke(new Action(Close));
            }
        };
    }

    /// <summary>Closes without asking, for example after the files were handed to the main window.</summary>
    internal void CloseNow()
    {
        _allowClose = true;
        Close();
    }

    /// <summary>Run once before the window closes while idle (shuts the queue down); then the window closes.</summary>
    internal Func<Task>? BeforeCloseWhenIdle { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose && _viewModel is { IsBusy: true })
        {
            _closeRequested = true;
            _viewModel.CancelCommand.Execute(null);
            e.Cancel = true;
        }
        else if (!_allowClose && BeforeCloseWhenIdle is { } beforeClose)
        {
            e.Cancel = true;
            BeforeCloseWhenIdle = null;
            _ = FinishCloseAsync(beforeClose);
        }

        base.OnClosing(e);
    }

    private async Task FinishCloseAsync(Func<Task> beforeClose)
    {
        try
        {
            await beforeClose().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            global::Serilog.Log.Warning(ex, "Quick window shutdown step failed.");
        }

        CloseNow();
    }

    protected override void OnClosed(EventArgs e)
    {
        AppLanguageService.Instance.LanguageChanged -= OnLanguageChanged;
        _viewModel?.Detach();
        base.OnClosed(e);
        ClosedWhenIdle?.Invoke();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => _viewModel?.RefreshTexts();

    private void ShowDetails(string title, string report)
    {
        if (string.IsNullOrWhiteSpace(report))
        {
            return;
        }

        // The report is already localized and mixes in technical terms, so it is shown as is (as in the main window).
        var dialog = new RedumpNoticeDialog(title, report) { Owner = this };
        _ = dialog.ShowDialog();
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

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
