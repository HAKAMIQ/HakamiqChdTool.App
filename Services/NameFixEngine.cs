using Serilog;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.Services;

public sealed record ManualRedumpRenameResult(
    bool Success,
    string NewPath,
    string ErrorMessageKey);

public static class NamingCorrectionEngine
{
    private static readonly ILogger Logger = global::Serilog.Log.ForContext(typeof(NamingCorrectionEngine));

    private static readonly Regex RegionRegex = new(
        @"\b(USA|US|UNITED\s+STATES|EUROPE|EUR|EU|JAPAN|JPN|JAP|PAL|WORLD|REGION\s*[-_ ]?\s*FREE|ASIA|KOREA|KOR|CHINA|CHN|AUSTRALIA|AUS|BRAZIL|BRA|CANADA|CAN|LATIN\s+AMERICA|LATAM|MEXICO|MEX|MIDDLE\s+EAST|UAE|KSA|MDE|RUSSIA|RUS)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex YearRegex = new(
        @"\b(19|20)\d{2}\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private const string AdvisoryOnlyMessageKey = "LocNaming_RedumpRenameAdvisoryOnly";
    private const string OriginalPathMissingMessageKey = "LocNaming_OriginalPathMissing";
    private const string OriginalFileNotFoundMessageKey = "LocNaming_OriginalFileNotFound";
    private const string SuggestedNameMissingMessageKey = "LocNaming_SuggestedNameMissing";
    private const string OriginalDirectoryMissingMessageKey = "LocNaming_OriginalDirectoryMissing";
    private const string SuggestedNameInvalidMessageKey = "LocNaming_SuggestedNameInvalid";
    private const string SuggestedNameUnsafeMessageKey = "LocNaming_SuggestedNameUnsafe";
    private const string TargetFileExistsMessageKey = "LocNaming_TargetFileExists";
    private const string RenameFailedMessageKey = "LocNaming_RenameFailed";

    public static bool TryExtractRegion(string? filePathOrName, out string region)
    {
        region = string.Empty;
        if (string.IsNullOrWhiteSpace(filePathOrName))
        {
            return false;
        }

        string name = Path.GetFileName(filePathOrName);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = filePathOrName;
        }

        string extension = Path.GetExtension(name);
        if (IsKnownMediaFileExtension(extension))
        {
            name = Path.GetFileNameWithoutExtension(name);
        }

        IReadOnlyList<string> regions = ResolveCanonicalRegionsForPath(name);
        if (regions.Count == 0)
        {
            return false;
        }

        region = BuildRegionFolderName(regions);
        return !string.IsNullOrWhiteSpace(region);
    }

    private static string BuildRegionFolderName(IReadOnlyList<string> regions)
    {
        if (regions.Count == 0)
        {
            return string.Empty;
        }

        if (regions.Contains("World", StringComparer.OrdinalIgnoreCase))
        {
            return "World";
        }

        bool hasUsa = regions.Contains("USA", StringComparer.OrdinalIgnoreCase);
        bool hasEurope = regions.Contains("Europe", StringComparer.OrdinalIgnoreCase);
        bool hasJapan = regions.Contains("Japan", StringComparer.OrdinalIgnoreCase);

        if (hasUsa && hasEurope && hasJapan)
        {
            return "World";
        }

        string[] priority =
        [
            "Japan",
            "USA",
            "Europe",
            "Asia",
            "Korea",
            "China",
            "Australia",
            "Canada",
            "Brazil",
            "Latin America",
            "Middle East",
            "Russia",
            "Mexico"
        ];

        var ordered = new List<string>();
        foreach (string item in priority)
        {
            if (regions.Contains(item, StringComparer.OrdinalIgnoreCase))
            {
                ordered.Add(item);
            }
        }

        foreach (string item in regions)
        {
            if (!ordered.Contains(item, StringComparer.OrdinalIgnoreCase))
            {
                ordered.Add(item);
            }
        }

        return ordered.Count == 1
            ? ordered[0]
            : string.Join("-", ordered);
    }

    private static bool IsKnownMediaFileExtension(string? extension) =>
        extension?.ToLowerInvariant() is ".chd"
            or ".cue"
            or ".bin"
            or ".iso"
            or ".gdi"
            or ".toc"
            ;

    private static IReadOnlyList<string> ResolveCanonicalRegionsForPath(string filePathOrName)
    {
        var regions = new List<string>();
        if (string.IsNullOrWhiteSpace(filePathOrName))
        {
            return regions;
        }

        string name = Path.GetFileName(filePathOrName);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = filePathOrName;
        }

        string extension = Path.GetExtension(name);
        if (IsKnownMediaFileExtension(extension))
        {
            name = Path.GetFileNameWithoutExtension(name);
        }

        if (Regex.IsMatch(name, @"[\(\[\{]\s*U\s*[\)\]\}]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            AddRegion(regions, "USA");
        }

        if (Regex.IsMatch(name, @"[\(\[\{]\s*E\s*[\)\]\}]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            AddRegion(regions, "Europe");
        }

        if (Regex.IsMatch(name, @"[\(\[\{]\s*J\s*[\)\]\}]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            AddRegion(regions, "Japan");
        }

        if (Regex.IsMatch(
                name,
                @"(?:[\(\[\{]\s*NTSC\s*[\)\]\}]\s*[\(\[\{]\s*(?:U|US|USA)\s*[\)\]\}]|[\(\[\{]\s*NTSC\s*[-_ ]?\s*(?:U|US|USA)\s*[\)\]\}]|\bNTSC\s*[-_ ]\s*(?:U|US|USA)\b)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            AddRegion(regions, "USA");
        }

        if (Regex.IsMatch(
                name,
                @"(?:[\(\[\{]\s*NTSC\s*[\)\]\}]\s*[\(\[\{]\s*J\s*[\)\]\}]|[\(\[\{]\s*NTSC\s*[-_ ]?\s*J\s*[\)\]\}]|\bNTSC\s*[-_ ]\s*J\b)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            AddRegion(regions, "Japan");
        }

        string normalized = NormalizeRegionCandidate(name);

        if (ContainsRegionPhrase(normalized, "UNITED_STATES"))
        {
            AddRegion(regions, "USA");
        }

        if (ContainsRegionPhrase(normalized, "LATIN_AMERICA"))
        {
            AddRegion(regions, "Latin America");
        }

        if (ContainsRegionPhrase(normalized, "MIDDLE_EAST"))
        {
            AddRegion(regions, "Middle East");
        }

        if (ContainsRegionPhrase(normalized, "REGION_FREE"))
        {
            AddRegion(regions, "World");
        }

        string[] tokens = normalized.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i];

            if (string.Equals(token, "NTSC", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Length)
            {
                string next = tokens[i + 1];
                if (string.Equals(next, "U", StringComparison.OrdinalIgnoreCase))
                {
                    AddRegion(regions, "USA");
                    i++;
                    continue;
                }

                if (string.Equals(next, "J", StringComparison.OrdinalIgnoreCase))
                {
                    AddRegion(regions, "Japan");
                    i++;
                    continue;
                }
            }

            string canonical = CanonicalizeRegionForPath(token);
            if (!string.IsNullOrWhiteSpace(canonical))
            {
                AddRegion(regions, canonical);
            }
        }

        return regions;
    }

    private static void AddRegion(List<string> regions, string region)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            return;
        }

        if (!regions.Contains(region, StringComparer.OrdinalIgnoreCase))
        {
            regions.Add(region);
        }
    }

    private static bool ContainsRegionPhrase(string normalized, string phrase)
    {
        if (string.IsNullOrWhiteSpace(normalized) || string.IsNullOrWhiteSpace(phrase))
        {
            return false;
        }

        return string.Equals(normalized, phrase, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(phrase + "_", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("_" + phrase, StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("_" + phrase + "_", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRegionCandidate(string value)
    {
        string normalized = Regex.Replace(value, @"[\.\-\s,;\(\)\[\]\{\}\+]+", "_");
        normalized = Regex.Replace(normalized, @"_+", "_").Trim('_');
        return normalized.ToUpperInvariant();
    }

    private static string CanonicalizeRegionForPath(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return string.Empty;
        }

        string upper = rawToken.Trim().Trim('_', '.', '-', ',', ';').ToUpperInvariant();
        return upper switch
        {
            "USA" or "US" => "USA",
            "EUROPE" or "EUR" or "EU" or "PAL" => "Europe",
            "JAPAN" or "JPN" or "JAP" => "Japan",
            "WORLD" or "RF" => "World",
            "ASIA" => "Asia",
            "KOREA" or "KOR" => "Korea",
            "CHINA" or "CHN" => "China",
            "AUSTRALIA" or "AUS" => "Australia",
            "BRAZIL" or "BRA" => "Brazil",
            "CANADA" or "CAN" => "Canada",
            "LATAM" => "Latin America",
            "MEXICO" or "MEX" => "Mexico",
            "UAE" or "KSA" or "MDE" => "Middle East",
            "RUSSIA" or "RUS" => "Russia",
            _ => string.Empty
        };
    }

    public static (bool IsCompliant, string SuggestedName) Analyze(string filePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(filePath);
        if (ArchiveNamingRuleValidator.IsValid(fileName))
        {
            return (true, fileName);
        }

        string normalized = Regex.Replace(fileName, @"[\.\-\s]+", "_");
        normalized = Regex.Replace(normalized, @"_+", "_").Trim('_');

        string region = RegionRegex.Match(normalized).Value.ToUpperInvariant();
        if (region == "JAP")
        {
            region = "JPN";
        }

        string year = YearRegex.Match(normalized).Value;

        string game = normalized;
        if (!string.IsNullOrWhiteSpace(region))
        {
            game = Regex.Replace(game, $@"\b{Regex.Escape(region)}\b", "", RegexOptions.IgnoreCase).Trim('_');
        }

        if (!string.IsNullOrWhiteSpace(year))
        {
            game = Regex.Replace(game, $@"\b{Regex.Escape(year)}\b", "", RegexOptions.IgnoreCase).Trim('_');
        }

        game = string.IsNullOrWhiteSpace(game)
            ? "Game"
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(game.Replace("_", " ")).Replace(" ", "");

        region = string.IsNullOrWhiteSpace(region) ? "USA" : region;
        year = string.IsNullOrWhiteSpace(year) ? DateTime.Now.Year.ToString(CultureInfo.InvariantCulture) : year;

        return (false, $"{game}_{region}_{year}");
    }

    public static bool TryApplyRename(string originalPath, string suggestedName, out string newPath, out string error)
    {
        newPath = originalPath;
        error = AdvisoryOnlyMessageKey;
        return false;
    }

    public static bool TryApplyRedumpSuggestedRename(
        string originalPath,
        string suggestedFileName,
        out string newPath,
        out string error)
    {
        ManualRedumpRenameResult result = TryApplyManualRedumpSuggestedRenameAsync(
                originalPath,
                suggestedFileName,
                CancellationToken.None)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

        newPath = result.NewPath;
        error = result.ErrorMessageKey;
        return result.Success;
    }

    public static async Task<ManualRedumpRenameResult> TryApplyManualRedumpSuggestedRenameAsync(
        string originalPath,
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(originalPath))
        {
            return Failed(originalPath, OriginalPathMissingMessageKey);
        }

        string fullOriginalPath;
        try
        {
            fullOriginalPath = Path.GetFullPath(originalPath);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            Logger.Debug(ex, "Redump manual rename rejected an invalid original path. Path={Path}", originalPath);
            return Failed(originalPath, OriginalPathMissingMessageKey);
        }

        if (!File.Exists(fullOriginalPath))
        {
            return Failed(fullOriginalPath, OriginalFileNotFoundMessageKey);
        }

        if (string.IsNullOrWhiteSpace(suggestedFileName))
        {
            return Failed(fullOriginalPath, SuggestedNameMissingMessageKey);
        }

        string directory = Path.GetDirectoryName(fullOriginalPath) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return Failed(fullOriginalPath, OriginalDirectoryMissingMessageKey);
        }

        string sourceExtension = Path.GetExtension(fullOriginalPath);
        string safeName = SanitizeSuggestedFileName(suggestedFileName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            return Failed(fullOriginalPath, SuggestedNameInvalidMessageKey);
        }

        if (!IsKnownMediaFileExtension(Path.GetExtension(safeName)) && IsKnownMediaFileExtension(sourceExtension))
        {
            safeName += sourceExtension;
        }

        string targetPath;
        try
        {
            targetPath = Path.GetFullPath(Path.Combine(directory, safeName));
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            Logger.Debug(ex, "Redump manual rename rejected an invalid target path. SuggestedName={SuggestedName}", suggestedFileName);
            return Failed(fullOriginalPath, SuggestedNameInvalidMessageKey);
        }

        if (!IsUnderDirectory(directory, targetPath))
        {
            return Failed(fullOriginalPath, SuggestedNameUnsafeMessageKey);
        }

        if (string.Equals(fullOriginalPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return new ManualRedumpRenameResult(true, targetPath, string.Empty);
        }

        await using IAsyncDisposable sourceLease = await FilePathExclusiveGate
            .AcquireAsync(fullOriginalPath, cancellationToken)
            .ConfigureAwait(false);

        await using IAsyncDisposable targetLease = await FilePathExclusiveGate
            .AcquireAsync(targetPath, cancellationToken)
            .ConfigureAwait(false);

        if (!File.Exists(fullOriginalPath))
        {
            return Failed(fullOriginalPath, OriginalFileNotFoundMessageKey);
        }

        if (File.Exists(targetPath))
        {
            return Failed(fullOriginalPath, TargetFileExistsMessageKey);
        }

        try
        {
            File.Move(fullOriginalPath, targetPath);
            return new ManualRedumpRenameResult(true, targetPath, string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Logger.Warning(ex, "Redump manual rename failed. Source={Source}, Target={Target}", fullOriginalPath, targetPath);
            return Failed(fullOriginalPath, RenameFailedMessageKey);
        }
    }

    private static ManualRedumpRenameResult Failed(string originalPath, string messageKey) =>
        new(false, string.IsNullOrWhiteSpace(originalPath) ? string.Empty : originalPath, messageKey);

    private static string SanitizeSuggestedFileName(string value)
    {
        string fileNameOnly = Path.GetFileName(value.Trim());
        if (string.IsNullOrWhiteSpace(fileNameOnly))
        {
            return string.Empty;
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        var chars = fileNameOnly
            .Select(character => invalid.Contains(character) ? ' ' : character)
            .ToArray();

        string collapsed = Regex.Replace(new string(chars), @"\s+", " ").Trim();
        return collapsed.TrimEnd('.', ' ');
    }

    private static bool IsUnderDirectory(string baseDirectory, string candidate)
    {
        string root = Path.GetFullPath(baseDirectory);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        string path = Path.GetFullPath(candidate);
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException;
}
