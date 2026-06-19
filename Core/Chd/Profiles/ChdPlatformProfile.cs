namespace HakamiqChdTool.App.Core.Chd.Profiles;

public sealed record ChdPlatformProfile(
    string PlatformId,
    string DisplayName,
    string[] InputExtensions,
    ChdCommandKind CommandKind,
    ChdInputPreparationKind PreparationKind,
    bool RequiresToc,
    bool RequiresDvdSectorAlignment,
    int? HunkSize,
    string Note);
