using HakamiqChdTool.App.Services.StorageAdvisor;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace HakamiqChdTool.App.Views;

public partial class StorageAdvisorDialog : Window
{
    private readonly StorageAdvisorDialogViewModel _viewModel;

    internal StorageAdvisorDialog(StorageAdvisorPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        InitializeComponent();

        _viewModel = new StorageAdvisorDialogViewModel(presentation);
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

    private void OpenAdvancedOptionsButton_Click(object sender, RoutedEventArgs e)
    {
        CloseWithResult(StorageAdvisorDialogResult.OpenAdvancedOptions, true);
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
        internal StorageAdvisorDialogViewModel(StorageAdvisorPresentation presentation)
        {
            ArgumentNullException.ThrowIfNull(presentation);

            Paths = presentation.Paths;
            Messages = presentation.Messages;
            HasBlockingIssue = presentation.HasBlockingIssue;
            HasWarningOrHigher = presentation.HasWarningOrHigher;
            ShouldShowDialog = presentation.ShouldShowDialog;
        }

        public IReadOnlyList<StorageAdvisorPathPresentation> Paths { get; }

        public IReadOnlyList<StorageAdvisorMessagePresentation> Messages { get; }

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
    OpenAdvancedOptions = 2
}