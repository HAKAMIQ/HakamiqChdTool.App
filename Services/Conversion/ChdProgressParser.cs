using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.Services;

public sealed class ChdProgressParser : IChdProgressParser
{
    public static ChdProgressParser Shared { get; } = new();

    private static readonly Regex PercentTokenRegex = new(
        @"\b(100(?:\.0+)?|\d{1,2}(?:\.\d+)?)\s*%",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ProgressCompletePercentRegex = new(
        @"\b(100(?:\.0+)?|\d{1,2}(?:\.\d+)?)\s*%\s*complete\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex ControlChars = new(
        @"[\u0000-\u0008\u000B\u000C\u000E-\u001F]",
        RegexOptions.Compiled);

    public ChdmanProgressSnapshot ParseSnapshot(
        string? line,
        bool isErrorLine,
        int? minimumPercent = null)
    {
        string rawLine = line ?? string.Empty;
        int? percent = null;

        if (TryParseProgressCompletePercent(rawLine, out int parsedPercent) ||
            (IsSafeGenericProgressLine(rawLine) && TryParseLastPercent(rawLine, out parsedPercent)))
        {
            percent = minimumPercent is int floor
                ? Math.Max(parsedPercent, Math.Clamp(floor, 0, 100))
                : parsedPercent;
        }

        bool isFinalizing =
            rawLine.Contains("final", StringComparison.OrdinalIgnoreCase) ||
            rawLine.Contains("complete", StringComparison.OrdinalIgnoreCase) ||
            rawLine.Contains("moving", StringComparison.OrdinalIgnoreCase) ||
            rawLine.Contains("compressing", StringComparison.OrdinalIgnoreCase) && percent >= 99;

        return new ChdmanProgressSnapshot(
            Percent: percent,
            RawLine: rawLine,
            IsFinalizing: isFinalizing,
            IsErrorLine: isErrorLine);
    }

    public bool TryParseLastPercent(string? text, out int percent)
    {
        percent = 0;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        MatchCollection matches = PercentTokenRegex.Matches(text);
        if (matches.Count == 0)
        {
            return false;
        }

        Match last = matches[^1];
        string raw = last.Groups[1].Value;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return false;
        }

        percent = (int)Math.Round(parsed, MidpointRounding.AwayFromZero);
        percent = Math.Clamp(percent, 0, 100);
        return true;
    }

    public bool TryParseProgressCompletePercent(string? text, out int percent)
    {
        percent = 0;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        MatchCollection matches = ProgressCompletePercentRegex.Matches(text);
        if (matches.Count == 0)
        {
            return false;
        }

        Match last = matches[^1];
        if (!double.TryParse(
                last.Groups[1].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed))
        {
            return false;
        }

        percent = Math.Clamp((int)Math.Round(parsed, MidpointRounding.AwayFromZero), 0, 100);
        return true;
    }

    public bool TryParseLastPercent(StringBuilder rolling, out int percent) =>
        TryParseLastPercent(rolling.Length == 0 ? null : rolling.ToString(), out percent);

    public bool TryParseActiveProgressPercent(StringBuilder rolling, out int percent)
    {
        percent = 0;

        string activeSegment = GetActiveProgressSegment(rolling);
        if (activeSegment.Length == 0)
        {
            return false;
        }

        return TryParseLastPercent(activeSegment, out percent);
    }

    public bool TryParseActiveProgressSnapshot(
        StringBuilder rolling,
        bool isErrorLine,
        int? minimumPercent,
        out ChdmanProgressSnapshot snapshot)
    {
        snapshot = new ChdmanProgressSnapshot(
            Percent: null,
            RawLine: string.Empty,
            IsFinalizing: false,
            IsErrorLine: isErrorLine);

        string activeSegment = GetActiveProgressSegment(rolling);
        if (activeSegment.Length == 0)
        {
            return false;
        }

        snapshot = ParseSnapshot(activeSegment, isErrorLine, minimumPercent);
        return snapshot.Percent is not null || snapshot.IsFinalizing;
    }

    public string StripPercentTokensForNarrative(string? detail)
    {
        if (string.IsNullOrEmpty(detail))
        {
            return string.Empty;
        }

        return PercentTokenRegex.Replace(detail, string.Empty).Trim();
    }

    public string ToCleanLogLine(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return string.Empty;
        }

        string clean = line.Replace("\r", " ", StringComparison.Ordinal).Trim();
        clean = ControlChars.Replace(clean, string.Empty);
        return clean.Trim();
    }

    private static bool IsSafeGenericProgressLine(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        if (line.Contains("ratio", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (line.Contains("final", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static string GetActiveProgressSegment(StringBuilder rolling)
    {
        if (rolling.Length == 0)
        {
            return string.Empty;
        }

        string text = rolling.ToString();
        int end = text.Length;

        while (end > 0 && (text[end - 1] == '\r' || text[end - 1] == '\n'))
        {
            end--;
        }

        if (end <= 0)
        {
            return string.Empty;
        }

        int lastCarriageReturn = text.LastIndexOf('\r', end - 1);
        int lastNewLine = text.LastIndexOf('\n', end - 1);
        int lastBreak = Math.Max(lastCarriageReturn, lastNewLine);

        string activeSegment = lastBreak >= 0
            ? text[(lastBreak + 1)..end]
            : text[..end];

        return activeSegment.Trim();
    }
}
