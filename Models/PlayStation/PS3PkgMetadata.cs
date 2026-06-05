namespace HakamiqChdTool.App.Models.PlayStation;

public sealed record PS3PkgMetadata(
    bool IsValidPackage,
    string? ContentId,
    string? TitleId,
    PS3ContentKind ContentKind,
    bool IsProbablyEncrypted);
