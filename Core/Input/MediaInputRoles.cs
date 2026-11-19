using System;

namespace HakamiqChdTool.App.Core.Input;

public static class MediaInputRoles
{
    public static QueueInputRole ResolveQueueRole(MediaInputDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        QueueInputRole role = ResolveRole(
            descriptor.Kind,
            descriptor.Extension);

        if (!descriptor.Exists || string.IsNullOrWhiteSpace(descriptor.FullPath))
        {
            return role;
        }

        // ECM-compressed tracks: the set is queued once, through its first .ecm.
        if (role == QueueInputRole.ConvertibleDiscImage
            && string.Equals(NormalizeExtension(descriptor.Extension), EcmEvidence.EcmExtension, StringComparison.Ordinal))
        {
            return EcmEvidence.IsQueueEntry(descriptor.FullPath)
                ? QueueInputRole.ConvertibleDiscImage
                : QueueInputRole.DependentTrackFile;
        }

        if (descriptor.Kind == MediaInputKind.CUE
            && EcmEvidence.IsCueCoveredByEcm(descriptor.FullPath))
        {
            return QueueInputRole.DependentTrackFile;
        }

        return role;
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
                or MediaInputKind.CSO
                or MediaInputKind.ZSO
                or MediaInputKind.HardDiskImage =>
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
            ".toc" or ".nrg" or ".ccd" or ".mds" or ".ecm" =>
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