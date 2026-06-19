namespace HakamiqChdTool.App.Core.Verification;

public sealed record GameHashSet(
    string SourceName,
    string SourceDate,
    string SystemName,
    IReadOnlyList<GameHashEntry> Entries)
{
    public static GameHashSet Empty { get; } = new(string.Empty, string.Empty, string.Empty, []);
}
