namespace HakamiqChdTool.App.Core.Input;

public enum QueueInputRole
{
    Unsupported = 0,
    ConvertibleDiscImage = 1,
    ArchiveContainer = 2,
    ChdImage = 3,
    BinCueRescueCandidate = 4,
    DependentTrackFile = 5
}

public readonly record struct QueueInputClassification(QueueInputRole Role, string Extension)
{
    public bool IsSupported =>
        Role is QueueInputRole.ConvertibleDiscImage
            or QueueInputRole.ArchiveContainer
            or QueueInputRole.ChdImage
            or QueueInputRole.BinCueRescueCandidate;

    public bool IsConvertibleDiscImage =>
        Role is QueueInputRole.ConvertibleDiscImage
            or QueueInputRole.BinCueRescueCandidate;

    public bool IsArchiveContainer => Role == QueueInputRole.ArchiveContainer;

    public bool IsChdImage => Role == QueueInputRole.ChdImage;

    public bool IsBinCueRescueCandidate => Role == QueueInputRole.BinCueRescueCandidate;

    public bool IsDependentTrackFile => Role == QueueInputRole.DependentTrackFile;
}

public static class QueueInputClassifier
{
    public static QueueInputClassification Classify(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new QueueInputClassification(QueueInputRole.Unsupported, string.Empty);
        }

        MediaInputDescriptor descriptor = MediaInputClassifier.Shared.Classify(path);
        string extension = descriptor.Extension ?? string.Empty;

        QueueInputRole role = MediaInputPipeline.Decide(descriptor).QueueRole;

        return new QueueInputClassification(role, extension);
    }

    public static bool IsConvertibleDiscImagePath(string? path) =>
        Classify(path).IsConvertibleDiscImage;

    public static bool IsArchiveContainerPath(string? path) =>
        Classify(path).IsArchiveContainer;

    public static bool IsChdImagePath(string? path) =>
        Classify(path).IsChdImage;

    public static bool IsDependentTrackFilePath(string? path) =>
        Classify(path).IsDependentTrackFile;

    public static bool IsSupportedExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        string trimmed = extension.Trim();
        string normalized = trimmed.StartsWith('.')
            ? trimmed
            : "." + trimmed;

        return new QueueInputClassification(
            MediaInputRoles.ResolveExtensionRole(normalized),
            normalized).IsSupported;
    }
}
