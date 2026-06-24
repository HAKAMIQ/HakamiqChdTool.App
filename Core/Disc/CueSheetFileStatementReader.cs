using System;

namespace HakamiqChdTool.App.Core.Disc;

internal static class CueSheetFileStatementReader
{
    public static bool TryRead(
        string? line,
        out string reference,
        out bool hasFileStatement) =>
        TryRead(line, requireFileType: false, out reference, out hasFileStatement);

    public static bool TryRead(
        string? line,
        bool requireFileType,
        out string reference,
        out bool hasFileStatement)
    {
        reference = string.Empty;
        hasFileStatement = false;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        string trimmed = line.TrimStart();
        if (!trimmed.StartsWith("FILE", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trimmed.Length > 4 && !char.IsWhiteSpace(trimmed[4]))
        {
            return false;
        }

        hasFileStatement = true;
        int index = 4;
        while (index < trimmed.Length && char.IsWhiteSpace(trimmed[index]))
        {
            index++;
        }

        if (index >= trimmed.Length)
        {
            return false;
        }

        if (trimmed[index] == '"')
        {
            int closingQuote = trimmed.IndexOf('"', index + 1);
            if (closingQuote <= index + 1)
            {
                return false;
            }

            if (requireFileType && string.IsNullOrWhiteSpace(trimmed[(closingQuote + 1)..]))
            {
                return false;
            }

            reference = trimmed[(index + 1)..closingQuote].Trim();
            return !string.IsNullOrWhiteSpace(reference);
        }

        string remainder = trimmed[index..].Trim();
        int lastWhitespace = remainder.LastIndexOfAny([' ', '\t']);
        if (lastWhitespace <= 0)
        {
            if (requireFileType)
            {
                return false;
            }

            reference = remainder;
            return !string.IsNullOrWhiteSpace(reference);
        }

        reference = remainder[..lastWhitespace].Trim();
        return !string.IsNullOrWhiteSpace(reference);
    }

    public static bool IsFileStatementLine(string? line)
    {
        _ = TryRead(line, out _, out bool hasFileStatement);
        return hasFileStatement;
    }
}
