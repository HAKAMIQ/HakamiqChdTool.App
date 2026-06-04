namespace HakamiqChdTool.UiPorts.Dialogs;

public interface IDialogService
{
    void ShowNotice(string title, string message);

    void ShowError(string title, string message);

    bool Confirm(string title, string message);
}
