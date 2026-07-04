namespace HakamiqChdTool.App.Services;

public sealed class ArchiveIntegrityResult
{
    public bool IsValid { get; init; }

    public bool WasCancelled { get; init; }

    public string? MessageResourceKey { get; init; }
}