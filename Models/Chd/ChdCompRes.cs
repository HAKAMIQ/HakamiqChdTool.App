namespace HakamiqChdTool.App.Models.Chd;

public sealed record ChdCompressionResolution(
    string RequestedPreset,
    string ResolvedCompression,
    string EffectiveCompression,
    bool SameAsMameDefault,
    string? TruthNoteKey)
{
    public const string MameCreateCdDefaultCompression = "cdlz,cdzl,cdfl";

    public static ChdCompressionResolution NotApplicable { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        false,
        null);

    public string LogResolvedCompression => string.IsNullOrWhiteSpace(ResolvedCompression)
        ? "default"
        : ResolvedCompression;
}