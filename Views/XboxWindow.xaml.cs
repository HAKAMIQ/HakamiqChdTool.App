using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.ViewModels;
using Microsoft.Win32;
using Ookii.Dialogs.Wpf;

namespace HakamiqChdTool.App.Views;

/// <summary>The Xbox section's window. Closing it while a game is converting stops it first, then closes.</summary>
public partial class XboxWindow : Window, IXboxPicker
{
    private XboxViewModel? _viewModel;
    private bool _closeRequested;

    public XboxWindow()
    {
        InitializeComponent();
        HakamiqChdTool.App.Ui.Shell.WindowBackdrop.ApplyDialog(this);
        AppLanguageService.ApplyToWindow(this);
    }

    internal void Attach(XboxViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (_closeRequested && e.PropertyName == nameof(XboxViewModel.IsRunning) && !viewModel.IsRunning)
            {
                Dispatcher.BeginInvoke(new Action(Close));
            }
        };
    }

    IReadOnlyList<string> IXboxPicker.PickImages()
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = ArabicUi.Get("LocXbox_AddFiles"),
            Filter = ArabicUi.Get("LocXbox_FileFilter")
        };

        return dialog.ShowDialog(this) == true ? dialog.FileNames : [];
    }

    string? IXboxPicker.PickFolder(string titleKey)
    {
        var dialog = new VistaFolderBrowserDialog
        {
            Description = ArabicUi.Get(titleKey),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        return dialog.ShowDialog(this) == true ? dialog.SelectedPath : null;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // The running tool is stopped and its temporary files removed by the service; closing waits for that.
        if (_viewModel is { IsRunning: true })
        {
            _closeRequested = true;
            _viewModel.CancelRun();
            e.Cancel = true;
        }

        base.OnClosing(e);
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

    private void List_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = _viewModel is { IsRunning: false } && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void List_Drop(object sender, DragEventArgs e)
    {
        if (_viewModel is not { IsRunning: false } || e.Data.GetData(DataFormats.FileDrop) is not string[] dropped)
        {
            return;
        }

        e.Handled = true;
        try
        {
            await _viewModel.AddPathsAsync(dropped).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            global::Serilog.Log.Debug(ex, "Adding dropped Xbox items failed.");
        }
    }
}
