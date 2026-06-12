namespace HakamiqChdTool.App.Ui.Shell;

public sealed record FilePickerRequest(
    string Title,
    string Filter,
    string? InitialDirectory = null,
    string? FileName = null,
    bool AllowMultiple = false);
