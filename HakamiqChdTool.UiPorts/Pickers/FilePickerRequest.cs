namespace HakamiqChdTool.UiPorts.Pickers;

public sealed record FilePickerRequest(
    string Title,
    string Filter,
    string? InitialDirectory = null,
    string? FileName = null,
    bool AllowMultiple = false);
