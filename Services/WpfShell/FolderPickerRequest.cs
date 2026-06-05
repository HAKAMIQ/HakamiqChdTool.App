namespace HakamiqChdTool.App.Services.WpfShell;

public sealed record FolderPickerRequest(
    string Title,
    string? SelectedPath = null,
    bool ShowNewFolderButton = true);
