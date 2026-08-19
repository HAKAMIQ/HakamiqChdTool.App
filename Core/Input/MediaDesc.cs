namespace HakamiqChdTool.App.Core.Input;

public sealed record MediaInputDescriptor(
    string OriginalPath,
    string? FullPath,
    MediaInputKind Kind,
    bool Exists,
    bool IsDirectory,
    long? SizeBytes,
    string? Extension,
    string DetectionReason,
    MediaInputProbeStatus ProbeStatus = MediaInputProbeStatus.NotRequired)
{
    public bool IsFile => Exists && !IsDirectory;
}