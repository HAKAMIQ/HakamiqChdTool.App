using System.IO;

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
        string extension = descriptor.Extension;

        QueueInputRole role = descriptor.Kind switch
        {
            MediaInputKind.CUE or MediaInputKind.GDI or MediaInputKind.ISO or MediaInputKind.CSO => QueueInputRole.ConvertibleDiscImage,
            MediaInputKind.CHD => QueueInputRole.ChdImage,
            MediaInputKind.BIN => QueueInputRole.BinCueRescueCandidate,
            _ => ResolveLegacyRole(extension)
        };

        return new QueueInputClassification(role, extension);
    }


    private static QueueInputRole ResolveLegacyRole(string extension) => extension switch
    {
        ".toc" or ".nrg" => QueueInputRole.ConvertibleDiscImage,
        ".zip" or ".rar" or ".7z" => QueueInputRole.ArchiveContainer,
        ".raw" => QueueInputRole.DependentTrackFile,
        _ => QueueInputRole.Unsupported
    };

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

        return Classify("input" + normalized).IsSupported;
    }
}
