using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HakamiqChdTool.App.Core.Workflow.Extraction;

internal sealed record CueSheetFileReference(
    int LineIndex,
    string Reference,
    int? TrackNumber,
    string TrackType,
    bool IsHighDensityArea);

internal sealed record CueSheetReadResult(
    IReadOnlyList<string> Lines,
    IReadOnlyList<CueSheetFileReference> References,
    bool IsLikelyGdRom);

internal sealed class CueSheetReferenceReader
{
    private const int MaxCueBytes = 1024 * 1024;
    private const int MaxCueLines = 8192;
    private const string InvalidCueKey = "LocWorkflow_ExtractedCueBinInvalid";

    public bool TryRead(
        string cuePath,
        out CueSheetReadResult result,
        out string failureMessageKey)
    {
        result = new CueSheetReadResult([], [], false);
        failureMessageKey = InvalidCueKey;

        if (string.IsNullOrWhiteSpace(cuePath)
            || !string.Equals(Path.GetExtension(cuePath), ".cue", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(cuePath))
        {
            return false;
        }

        try
        {
            var info = new FileInfo(cuePath);
            if (info.Length <= 0 || info.Length > MaxCueBytes)
            {
                return false;
            }

            string[] lines = File.ReadAllLines(cuePath, Encoding.UTF8);
            if (lines.Length == 0 || lines.Length > MaxCueLines)
            {
                return false;
            }

            if (!TryParseCueLines(lines, out IReadOnlyList<CueSheetFileReference> references, out bool isLikelyGdRom))
            {
                return false;
            }

            result = new CueSheetReadResult(lines, references, isLikelyGdRom);
            failureMessageKey = string.Empty;
            return true;
        }
        catch (Exception ex) when (IsExpectedReadFailure(ex))
        {
            return false;
        }
    }

    private static bool TryParseCueLines(
        IReadOnlyList<string> lines,
        out IReadOnlyList<CueSheetFileReference> references,
        out bool isLikelyGdRom)
    {
        var entries = new List<CueFileEntry>();
        isLikelyGdRom = false;

        CueFileEntry? currentFile = null;
        bool pendingHighDensityArea = false;

        for (int index = 0; index < lines.Count; index++)
        {
            string line = lines[index];
            string trimmed = line.TrimStart();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (IsRemHighDensityArea(trimmed))
            {
                isLikelyGdRom = true;
                pendingHighDensityArea = true;
                if (currentFile is not null && currentFile.TrackNumber.HasValue)
                {
                    currentFile.IsHighDensityArea = true;
                }

                continue;
            }

            if (TryReadFileStatement(line, out string reference, out bool hasFileStatement))
            {
                currentFile = new CueFileEntry(index, reference);
                entries.Add(currentFile);
                continue;
            }

            if (hasFileStatement)
            {
                references = [];
                return false;
            }

            if (TryReadTrackStatement(trimmed, out int trackNumber, out string trackType))
            {
                if (currentFile is null)
                {
                    references = [];
                    return false;
                }

                if (!currentFile.TrackNumber.HasValue)
                {
                    currentFile.TrackNumber = trackNumber;
                    currentFile.TrackType = trackType;
                    currentFile.IsHighDensityArea = currentFile.IsHighDensityArea || pendingHighDensityArea;
                    pendingHighDensityArea = false;
                }

                continue;
            }
        }

        if (entries.Count == 0)
        {
            references = [];
            return false;
        }

        references = [
            .. entries.Select(static entry => new CueSheetFileReference(
                entry.LineIndex,
                entry.Reference,
                entry.TrackNumber,
                entry.TrackType,
                entry.IsHighDensityArea))
        ];

        return references.Count > 0;
    }

    private static bool TryReadFileStatement(
        string line,
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

            reference = trimmed[(index + 1)..closingQuote].Trim();
            return !string.IsNullOrWhiteSpace(reference);
        }

        string remainder = trimmed[index..].Trim();
        int lastWhitespace = remainder.LastIndexOfAny([' ', '\t']);
        if (lastWhitespace <= 0)
        {
            return false;
        }

        reference = remainder[..lastWhitespace].Trim();
        return !string.IsNullOrWhiteSpace(reference);
    }

    private static bool TryReadTrackStatement(
        string trimmed,
        out int trackNumber,
        out string trackType)
    {
        trackNumber = 0;
        trackType = string.Empty;

        if (!trimmed.StartsWith("TRACK", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trimmed.Length > 5 && !char.IsWhiteSpace(trimmed[5]))
        {
            return false;
        }

        string[] parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !int.TryParse(parts[1], out int parsedTrackNumber) || parsedTrackNumber <= 0)
        {
            return false;
        }

        trackNumber = parsedTrackNumber;
        trackType = parts[2].Trim();
        return !string.IsNullOrWhiteSpace(trackType);
    }

    private static bool IsRemHighDensityArea(string trimmed) =>
        trimmed.StartsWith("REM", StringComparison.OrdinalIgnoreCase)
        && trimmed.Contains("HIGH-DENSITY AREA", StringComparison.OrdinalIgnoreCase);

    private static bool IsExpectedReadFailure(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException
        or PathTooLongException
        or InvalidOperationException
        or System.Security.SecurityException;

    private sealed class CueFileEntry(int lineIndex, string reference)
    {
        public int LineIndex { get; } = lineIndex;
        public string Reference { get; } = reference;
        public int? TrackNumber { get; set; }
        public string TrackType { get; set; } = string.Empty;
        public bool IsHighDensityArea { get; set; }
    }
}
