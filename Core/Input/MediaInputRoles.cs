using System;

namespace HakamiqChdTool.App.Core.Input;

public static class MediaInputRoles
{
    public static QueueInputRole ResolveQueueRole(MediaInputDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return ResolveRole(
            descriptor.Kind,
            descriptor.Extension);
    }

    public static QueueInputRole ResolveExtensionRole(string? extension)
    {
        return ResolveRole(
            MediaInputClassifier.ClassifyExtension(extension),
            extension);
    }

    private static QueueInputRole ResolveRole(
        MediaInputKind kind,
        string? extension)
    {
        QueueInputRole knownRole = ResolveKnownKindRole(kind);

        return knownRole != QueueInputRole.Unsupported
            ? knownRole
            : ResolveSupplementalExtensionRole(extension);
    }

    private static QueueInputRole ResolveKnownKindRole(MediaInputKind kind) =>
        kind switch
        {
            MediaInputKind.CUE
                or MediaInputKind.GDI
                or MediaInputKind.ISO
                or MediaInputKind.CSO =>
                QueueInputRole.ConvertibleDiscImage,

            MediaInputKind.CHD =>
                QueueInputRole.ChdImage,

            MediaInputKind.BIN =>
                QueueInputRole.BinCueRescueCandidate,

            _ =>
                QueueInputRole.Unsupported
        };

    private static QueueInputRole ResolveSupplementalExtensionRole(
        string? extension)
    {
        string normalized = NormalizeExtension(extension);

        return normalized switch
        {
            ".toc" or ".nrg" =>
                QueueInputRole.ConvertibleDiscImage,

            ".zip" or ".rar" or ".7z" =>
                QueueInputRole.ArchiveContainer,

            ".raw" =>
                QueueInputRole.DependentTrackFile,

            _ =>
                QueueInputRole.Unsupported
        };
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        string normalized = extension.Trim();

        if (normalized[0] != '.')
        {
            normalized = "." + normalized;
        }

        return normalized.ToLowerInvariant();
    }
}