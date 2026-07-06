using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace HakamiqChdTool.App.Services;

public sealed class RedumpLocalLibraryIndexer
{
    public const string CandidateStatus = "Candidate";
    public const string SelectedStatus = "Selected";
    public const string OlderStatus = "Older";
    public const string DuplicateStatus = "Duplicate";
    public const string VariantStatus = "Variant";
    public const string ReadErrorStatus = "ReadError";

    private const int MaxPreviewElementReads = 4096;
    private const int MaxPreviewGameCount = 64;

    private static readonly Regex DateTokenRegex = new(
        @"(?<date>\d{4}[-_]\d{2}[-_]\d{2})(?:[ T_](?<time>\d{2}[-:]\d{2}[-:]\d{2}))?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WhitespaceRegex = new(
        @"\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex VariantParenthesisRegex = new(
        @"\((?=[^)]*(serial|version))[^)]*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd HH-mm-ss",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd",
        "yyyy_MM_dd HH-mm-ss",
        "yyyy_MM_dd HH:mm:ss",
        "yyyy_MM_dd",
        "yyyyMMdd"
    };

    public Task<RedumpLocalLibraryIndexResult> IndexAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => IndexCore(rootPath, cancellationToken),
            cancellationToken);
    }

    private static RedumpLocalLibraryIndexResult IndexCore(
        string rootPath,
        CancellationToken cancellationToken)
    {
        DateTime startedUtc = DateTime.UtcNow;
        string normalizedRoot = rootPath?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedRoot)) {
            return RedumpLocalLibraryIndexResult.Empty(
                string.Empty,
                startedUtc,
                DateTime.UtcNow,
                "Redump local library root is empty.");
        }

        if (!Directory.Exists(normalizedRoot)) {
            return RedumpLocalLibraryIndexResult.Empty(
                normalizedRoot,
                startedUtc,
                DateTime.UtcNow,
                "Redump local library root does not exist.");
        }

        List<string> errors = new();
        List<RedumpLocalLibraryDatEntry> entries = new();
        int totalDatXmlFiles = 0;

        foreach (string filePath in EnumerateDatXmlFiles(normalizedRoot, errors, cancellationToken)) {
            cancellationToken.ThrowIfCancellationRequested();
            totalDatXmlFiles++;

            try {
                entries.Add(ReadEntry(filePath, cancellationToken));
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch (Exception ex) {
                errors.Add($"{filePath}: {ex.Message}");
                entries.Add(CreateReadErrorEntry(filePath, ex));
            }
        }

        IReadOnlyList<RedumpLocalLibraryDatEntry> selectedEntries = SelectLatestPerPlatform(entries);

        int platformCount = selectedEntries
            .Where(entry => !IsReadError(entry))
            .Select(entry => entry.PlatformKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        int selectedCount = selectedEntries.Count(entry => IsStatus(entry, SelectedStatus));
        int olderCount = selectedEntries.Count(entry => IsStatus(entry, OlderStatus));
        int duplicateCount = selectedEntries.Count(entry => IsStatus(entry, DuplicateStatus));
        int variantCount = selectedEntries.Count(entry => IsStatus(entry, VariantStatus));
        int readErrorCount = selectedEntries.Count(entry => IsStatus(entry, ReadErrorStatus));

        return new RedumpLocalLibraryIndexResult(
            normalizedRoot,
            totalDatXmlFiles,
            platformCount,
            selectedCount,
            olderCount,
            duplicateCount,
            variantCount,
            readErrorCount,
            startedUtc,
            DateTime.UtcNow,
            selectedEntries,
            errors);
    }

    private static IEnumerable<string> EnumerateDatXmlFiles(
        string rootPath,
        IList<string> errors,
        CancellationToken cancellationToken)
    {
        Stack<string> pending = new();
        pending.Push(rootPath);

        while (pending.Count > 0) {
            cancellationToken.ThrowIfCancellationRequested();

            string currentDirectory = pending.Pop();

            string[] files;
            try {
                files = Directory.GetFiles(currentDirectory);
            }
            catch (Exception ex) {
                errors.Add($"{currentDirectory}: {ex.Message}");
                continue;
            }

            foreach (string file in files) {
                cancellationToken.ThrowIfCancellationRequested();

                string extension = Path.GetExtension(file);
                bool isDat = extension.Equals(".dat", StringComparison.OrdinalIgnoreCase);
                bool isXml = extension.Equals(".xml", StringComparison.OrdinalIgnoreCase);

                if (!isDat && !isXml) {
                    continue;
                }

                yield return file;
            }

            string[] directories;
            try {
                directories = Directory.GetDirectories(currentDirectory);
            }
            catch (Exception ex) {
                errors.Add($"{currentDirectory}: {ex.Message}");
                continue;
            }

            foreach (string directory in directories) {
                pending.Push(directory);
            }
        }
    }

    private static RedumpLocalLibraryDatEntry ReadEntry(
        string filePath,
        CancellationToken cancellationToken)
    {
        FileInfo info = new(filePath);

        string? name = null;
        string? description = null;
        string? version = null;
        int gameCount = 0;

        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            XmlResolver = null,
            MaxCharactersFromEntities = 1024,
            MaxCharactersInDocument = 1024 * 1024
        };

        using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            FileOptions.SequentialScan);

        using XmlReader reader = XmlReader.Create(stream, settings);

        bool insideHeader = false;
        int headerDepth = -1;
        int elementReads = 0;

        while (reader.Read()) {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.Element) {
                elementReads++;

                if (elementReads > MaxPreviewElementReads) {
                    break;
                }

                string localName = reader.LocalName;

                if (localName.Equals("header", StringComparison.OrdinalIgnoreCase)) {
                    insideHeader = true;
                    headerDepth = reader.Depth;
                    continue;
                }

                if (IsGameElement(localName)) {
                    gameCount++;

                    if (gameCount >= MaxPreviewGameCount) {
                        break;
                    }

                    continue;
                }

                if (insideHeader) {
                    if (name is null && localName.Equals("name", StringComparison.OrdinalIgnoreCase)) {
                        name = ReadSmallElementText(reader);
                        continue;
                    }

                    if (description is null && localName.Equals("description", StringComparison.OrdinalIgnoreCase)) {
                        description = ReadSmallElementText(reader);
                        continue;
                    }

                    if (version is null && localName.Equals("version", StringComparison.OrdinalIgnoreCase)) {
                        version = ReadSmallElementText(reader);
                        continue;
                    }
                }
            }

            if (insideHeader &&
                reader.NodeType == XmlNodeType.EndElement &&
                reader.Depth == headerDepth &&
                reader.LocalName.Equals("header", StringComparison.OrdinalIgnoreCase)) {
                insideHeader = false;
            }
        }

        DateTime? datDateUtc = TryParseDatDate(version);
        if (!datDateUtc.HasValue) {
            datDateUtc = TryParseDatDate(description);
        }

        if (!datDateUtc.HasValue) {
            datDateUtc = TryParseDatDate(info.Name);
        }

        string platformSource = FirstNonEmpty(
            name,
            description,
            Path.GetFileNameWithoutExtension(filePath));

        string platformKey = NormalizePlatformKey(platformSource);

        int? previewGameCount = null;
        if (gameCount > 0) {
            previewGameCount = gameCount;
        }

        return new RedumpLocalLibraryDatEntry(
            info.FullName,
            info.Name,
            info.DirectoryName ?? string.Empty,
            info.Extension,
            platformKey,
            CleanNullable(name),
            CleanNullable(description),
            CleanNullable(version),
            datDateUtc,
            previewGameCount,
            false,
            CandidateStatus,
            null,
            info.Length,
            info.LastWriteTimeUtc);
    }

    private static IReadOnlyList<RedumpLocalLibraryDatEntry> SelectLatestPerPlatform(
        IReadOnlyList<RedumpLocalLibraryDatEntry> entries)
    {
        Dictionary<string, RedumpLocalLibraryDatEntry> byPath = entries.ToDictionary(
            entry => entry.FilePath,
            StringComparer.OrdinalIgnoreCase);

        IEnumerable<IGrouping<string, RedumpLocalLibraryDatEntry>> groups = entries
            .Where(entry => !IsReadError(entry))
            .GroupBy(entry => entry.PlatformKey, StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, RedumpLocalLibraryDatEntry> group in groups) {
            List<RedumpLocalLibraryDatEntry> ordered = group
                .OrderBy(entry => LooksLikeSerialVersionVariant(entry) ? 1 : 0)
                .ThenByDescending(entry => entry.DatDateUtc.HasValue)
                .ThenByDescending(entry => entry.DatDateUtc)
                .ThenByDescending(entry => entry.LastWriteTimeUtc)
                .ThenByDescending(entry => entry.FileSizeBytes)
                .ThenBy(entry => entry.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            HashSet<string> seenSignatures = new(StringComparer.OrdinalIgnoreCase);
            bool selectedFirst = false;

            foreach (RedumpLocalLibraryDatEntry entry in ordered) {
                string signature = BuildDuplicateSignature(entry);

                if (LooksLikeUnsupportedRedumpArtifact(entry)) {
                    byPath[entry.FilePath] = entry with
                    {
                        IsSelected = false,
                        Status = VariantStatus,
                        Reason = "Non-game Redump artifact kept out of the automatic import."
                    };

                    seenSignatures.Add(signature);
                    continue;
                }

                if (!selectedFirst) {
                    byPath[entry.FilePath] = entry with
                    {
                        IsSelected = true,
                        Status = SelectedStatus,
                        Reason = "Newest DAT/XML candidate for this platform."
                    };

                    seenSignatures.Add(signature);
                    selectedFirst = true;
                    continue;
                }

                if (seenSignatures.Contains(signature)) {
                    byPath[entry.FilePath] = entry with
                    {
                        IsSelected = false,
                        Status = DuplicateStatus,
                        Reason = "Duplicate DAT/XML candidate for this platform."
                    };

                    continue;
                }

                seenSignatures.Add(signature);

                if (LooksLikeSerialVersionVariant(entry)) {
                    byPath[entry.FilePath] = entry with
                    {
                        IsSelected = false,
                        Status = VariantStatus,
                        Reason = "Variant DAT/XML candidate kept out of the automatic selection."
                    };

                    continue;
                }

                byPath[entry.FilePath] = entry with
                {
                    IsSelected = false,
                    Status = OlderStatus,
                    Reason = "Older DAT/XML candidate for this platform."
                };
            }
        }

        return entries
            .Select(entry => byPath[entry.FilePath])
            .OrderBy(entry => entry.PlatformKey, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(entry => entry.IsSelected)
            .ThenBy(entry => entry.Status, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static RedumpLocalLibraryDatEntry CreateReadErrorEntry(
        string filePath,
        Exception ex)
    {
        FileInfo? info = null;

        try {
            info = new FileInfo(filePath);
        }
        catch {
            info = null;
        }

        string fileName = Path.GetFileName(filePath);
        string directoryPath = Path.GetDirectoryName(filePath) ?? string.Empty;
        string extension = Path.GetExtension(filePath);
        string platformKey = NormalizePlatformKey(Path.GetFileNameWithoutExtension(filePath));

        long fileSize = 0;
        DateTime lastWriteTimeUtc = DateTime.MinValue;

        if (info is not null && info.Exists) {
            fileSize = info.Length;
            lastWriteTimeUtc = info.LastWriteTimeUtc;
        }

        return new RedumpLocalLibraryDatEntry(
            filePath,
            fileName,
            directoryPath,
            extension,
            platformKey,
            null,
            null,
            null,
            null,
            null,
            false,
            ReadErrorStatus,
            ex.Message,
            fileSize,
            lastWriteTimeUtc);
    }

    private static string ReadSmallElementText(XmlReader reader)
    {
        using XmlReader subtree = reader.ReadSubtree();
        subtree.Read();
        return subtree.ReadElementContentAsString().Trim();
    }

    private static bool IsGameElement(string localName)
    {
        if (localName.Equals("game", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (localName.Equals("machine", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return false;
    }

    private static bool IsReadError(RedumpLocalLibraryDatEntry entry)
    {
        return IsStatus(entry, ReadErrorStatus);
    }

    private static bool IsStatus(
        RedumpLocalLibraryDatEntry entry,
        string status)
    {
        return entry.Status.Equals(status, StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values) {
            if (!string.IsNullOrWhiteSpace(value)) {
                return value.Trim();
            }
        }

        return "unknown";
    }

    private static string? CleanNullable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return WhitespaceRegex.Replace(value.Trim(), " ");
    }

    private static DateTime? TryParseDatDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        string clean = value.Trim();

        foreach (string format in DateFormats) {
            if (DateTime.TryParseExact(
                clean,
                format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out DateTime parsed)) {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }
        }

        Match match = DateTokenRegex.Match(clean);
        if (!match.Success) {
            return null;
        }

        string date = match.Groups["date"].Value.Replace('_', '-');
        string time = "00:00:00";

        if (match.Groups["time"].Success) {
            time = match.Groups["time"].Value.Replace('-', ':');
        }

        string candidate = $"{date} {time}";

        if (DateTime.TryParseExact(
            candidate,
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out DateTime tokenDate)) {
            return DateTime.SpecifyKind(tokenDate, DateTimeKind.Utc);
        }

        return null;
    }

    private static string NormalizePlatformKey(string value)
    {
        string result = value.Trim();

        int datfileIndex = result.IndexOf(" - Datfile", StringComparison.OrdinalIgnoreCase);
        if (datfileIndex >= 0) {
            result = result.Substring(0, datfileIndex);
        }

        datfileIndex = result.IndexOf(" Datfile", StringComparison.OrdinalIgnoreCase);
        if (datfileIndex >= 0) {
            result = result.Substring(0, datfileIndex);
        }

        result = VariantParenthesisRegex.Replace(result, " ");
        result = result.Replace('_', ' ');
        result = result.Replace('\\', '/');
        result = WhitespaceRegex.Replace(result, " ");
        result = result.Trim();

        if (result.Length == 0) {
            return "UNKNOWN";
        }

        return result.ToUpperInvariant();
    }

    private static string BuildDuplicateSignature(RedumpLocalLibraryDatEntry entry)
    {
        return string.Join(
            "|",
            entry.PlatformKey,
            entry.Name ?? string.Empty,
            entry.Description ?? string.Empty,
            entry.Version ?? string.Empty,
            entry.FileSizeBytes.ToString(CultureInfo.InvariantCulture));
    }

    private static bool LooksLikeUnsupportedRedumpArtifact(RedumpLocalLibraryDatEntry entry)
    {
        string combined = string.Join(
            " ",
            entry.FileName,
            entry.Name ?? string.Empty,
            entry.Description ?? string.Empty);

        if (ContainsOrdinalIgnoreCase(combined, "bios datfile")) {
            return true;
        }

        if (ContainsOrdinalIgnoreCase(combined, "bios images")) {
            return true;
        }

        if (ContainsOrdinalIgnoreCase(combined, " - bios")) {
            return true;
        }

        return false;
    }

    private static bool LooksLikeSerialVersionVariant(RedumpLocalLibraryDatEntry entry)
    {
        string combined = string.Join(
            " ",
            entry.FileName,
            entry.Name ?? string.Empty,
            entry.Description ?? string.Empty);

        bool hasSerial = ContainsOrdinalIgnoreCase(combined, "serial");
        bool hasVersion = ContainsOrdinalIgnoreCase(combined, "version");

        if (hasSerial && hasVersion) {
            return true;
        }

        if (ContainsOrdinalIgnoreCase(combined, "serial,version")) {
            return true;
        }

        return false;
    }

    private static bool ContainsOrdinalIgnoreCase(
        string value,
        string expected)
    {
        return value.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
