using System;
using System.ComponentModel;
using System.Windows;

using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.ViewModels;

namespace HakamiqChdTool.App.Views;

public partial class RenameConfirmationDialog : Window
{
    private readonly RenameConfirmationViewModel _viewModel;

    public RenameConfirmationDialog(RenameConfirmationViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        HakamiqChdTool.App.Ui.Shell.WindowBackdrop.ApplyDialog(this);
        AppLanguageService.ApplyToWindow(this);

        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Closed -= OnClosed;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!StringComparer.Ordinal.Equals(e.PropertyName, nameof(RenameConfirmationViewModel.DialogResult)))
        {
            return;
        }

        bool? result = _viewModel.DialogResult;
        if (!result.HasValue)
        {
            return;
        }

        CloseWithResult(result.Value);
    }

    private void CloseWithResult(bool result)
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