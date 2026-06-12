using HakamiqChdTool.App.Services.StorageAdvisor;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace HakamiqChdTool.App.Views;

public partial class StorageAdvisorDialog : Window
{
    private readonly StorageAdvisorDialogViewModel _viewModel;

    internal StorageAdvisorDialog(StorageAdvisorView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        InitializeComponent();
        HakamiqChdTool.App.Ui.Shell.WindowBackdrop.ApplyDialog(this);

        _viewModel = new StorageAdvisorDialogViewModel(view);
        DataContext = _viewModel;
    }

    internal StorageAdvisorDialogResult AdvisorResult { get; private set; } =
        StorageAdvisorDialogResult.Cancel;

    internal bool DoNotShowAgain => _viewModel.DoNotShowAgain;

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
        CloseWithResult(StorageAdvisorDialogResult.Cancel, false);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CloseWithResult(StorageAdvisorDialogResult.Cancel, false);
    }

    private void OpenOptionsButton_Click(object sender, RoutedEventArgs e)
    {
        CloseWithResult(StorageAdvisorDialogResult.OpenOptions, true);
    }

    private void ContinueRecommendedButton_Click(object sender, RoutedEventArgs e)
    {
        CloseWithResult(StorageAdvisorDialogResult.ContinueRecommended, true);
    }

    private void CloseWithResult(
        StorageAdvisorDialogResult result,
        bool dialogResult)
    {
        AdvisorResult = result;

        try
        {
            DialogResult = dialogResult;
        }
        catch (InvalidOperationException)
        {
        }

        Close();
    }

    private sealed class StorageAdvisorDialogViewModel
    {
        internal StorageAdvisorDialogViewModel(StorageAdvisorView view)
        {
            ArgumentNullException.ThrowIfNull(view);

            Paths = view.Paths;
            Messages = view.Messages;
            HasBlockingIssue = view.HasBlockingIssue;
            HasWarningOrHigher = view.HasWarningOrHigher;
            ShouldShowDialog = view.ShouldShowDialog;
        }

        public IReadOnlyList<StoragePathView> Paths { get; }

        public IReadOnlyList<StorageMessageView> Messages { get; }

        public bool HasBlockingIssue { get; }

        public bool HasWarningOrHigher { get; }

        public bool ShouldShowDialog { get; }

        public bool DoNotShowAgain { get; set; }
    }
}

internal enum StorageAdvisorDialogResult
{
    Cancel = 0,
    ContinueRecommended = 1,
    OpenOptions = 2
}