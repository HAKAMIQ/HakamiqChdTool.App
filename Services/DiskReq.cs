namespace HakamiqChdTool.App.Services;

internal sealed class DiskSpaceRequirement
{
    public DiskSpaceRequirement(string driveRoot, long requiredBytes, string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driveRoot);

        DriveRoot = driveRoot;
        RequiredBytes = Math.Max(0, requiredBytes);
        Purpose = purpose ?? string.Empty;
    }

    public string DriveRoot { get; }

    public long RequiredBytes { get; }

    public string Purpose { get; }
}
