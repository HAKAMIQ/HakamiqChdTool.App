using DiscUtils.Iso9660;
using System.IO;
using System.Text;

namespace HakamiqChdTool.App.Services;

internal sealed class DiscMetadataProbeResult
{
    public static readonly DiscMetadataProbeResult Empty = new(string.Empty, string.Empty, string.Empty, 0, string.Empty);

    public DiscMetadataProbeResult(string platformName, string region, string titleId, int confidenceScore, string reason)
    {
        PlatformName = platformName ?? string.Empty;
        Region = region ?? string.Empty;
        TitleId = titleId ?? string.Empty;
        ConfidenceScore = Math.Clamp(confidenceScore, 0, 100);
        Reason = reason ?? string.Empty;
    }

    public string PlatformName { get; }
    public string Region { get; }
    public string TitleId { get; }
    public int ConfidenceScore { get; }
    public string Reason { get; }
}

internal static class DiscMetadataProbe
{
    private const int MaxTextProbeBytes = 256 * 1024;

    private const string InsufficientMetadataReasonKey = "LocDiscProbe_InsufficientMetadata";
    private const string CueDescriptorSerialReasonKey = "LocDiscProbe_CueDescriptorSerial";
    private const string CueTrackNameSerialReasonKey = "LocDiscProbe_CueTrackNameSerial";
    private const string GdiDescriptorSerialReasonKey = "LocDiscProbe_GdiDescriptorSerial";
    private const string PspSfoSerialReasonKey = "LocDiscProbe_PspSfoSerial";
    private const string PspUmdDataSerialReasonKey = "LocDiscProbe_PspUmdDataSerial";
    private const string SystemCnfSerialReasonKey = "LocDiscProbe_SystemCnfSerial";
    private const string PspStructureReasonKey = "LocDiscProbe_PspStructure";
    private const string NintendoGameIdReasonKey = "LocDiscProbe_NintendoGameId";

    public static bool TryResolveRegion(string? path, out string region)
    {
        region = string.Empty;

        if (!TryProbe(path, out DiscMetadataProbeResult result))
        {
            return false;
        }

        if (result.ConfidenceScore < 70 || string.IsNullOrWhiteSpace(result.Region))
        {
            return false;
        }

        region = result.Region;
        return true;
    }

    public static bool TryDetectPlatform(string? path, out PlatformDetectionResult detection)
    {
        detection = PlatformDetectionResult.Create(string.Empty, string.Empty, 10, InsufficientMetadataReasonKey);

        if (!TryProbe(path, out DiscMetadataProbeResult result))
        {
            return false;
        }

        if (result.ConfidenceScore < 75 || string.IsNullOrWhiteSpace(result.PlatformName))
        {
            return false;
        }

        detection = PlatformDetectionResult.Create(result.PlatformName, result.TitleId, result.ConfidenceScore, result.Reason);
        return true;
    }

    public static bool TryProbe(string? path, out DiscMetadataProbeResult result)
    {
        result = DiscMetadataProbeResult.Empty;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".iso" or ".img" or ".raw" or ".bin" => TryProbeIsoLike(path, out result),
            ".cue" => TryProbeCue(path, out result),
            ".gdi" => TryProbeTextDescriptor(path, "SEGA Dreamcast", GdiDescriptorSerialReasonKey, out result),
            _ => false
        };
    }

    private static bool TryProbeCue(string cuePath, out DiscMetadataProbeResult result)
    {
        result = DiscMetadataProbeResult.Empty;

        try
        {
            string text = ReadSmallTextFile(cuePath);
            if (TryBuildFromSerialText(text, "Sony PlayStation", CueDescriptorSerialReasonKey, 82, out result))
            {
                return true;
            }

            foreach (string line in EnumerateLines(text))
            {
                if (!TryExtractCueFileName(line, out string referenced))
                {
                    continue;
                }

                if (TryBuildFromSerialText(referenced, "Sony PlayStation", CueTrackNameSerialReasonKey, 78, out result))
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (IsExpectedReadException(ex))
        {
            return false;
        }

        return false;
    }

    private static bool TryProbeTextDescriptor(string path, string fallbackPlatform, string reasonKey, out DiscMetadataProbeResult result)
    {
        result = DiscMetadataProbeResult.Empty;

        try
        {
            string text = ReadSmallTextFile(path);
            return TryBuildFromSerialText(text, fallbackPlatform, reasonKey, 76, out result);
        }
        catch (Exception ex) when (IsExpectedReadException(ex))
        {
            return false;
        }
    }

    private static bool TryProbeIsoLike(string path, out DiscMetadataProbeResult result)
    {
        result = DiscMetadataProbeResult.Empty;

        try
        {
            using (FileStream raw = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (TryProbeNintendoGameId(raw, out result))
                {
                    return true;
                }
            }

            if (!LooksLikeIso9660Volume(path))
            {
                return false;
            }

            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var cd = new CDReader(stream, true);

            if (TryReadIsoFileText(cd, @"\PSP_GAME\PARAM.SFO", out string pspSfo)
                && TryBuildFromSerialText(pspSfo, "PlayStation Portable", PspSfoSerialReasonKey, 96, out result))
            {
                return true;
            }

            if (TryReadIsoFileText(cd, @"\UMD_DATA.BIN", out string umdData)
                && TryBuildFromSerialText(umdData, "PlayStation Portable", PspUmdDataSerialReasonKey, 94, out result))
            {
                return true;
            }

            if (TryReadIsoFileText(cd, @"\SYSTEM.CNF", out string systemCnf))
            {
                string platform = systemCnf.IndexOf("BOOT2", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "PlayStation 2"
                    : "PlayStation 1";

                if (TryBuildFromSerialText(systemCnf, platform, SystemCnfSerialReasonKey, 96, out result))
                {
                    return true;
                }
            }

            if (IsoFileExists(cd, @"\PSP_GAME\PARAM.SFO") || IsoFileExists(cd, @"\UMD_DATA.BIN"))
            {
                result = new DiscMetadataProbeResult("PlayStation Portable", string.Empty, string.Empty, 88, PspStructureReasonKey);
                return true;
            }
        }
        catch (Exception ex) when (IsExpectedReadException(ex) || IsDiscUtilsReadException(ex))
        {
            return false;
        }

        return false;
    }

    private static bool TryProbeNintendoGameId(FileStream stream, out DiscMetadataProbeResult result)
    {
        result = DiscMetadataProbeResult.Empty;

        if (stream.Length < 6)
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[6];
        stream.Position = 0;
        int read = stream.Read(buffer);
        if (read < 6)
        {
            return false;
        }

        string gameId = Encoding.ASCII.GetString(buffer);
        if (!LooksLikeNintendoGameId(gameId))
        {
            return false;
        }

        char regionCode = char.ToUpperInvariant(gameId[3]);
        if (!TryMapNintendoRegion(regionCode, out string region))
        {
            return false;
        }

        string platform = stream.Length >= 2_500_000_000L ? "Nintendo Wii" : "Nintendo GameCube";
        result = new DiscMetadataProbeResult(platform, region, gameId, 90, NintendoGameIdReasonKey);
        return true;
    }

    private static bool LooksLikeNintendoGameId(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId) || gameId.Length < 4)
        {
            return false;
        }

        for (int i = 0; i < 4; i++)
        {
            if (!char.IsLetterOrDigit(gameId[i]))
            {
                return false;
            }
        }

        return TryMapNintendoRegion(char.ToUpperInvariant(gameId[3]), out _);
    }

    private static bool TryMapNintendoRegion(char code, out string region)
    {
        region = code switch
        {
            'E' or 'N' => "USA",
            'J' => "Japan",
            'P' or 'D' or 'F' or 'I' or 'S' or 'H' or 'Y' or 'Z' => "Europe",
            'K' or 'Q' => "Korea",
            'C' => "China",
            'A' or 'U' => "Australia",
            _ => string.Empty,
        };

        return !string.IsNullOrWhiteSpace(region);
    }

    private static bool TryBuildFromSerialText(string? text, string fallbackPlatform, string reasonKey, int confidence, out DiscMetadataProbeResult result)
    {
        result = DiscMetadataProbeResult.Empty;

        if (!DiscSerialCatalog.TryExtract(
            text,
            DiscSerialScanProfile.Metadata,
            fallbackPlatform,
            includeOptionalTail: true,
            serialSeparator: string.Empty,
            out DiscSerialCatalogResult serial))
        {
            return false;
        }

        result = new DiscMetadataProbeResult(
            serial.PlatformName,
            serial.Region,
            serial.Serial,
            confidence,
            reasonKey);
        return true;
    }

    private static bool LooksLikeIso9660Volume(string isoPath)
    {
        try
        {
            using FileStream stream = File.Open(isoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return HasIso9660DescriptorAt(stream, 0x8001)
                || HasIso9660DescriptorAt(stream, 0x8801)
                || HasIso9660DescriptorAt(stream, 0x9001);
        }
        catch (Exception ex) when (IsExpectedReadException(ex))
        {
            return false;
        }
    }

    private static bool HasIso9660DescriptorAt(FileStream stream, long offset)
    {
        if (stream.Length < offset + 5)
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[5];
        stream.Position = offset;
        int read = stream.Read(buffer);
        return read == 5
            && buffer[0] == (byte)'C'
            && buffer[1] == (byte)'D'
            && buffer[2] == (byte)'0'
            && buffer[3] == (byte)'0'
            && buffer[4] == (byte)'1';
    }

    private static bool TryReadIsoFileText(CDReader cd, string path, out string text)
    {
        text = string.Empty;

        try
        {
            string normalized = NormalizeIsoPath(path);
            if (!cd.FileExists(normalized))
            {
                return false;
            }

            using Stream stream = cd.OpenFile(normalized, FileMode.Open);
            int length = (int)Math.Min(MaxTextProbeBytes, Math.Max(0, stream.Length));
            byte[] buffer = new byte[length];
            int read = stream.Read(buffer, 0, buffer.Length);
            text = DecodeAsciiLoose(buffer.AsSpan(0, read));
            return text.Length > 0;
        }
        catch (Exception ex) when (IsExpectedReadException(ex) || IsDiscUtilsReadException(ex))
        {
            text = string.Empty;
            return false;
        }
    }

    private static bool IsoFileExists(CDReader cd, string path)
    {
        try
        {
            return cd.FileExists(NormalizeIsoPath(path));
        }
        catch (Exception ex) when (IsExpectedReadException(ex) || IsDiscUtilsReadException(ex))
        {
            return false;
        }
    }

    private static string NormalizeIsoPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "\\";
        }

        path = path.Replace("/", "\\");
        if (!path.StartsWith('\\'))
        {
            path = "\\" + path;
        }

        return path;
    }

    private static string ReadSmallTextFile(string path)
    {
        using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        int length = (int)Math.Min(MaxTextProbeBytes, Math.Max(0, stream.Length));
        byte[] buffer = new byte[length];
        int read = stream.Read(buffer, 0, buffer.Length);
        return DecodeAsciiLoose(buffer.AsSpan(0, read));
    }

    private static IEnumerable<string> EnumerateLines(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static bool TryExtractCueFileName(string rawLine, out string fileName)
    {
        fileName = string.Empty;

        ReadOnlySpan<char> span = rawLine.AsSpan().TrimStart();
        if (span.Length < 4 || !span[..4].Equals("FILE".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (span.Length > 4 && !char.IsWhiteSpace(span[4]))
        {
            return false;
        }

        span = span[4..].TrimStart();
        if (span.Length == 0)
        {
            return false;
        }

        if (span[0] == '"')
        {
            int closingQuote = span[1..].IndexOf('"');
            if (closingQuote < 0)
            {
                return false;
            }

            fileName = span.Slice(1, closingQuote).ToString().Trim();
            return fileName.Length > 0;
        }

        int separator = IndexOfWhiteSpace(span);
        fileName = separator > 0 ? span[..separator].ToString().Trim() : span.ToString().Trim();
        return fileName.Length > 0;
    }

    private static string DecodeAsciiLoose(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder(bytes.Length);

        foreach (byte value in bytes)
        {
            builder.Append(value >= 32 && value <= 126 ? (char)value : ' ');
        }

        return builder.ToString();
    }

    private static int IndexOfWhiteSpace(ReadOnlySpan<char> span)
    {
        for (int i = 0; i < span.Length; i++)
        {
            if (char.IsWhiteSpace(span[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsExpectedReadException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException
        or InvalidDataException;

    private static bool IsDiscUtilsReadException(Exception ex) =>
        ex.GetType().FullName?.Contains("DiscUtils", StringComparison.OrdinalIgnoreCase) == true
        || ex.GetType().Name.Contains("FileSystem", StringComparison.OrdinalIgnoreCase);
}