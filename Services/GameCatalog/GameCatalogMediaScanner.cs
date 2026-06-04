using HakamiqChdTool.App.Models;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.Services.GameCatalog;

public sealed partial class GameCatalogMediaScanner
{
    private const int RegexTimeoutMilliseconds = 250;

    private static readonly string[] LeadingEnglishArticles =
    [
        "The ",
        "A ",
        "An "
    ];

    public GameCatalogEntry Scan(string path, DeepHashAnalysisResult? redumpAnalysis = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Invalid(path ?? string.Empty);
        }

        try
        {
            string fullPath = Path.GetFullPath(path.Trim());
            if (!File.Exists(fullPath))
            {
                return Invalid(fullPath);
            }

            GameCatalogEntryType type = ResolveType(fullPath);
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fullPath);
            string titleWithoutDisc = RemoveDiscToken(fileNameWithoutExtension, out int? discNumber, out int? totalDiscs);
            string title = BuildDisplayTitle(titleWithoutDisc);
            string redumpTitle = redumpAnalysis?.MatchedGameName?.Trim() ?? string.Empty;
            string selectedTitle = string.IsNullOrWhiteSpace(redumpTitle) ? title : redumpTitle;

            PlatformDetectionResult platformDetection = PlatformDetectionService.Detect(fullPath);
            string platform = PlatformDetectionService.IsActionablePlatformName(platformDetection.PlatformName)
                ? platformDetection.PlatformName.Trim()
                : string.Empty;

            string region = NamingCorrectionEngine.TryExtractRegion(fullPath, out string extractedRegion)
                ? extractedRegion
                : string.Empty;

            string serial = string.Empty;
            if (DiscRawSerialProbe.TryProbe(fullPath, out DiscRawSerialProbeResult rawSerial))
            {
                serial = rawSerial.Serial;
                if (string.IsNullOrWhiteSpace(platform) && !string.IsNullOrWhiteSpace(rawSerial.Platform))
                {
                    platform = rawSerial.Platform;
                }

                if (string.IsNullOrWhiteSpace(region) && !string.IsNullOrWhiteSpace(rawSerial.Region))
                {
                    region = rawSerial.Region;
                }
            }

            DeepHashMatch? firstMatch = redumpAnalysis?.Matches.FirstOrDefault();
            DeepHashFileDigest? firstHash = redumpAnalysis?.HashedFiles.FirstOrDefault();
            string crc = firstMatch?.Crc ?? string.Empty;
            string hash = firstMatch?.Sha1 ?? firstHash?.Sha1 ?? string.Empty;

            return new GameCatalogEntry
            {
                Type = type,
                Platform = platform,
                Region = region,
                Path = fullPath,
                Serial = serial,
                Crc = crc,
                Hash = hash,
                Title = selectedTitle,
                SortTitle = BuildSortTitle(selectedTitle),
                RedumpTitle = redumpTitle,
                CollectionTitle = BuildCollectionTitle(selectedTitle),
                DiscNumber = discNumber,
                TotalDiscs = totalDiscs,
                IsDisc = IsDiscType(type),
                IsValid = type != GameCatalogEntryType.Unknown
            };
        }
        catch (Exception ex) when (IsScannerFailure(ex))
        {
            return Invalid(path);
        }
    }

    private static GameCatalogEntry Invalid(string path) => new()
    {
        Path = path,
        IsValid = false
    };

    private static GameCatalogEntryType ResolveType(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.ToLowerInvariant() switch
        {
            ".cue" => GameCatalogEntryType.Cue,
            ".gdi" => GameCatalogEntryType.Gdi,
            ".toc" => GameCatalogEntryType.Toc,
            ".iso" => GameCatalogEntryType.Iso,
            ".chd" => GameCatalogEntryType.Chd,
            ".zip" or ".rar" or ".7z" => GameCatalogEntryType.Archive,
            ".bin" => GameCatalogEntryType.BinTrack,
            _ => GameCatalogEntryType.Unknown
        };
    }

    private static bool IsDiscType(GameCatalogEntryType type) =>
        type is GameCatalogEntryType.Cue
            or GameCatalogEntryType.Gdi
            or GameCatalogEntryType.Toc
            or GameCatalogEntryType.Iso
            or GameCatalogEntryType.Chd
            or GameCatalogEntryType.BinTrack;

    private static string RemoveDiscToken(string value, out int? discNumber, out int? totalDiscs)
    {
        discNumber = null;
        totalDiscs = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        Match match = DiscTokenRegex().Match(value);
        if (match.Success && int.TryParse(match.Groups["disc"].Value, out int parsedDisc) && parsedDisc > 0)
        {
            discNumber = parsedDisc;
            if (match.Groups["total"].Success
                && int.TryParse(match.Groups["total"].Value, out int parsedTotal)
                && parsedTotal >= parsedDisc)
            {
                totalDiscs = parsedTotal;
            }

            return DiscTokenRegex().Replace(value, " ");
        }

        return value;
    }

    private static string BuildCollectionTitle(string title)
    {
        string withoutDisc = RemoveDiscToken(title, out _, out _);
        return BuildDisplayTitle(withoutDisc);
    }

    private static string BuildDisplayTitle(string value)
    {
        string collapsed = SeparatorRegex().Replace(value ?? string.Empty, " ").Trim();
        return string.IsNullOrWhiteSpace(collapsed) ? string.Empty : collapsed;
    }

    private static string BuildSortTitle(string title)
    {
        string value = BuildDisplayTitle(title);
        foreach (string article in LeadingEnglishArticles)
        {
            if (value.StartsWith(article, StringComparison.OrdinalIgnoreCase))
            {
                value = value[article.Length..].Trim();
                break;
            }
        }

        return SeparatorRegex().Replace(value, " ").Trim().ToUpperInvariant();
    }

    private static bool IsScannerFailure(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or InvalidOperationException
            or RegexMatchTimeoutException
            or System.Security.SecurityException;
    }

    [GeneratedRegex(
        @"(?:^|[\s._\-\(\[])(?:disc|disk|cd)[\s._\-]*(?<disc>\d{1,2})(?:\s*of\s*(?<total>\d{1,2}))?(?:[\s._\-\)\]]|$)",
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant,
        RegexTimeoutMilliseconds)]
    private static partial Regex DiscTokenRegex();

    [GeneratedRegex(
        @"[\s._\-]+",
        RegexOptions.CultureInvariant,
        RegexTimeoutMilliseconds)]
    private static partial Regex SeparatorRegex();
}