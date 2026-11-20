using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.ViewModels;
using Microsoft.Win32;
using Ookii.Dialogs.Wpf;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace HakamiqChdTool.App.Views;

public partial class RecompressWindow : Window, IRecompressPicker
{
    private RecompressViewModel? _viewModel;
    private bool _closeRequested;

    public RecompressWindow()
    {
        InitializeComponent();
        HakamiqChdTool.App.Ui.Shell.WindowBackdrop.ApplyDialog(this);
        AppLanguageService.ApplyToWindow(this);
    }

    public void Attach(RecompressViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (_closeRequested && e.PropertyName == nameof(RecompressViewModel.IsRunning) && !viewModel.IsRunning)
            {
                Dispatcher.BeginInvoke(new Action(Close));
            }
        };
    }

    public IReadOnlyList<string> PickChdFiles()
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = ArabicUi.Get("LocRecompress_AddFiles"),
            Filter = ArabicUi.Get("LocRecompress_FileFilter")
        };

        return dialog.ShowDialog(this) == true ? dialog.FileNames : [];
    }

    public string? PickFolder(string titleKey)
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
        // A running file is stopped and cleaned up by the service; closing waits for that.
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

    private void List_Drop(object sender, DragEventArgs e)
    {
        if (_viewModel is not { IsRunning: false } || e.Data.GetData(DataFormats.FileDrop) is not string[] dropped)
        {
            return;
        }

        var paths = new List<string>();
        foreach (string path in dropped)
        {
            try
            {
                if (System.IO.Directory.Exists(path))
                {
                    paths.AddRange(RecompressViewModel.EnumerateChdFiles(path));
                }
                else
                {
                    paths.Add(path);
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                global::Serilog.Log.Debug(ex, "Reading a dropped folder failed.");
            }
        }

        _viewModel.AddPaths(paths);
        e.Handled = true;
    }
}
