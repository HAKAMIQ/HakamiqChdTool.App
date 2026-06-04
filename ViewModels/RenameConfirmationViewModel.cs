using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HakamiqChdTool.App.Localization;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class RenameConfirmationViewModel : ObservableObject
{
    private bool? _dialogResult;

    public RenameConfirmationViewModel(string fileName)
        : this(
            fileName,
            ArabicUi.Get(MainWindowMessages.RenameDialogHeading),
            ArabicUi.Get(MainWindowMessages.RenameDialogQuestion),
            ArabicUi.Get(MainWindowMessages.RenameDialogOk),
            ArabicUi.Get(MainWindowMessages.RenameDialogCancel),
            ArabicUi.Get(MainWindowMessages.RenameDialogTitle))
    {
    }

    public RenameConfirmationViewModel(string fileName, string heading, string question, string confirmText, string cancelText, string title)
    {
        Title = title;
        Heading = heading;
        Question = question;
        ConfirmText = confirmText;
        CancelText = cancelText;
        FileName = fileName;

        ConfirmCommand = new RelayCommand(OnConfirm);
        CancelCommand = new RelayCommand(OnCancel);
    }

    public string Title { get; }
    public string Heading { get; }
    public string FileName { get; }
    public string Question { get; }
    public string ConfirmText { get; }
    public string CancelText { get; }

    public bool? DialogResult
    {
        get => _dialogResult;
        private set => SetProperty(ref _dialogResult, value);
    }

    public IRelayCommand ConfirmCommand { get; }
    public IRelayCommand CancelCommand { get; }

    private void OnConfirm() => DialogResult = true;

    private void OnCancel() => DialogResult = false;
}
