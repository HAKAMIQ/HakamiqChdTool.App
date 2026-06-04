namespace HakamiqChdTool.App.Models;

public enum SuspiciousArtifactKind
{
    Unknown = 0,
    WindowsPeSignature = 1,
    WindowsScript = 2,
    WindowsInstallerOrShortcut = 3,
    UnsafeArchiveEntryPath = 4,
    UnsafeDescriptorReference = 5,
    IsoContentNotScanned = 6,
    ChdInternalContentNotScanned = 7,
    UnconfirmedArchiveExecutable = 8,
    FolderScanLimitReached = 9
}
