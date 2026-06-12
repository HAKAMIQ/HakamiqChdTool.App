using HakamiqChdTool.App.Localization;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.ViewModels.Dialogs;

public sealed partial class RedumpDetailsDialogViewModel
{
        private static ParsedRedumpDetails Parse(string rawDetails, string fallbackSize, string fallbackSystem, string fallbackFileName)
        {
            rawDetails = Normalize(rawDetails);

            var files = new List<ParsedFile>();
            var current = new ParsedFileBuilder();
            bool hasCurrent = false;

            string gameName = string.Empty;
            string systemName = string.IsNullOrWhiteSpace(fallbackSystem) ? string.Empty : fallbackSystem;
            var notes = new List<string>();

            foreach (string rawLine in rawDetails.Split('\n'))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.Equals("الملفات المفحوصة:", StringComparison.OrdinalIgnoreCase)
                    || line.Equals("المسارات المُجزأة:", StringComparison.OrdinalIgnoreCase)
                    || line.Equals("تفاصيل المطابقة:", StringComparison.OrdinalIgnoreCase)
                    || line.Equals("نتائج المطابقة:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (line.StartsWith("إجمالي الملفات:", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("إجمالي الحجم:", StringComparison.OrdinalIgnoreCase))
                {
                    notes.Add(line);
                    continue;
                }

                if (line.StartsWith("• ", StringComparison.Ordinal))
                {
                    if (hasCurrent && !string.IsNullOrWhiteSpace(current.Name))
                    {
                        files.Add(current.Build());
                    }

                    ParsedFileHeader header = ParseFileHeader(line[2..].Trim());
                    current = new ParsedFileBuilder
                    {
                        Name = header.Name,
                        SizeDisplay = header.SizeDisplay
                    };
                    hasCurrent = true;
                    continue;
                }

                if (!hasCurrent && LooksLikeFileName(line))
                {
                    ParsedFileHeader header = ParseFileHeader(line);
                    current = new ParsedFileBuilder
                    {
                        Name = header.Name,
                        SizeDisplay = header.SizeDisplay
                    };
                    hasCurrent = true;
                    continue;
                }

                if (line.StartsWith("الحجم:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!hasCurrent)
                    {
                        current = new ParsedFileBuilder { Name = fallbackFileName };
                        hasCurrent = true;
                    }

                    current.SizeDisplay = ParseSizeText(line);
                    continue;
                }

                if (line.StartsWith("CRC:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!hasCurrent)
                    {
                        current = new ParsedFileBuilder { Name = fallbackFileName };
                        hasCurrent = true;
                    }

                    current.Crc = AfterColon(line);
                    continue;
                }

                if (line.StartsWith("MD5:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!hasCurrent)
                    {
                        current = new ParsedFileBuilder { Name = fallbackFileName };
                        hasCurrent = true;
                    }

                    current.Md5 = AfterColon(line);
                    continue;
                }

                if (line.StartsWith("SHA1:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!hasCurrent)
                    {
                        current = new ParsedFileBuilder { Name = fallbackFileName };
                        hasCurrent = true;
                    }

                    current.Sha1 = AfterColon(line);
                    continue;
                }

                if (line.StartsWith("اسم النظام:", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("النظام:", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("المنصة:", StringComparison.OrdinalIgnoreCase))
                {
                    systemName = AfterColon(line);
                    continue;
                }

                if (line.StartsWith("اللعبة:", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("الاسم القياسي:", StringComparison.OrdinalIgnoreCase))
                {
                    gameName = AfterColon(line);
                    continue;
                }

                if (line.StartsWith("اسم السجل:", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("السجل:", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(gameName))
                    {
                        gameName = AfterColon(line);
                    }

                    continue;
                }

                if (line.StartsWith("طريقة التحقق:", StringComparison.OrdinalIgnoreCase))
                {
                    notes.Add(line);
                    continue;
                }

                if (!LooksLikeNoise(line))
                {
                    notes.Add(line);
                }
            }

            if (hasCurrent && !string.IsNullOrWhiteSpace(current.Name))
            {
                files.Add(current.Build());
            }

            string totalSizeDisplay = files.Count > 0
                ? BuildTotalSize(files)
                : (string.IsNullOrWhiteSpace(fallbackSize) ? TechnicalUnavailable : fallbackSize);

            string filesSummary = BuildFilesSummary(files, fallbackFileName, totalSizeDisplay);
            string hashesSummary = BuildHashesSummary(files);
            string notesText = BuildNotesText(notes);

            return new ParsedRedumpDetails(gameName, systemName, totalSizeDisplay, filesSummary, hashesSummary, notesText);
        }

        private static string BuildStatusNote(string status)
        {
            if (status.Contains("سليم", StringComparison.OrdinalIgnoreCase))
            {
                return Text(StatusNoteMatchedKey);
            }

            if (status.Contains("غير مكتمل", StringComparison.OrdinalIgnoreCase))
            {
                return Text(StatusNotePartialKey);
            }

            if (status.Contains("بلا قاعدة", StringComparison.OrdinalIgnoreCase))
            {
                return Text(StatusNoteNoDatabaseKey);
            }

            if (status.Contains("متضاربة", StringComparison.OrdinalIgnoreCase))
            {
                return Text(StatusNoteConflictedKey);
            }

            if (status.Contains("تالف", StringComparison.OrdinalIgnoreCase) || status.Contains("معدل", StringComparison.OrdinalIgnoreCase))
            {
                return Text(StatusNoteModifiedOrCorruptKey);
            }

            return Text(StatusNoteGenericKey);
        }

        private static string BuildDefaultNotes(string status)
        {
            if (status.Contains("بلا قاعدة", StringComparison.OrdinalIgnoreCase))
            {
                return Text(DefaultNotesNoDatabaseKey);
            }

            return Text(DefaultNotesNoneKey);
        }

        private static string BuildFooterSummary(string status, string suggestedName)
        {
            if (string.IsNullOrWhiteSpace(suggestedName) || string.Equals(suggestedName, Text(NoSuggestedNameKey), StringComparison.Ordinal))
            {
                return status;
            }

            return status + " — " + suggestedName;
        }

        private static string BuildFilesSummary(IReadOnlyList<ParsedFile> files, string fallbackFileName, string fallbackSize)
        {
            if (files.Count == 0)
            {
                return BuildSingleFileSummary(fallbackFileName, fallbackSize);
            }

            return string.Join(Environment.NewLine, files.Select(static file => BuildSingleFileSummary(file.Name, file.SizeDisplay, file.SizeBytes)));
        }

        private static string BuildSingleFileSummary(string fileName, string sizeDisplay)
        {
            return BuildSingleFileSummary(fileName, sizeDisplay, 0);
        }

        private static string BuildSingleFileSummary(string fileName, string sizeDisplay, long sizeBytes)
        {
            ParsedFileHeader header = ParseFileHeader(fileName);
            string safeFileName = CleanTechnicalName(header.Name);
            string effectiveSize = string.IsNullOrWhiteSpace(header.SizeDisplay) ? sizeDisplay : header.SizeDisplay;
            string safeSize = sizeBytes > 0 ? FormatTechnicalByteSize(sizeBytes) : ToTechnicalSizeText(effectiveSize);

            return string.IsNullOrWhiteSpace(safeSize)
                ? safeFileName
                : string.Create(CultureInfo.InvariantCulture, $"{safeFileName} — {safeSize}");
        }

        private static string BuildHashesSummary(IReadOnlyList<ParsedFile> files)
        {
            if (files.Count == 0)
            {
                return string.Empty;
            }

            var blocks = new List<string>();
            bool includeFileHeader = files.Count > 1;

            foreach (ParsedFile file in files)
            {
                var builder = new StringBuilder();

                if (includeFileHeader)
                {
                    builder.AppendLine(CleanTechnicalName(file.Name));
                }

                if (!string.IsNullOrWhiteSpace(file.Crc))
                {
                    builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"CRC : {CleanHashValue(file.Crc)}"));
                }

                builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"MD5 : {TechnicalFallback(file.Md5)}"));
                builder.Append(string.Create(CultureInfo.InvariantCulture, $"SHA1: {TechnicalFallback(file.Sha1)}"));

                blocks.Add(builder.ToString());
            }

            return string.Join(Environment.NewLine + Environment.NewLine, blocks);
        }

        private static string BuildNotesText(IReadOnlyList<string> notes)
        {
            List<string> filtered = notes
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return filtered.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, filtered.Select(static value => "• " + value));
        }

        private static string BuildTotalSize(IReadOnlyList<ParsedFile> files)
        {
            long totalBytes = files.Sum(static file => file.SizeBytes);
            if (totalBytes > 0)
            {
                return FormatTechnicalByteSize(totalBytes);
            }

            string? firstNonEmpty = files
                .Select(static item => item.SizeDisplay)
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

            return string.IsNullOrWhiteSpace(firstNonEmpty)
                ? TechnicalUnavailable
                : ToTechnicalSizeText(firstNonEmpty!);
        }

        private static ParsedFileHeader ParseFileHeader(string value)
        {
            string text = StripLeadingBullet(NormalizeInline(value));
            if (string.IsNullOrWhiteSpace(text))
            {
                return new ParsedFileHeader(string.Empty, string.Empty);
            }

            string[] separators = { " — ", " – ", " - " };
            foreach (string separator in separators)
            {
                int index = text.LastIndexOf(separator, StringComparison.Ordinal);
                if (index <= 0 || index >= text.Length - separator.Length)
                {
                    continue;
                }

                string left = text[..index].Trim();
                string right = text[(index + separator.Length)..].Trim();

                if (!string.IsNullOrWhiteSpace(left) && LooksLikeSizeText(right))
                {
                    return new ParsedFileHeader(left, right);
                }
            }

            return new ParsedFileHeader(text, string.Empty);
        }

        private static string CleanTechnicalName(string value)
        {
            string text = StripLeadingBullet(NormalizeInline(value));
            text = RemoveBidiFormattingCharacters(text);
            text = NormalizeTechnicalDigits(text);

            return string.IsNullOrWhiteSpace(text) || string.Equals(text, Text(UnnamedFileKey), StringComparison.Ordinal)
                ? TechnicalUnnamedFile
                : text;
        }

        private static string ToTechnicalSizeText(string sizeDisplay)
        {
            string text = NormalizeTechnicalDigits(NormalizeInline(sizeDisplay));
            if (string.IsNullOrWhiteSpace(text)
                || string.Equals(text, Text(UnavailableKey), StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, Text(UnknownKey), StringComparison.OrdinalIgnoreCase))
            {
                return TechnicalUnavailable;
            }

            long bytes = ParseByteCountFromText(text);
            if (bytes > 0)
            {
                return FormatTechnicalByteSize(bytes);
            }

            string localizedByteUnit = Text(ByteUnitKey);
            if (!string.IsNullOrWhiteSpace(localizedByteUnit))
            {
                text = text.Replace(localizedByteUnit, TechnicalByteUnit, StringComparison.OrdinalIgnoreCase);
            }

            text = text.Replace("بايت", TechnicalByteUnit, StringComparison.OrdinalIgnoreCase);
            return NormalizeTechnicalDigits(RemoveBidiFormattingCharacters(text));
        }

        private static string ParseSizeText(string line)
        {
            string after = AfterColon(line);
            long bytes = ParseByteCountFromText(after);
            return bytes > 0 ? FormatTechnicalByteSize(bytes) : ToTechnicalSizeText(after);
        }

        private static long ParseByteCountFromText(string text)
        {
            text = NormalizeTechnicalDigits(text);

            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            Match match = Regex.Match(
                text,
                @"\((?<bytes>[0-9][0-9,\.]*)\s*(?:بايت|bytes)\)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!match.Success)
            {
                match = Regex.Match(
                    text,
                    @"^(?<bytes>[0-9][0-9,\.]*)\s*(?:بايت|bytes)$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            if (!match.Success)
            {
                return 0;
            }

            string digits = Regex.Replace(match.Groups["bytes"].Value, @"[^0-9]", string.Empty);
            return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long bytes) ? bytes : 0;
        }

        private static string FormatTechnicalByteSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = Math.Abs((double)bytes);
            int unitIndex = 0;

            while (value >= 1024d && unitIndex < units.Length - 1)
            {
                value /= 1024d;
                unitIndex++;
            }

            if (unitIndex == 0)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:N0} bytes", bytes);
            }

            return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1} ({2:N0} bytes)", value, units[unitIndex], bytes);
        }

        private static bool LooksLikeSizeText(string text)
        {
            return !string.IsNullOrWhiteSpace(text)
                && (Regex.IsMatch(text, @"\b(?:B|KB|MB|GB|TB)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                    || text.Contains("بايت", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("bytes", StringComparison.OrdinalIgnoreCase));
        }

        private static bool LooksLikeFileName(string line)
        {
            return line.Contains('.') && !line.Contains(':') && line.Length < 260;
        }

        private static bool LooksLikeNoise(string line)
        {
            return line.Equals("✓ مطابقة Redump:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLocalizedFallbackText(string value)
        {
            return string.Equals(value, Text(NoSuggestedNameKey), StringComparison.Ordinal)
                || string.Equals(value, Text(NoUsefulHashesKey), StringComparison.Ordinal)
                || string.Equals(value, Text(UnavailableKey), StringComparison.Ordinal)
                || string.Equals(value, Text(UnknownKey), StringComparison.Ordinal);
        }

        private static string BuildTechnicalDisplayText(string value)
        {
            string normalized = Normalize(RemoveBidiFormattingCharacters(value));
            normalized = NormalizeTechnicalDigits(normalized);

            return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized;
        }

        private static string RemoveBidiFormattingCharacters(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);

            foreach (char ch in value)
            {
                if (ch is '\u200E' or '\u200F'
                    or '\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E'
                    or '\u2066' or '\u2067' or '\u2068' or '\u2069')
                {
                    continue;
                }

                builder.Append(ch);
            }

            return builder.ToString();
        }

        private static string CleanHashValue(string value)
        {
            return NormalizeTechnicalDigits(RemoveBidiFormattingCharacters(value.Trim()));
        }

        private static string Normalize(string text)
        {
            return string.IsNullOrWhiteSpace(text)
                ? string.Empty
                : text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static string NormalizeInline(string text)
        {
            return Normalize(text).Replace('\n', ' ').Trim();
        }

        private static string StripLeadingBullet(string text)
        {
            string value = text.Trim();

            while (value.StartsWith('•'))
            {
                value = value[1..].TrimStart();
            }

            return value;
        }

        private static string AfterColon(string text)
        {
            int index = text.IndexOf(':');
            return index < 0 ? text.Trim() : text[(index + 1)..].Trim();
        }

        private static string TechnicalFallback(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? TechnicalUnavailable : CleanHashValue(value);
        }

        private static string NormalizeTechnicalDigits(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);

            foreach (char ch in value)
            {
                builder.Append(ch switch
                {
                    >= '\u0660' and <= '\u0669' => (char)('0' + (ch - '\u0660')),
                    >= '\u06F0' and <= '\u06F9' => (char)('0' + (ch - '\u06F0')),
                    _ => ch
                });
            }

            return builder.ToString();
        }

        private static string Text(string key)
        {
            return ArabicUi.Get(key);
        }

        private sealed record ParsedRedumpDetails(
            string GameName,
            string SystemName,
            string TotalSizeDisplay,
            string FilesSummary,
            string HashesSummary,
            string NotesText);

        private sealed record ParsedFileHeader(
            string Name,
            string SizeDisplay);

        private sealed record ParsedFile(
            string Name,
            string SizeDisplay,
            long SizeBytes,
            string Crc,
            string Md5,
            string Sha1);

        private sealed class ParsedFileBuilder
        {
            public string Name { get; set; } = string.Empty;
            public string SizeDisplay { get; set; } = string.Empty;
            public string Crc { get; set; } = string.Empty;
            public string Md5 { get; set; } = string.Empty;
            public string Sha1 { get; set; } = string.Empty;

            public ParsedFile Build()
            {
                long sizeBytes = ParseByteCountFromText(SizeDisplay ?? string.Empty);

                return new ParsedFile(
                    string.IsNullOrWhiteSpace(Name) ? Text(UnnamedFileKey) : Name,
                    string.IsNullOrWhiteSpace(SizeDisplay) ? TechnicalUnavailable : SizeDisplay,
                    sizeBytes,
                    Crc ?? string.Empty,
                    Md5 ?? string.Empty,
                    Sha1 ?? string.Empty);
            }
        }

}
