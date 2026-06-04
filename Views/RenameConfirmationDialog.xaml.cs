using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.ViewModels;
using System.ComponentModel;
using System.Windows;

namespace HakamiqChdTool.App.Views;

public partial class RenameConfirmationDialog : Window
{
    private readonly RenameConfirmationViewModel _viewModel;

    public RenameConfirmationDialog(RenameConfirmationViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
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
        if (e.PropertyName != nameof(RenameConfirmationViewModel.DialogResult))
        {
            return;
        }

        if (_viewModel.DialogResult.HasValue)
        {
            DialogResult = _viewModel.DialogResult;
        }
    }
}
