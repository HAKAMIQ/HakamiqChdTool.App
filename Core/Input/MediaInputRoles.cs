namespace HakamiqChdTool.App.Core.Input;

public static class MediaInputRoles
{
    public static QueueInputRole ResolveQueueRole(MediaInputDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return descriptor.Kind switch
        {
            MediaInputKind.CUE or MediaInputKind.GDI or MediaInputKind.ISO or MediaInputKind.CSO => QueueInputRole.ConvertibleDiscImage,
            MediaInputKind.CHD => QueueInputRole.ChdImage,
            MediaInputKind.BIN => QueueInputRole.BinCueRescueCandidate,
            _ => ResolveExtensionRole(descriptor.Extension)
        };
    }

    public static QueueInputRole ResolveExtensionRole(string? extension) => extension switch
    {
        ".cue" or ".gdi" or ".iso" or ".cso" or ".toc" or ".nrg" => QueueInputRole.ConvertibleDiscImage,
        ".chd" => QueueInputRole.ChdImage,
        ".bin" => QueueInputRole.BinCueRescueCandidate,
        ".zip" or ".rar" or ".7z" => QueueInputRole.ArchiveContainer,
        ".raw" => QueueInputRole.DependentTrackFile,
        _ => QueueInputRole.Unsupported
    };
}
