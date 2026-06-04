namespace HakamiqChdTool.UiPorts.Pickers;

public sealed record FolderPickerRequest(
    string Title,
    string? SelectedPath = null,
    bool ShowNewFolderButton = true);
