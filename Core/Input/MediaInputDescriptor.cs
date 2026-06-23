namespace HakamiqChdTool.App.Core.Input;

public sealed record MediaInputDescriptor(
    string OriginalPath,
    string FullPath,
    MediaInputKind Kind,
    string Extension,
    bool Exists,
    bool IsDirectory,
    string FailureMessageKey)
{
    public bool IsFile => Exists && !IsDirectory;

    public bool IsKnownDiscFileKind => Kind is MediaInputKind.ISO
        or MediaInputKind.CHD
        or MediaInputKind.CSO
        or MediaInputKind.CUE
        or MediaInputKind.BIN
        or MediaInputKind.GDI;

    public bool IsKnownContainerKind => Kind is MediaInputKind.Folder or MediaInputKind.PKG;
}
