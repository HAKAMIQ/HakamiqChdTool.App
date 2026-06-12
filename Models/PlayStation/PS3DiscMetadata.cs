namespace HakamiqChdTool.App.Models.PlayStation;

public sealed record PS3DiscMetadata(
    string SourcePath,
    string? TitleId,
    string? TitleName,
    string? DiscId,
    bool HasPs3GameFolder,
    bool HasParamSfo,
    bool HasEbootBin,
    bool HasPs3DiscSfb);