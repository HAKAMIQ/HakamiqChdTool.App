using System.Text;

namespace HakamiqChdTool.App.Services;

public sealed record ChdmanProgressSnapshot(
    int? Percent,
    string RawLine,
    bool IsFinalizing,
    bool IsErrorLine);

public static class ChdmanOutputParser
{
    private static ChdProgressParser Parser => ChdProgressParser.Shared;

    public static ChdmanProgressSnapshot ParseSnapshot(
        string? line,
        bool isErrorLine,
        int? minimumPercent = null) =>
        Parser.ParseSnapshot(line, isErrorLine, minimumPercent);

    public static bool TryParseLastPercent(string? text, out int percent) =>
        Parser.TryParseLastPercent(text, out percent);

    public static bool TryParseProgressCompletePercent(string? text, out int percent) =>
        Parser.TryParseProgressCompletePercent(text, out percent);

    public static bool TryParseLastPercent(StringBuilder rolling, out int percent) =>
        Parser.TryParseLastPercent(rolling, out percent);

    public static bool TryParseActiveProgressPercent(StringBuilder rolling, out int percent) =>
        Parser.TryParseActiveProgressPercent(rolling, out percent);

    public static bool TryParseActiveProgressSnapshot(
        StringBuilder rolling,
        bool isErrorLine,
        int? minimumPercent,
        out ChdmanProgressSnapshot snapshot) =>
        Parser.TryParseActiveProgressSnapshot(rolling, isErrorLine, minimumPercent, out snapshot);

    public static string StripPercentTokensForNarrative(string? detail) =>
        Parser.StripPercentTokensForNarrative(detail);

    public static string ToCleanLogLine(string? line) =>
        Parser.ToCleanLogLine(line);
}
