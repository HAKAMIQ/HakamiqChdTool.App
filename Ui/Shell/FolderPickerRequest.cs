namespace HakamiqChdTool.App.Ui.Shell;

public sealed record FolderPickerRequest(
    string Title,
    string? SelectedPath = null,
    bool ShowNewFolderButton = true);
