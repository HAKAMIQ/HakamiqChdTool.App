using HakamiqChdTool.App.Models.PlayStation;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.Services.PlayStation.PS3ContentIntake;

public sealed class PS3PkgInspector
{
    private const int HeaderProbeBytes = 4096;

    private static readonly byte[] PkgMagic = [0x7F, (byte)'P', (byte)'K', (byte)'G'];

    private static readonly Regex ContentIdRegex = new(
        @"\b[A-Z]{2}\d{4}-[A-Z]{4}\d{5}_\d{2}-[A-Z0-9_]{3,36}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex TitleIdRegex = new(
        @"\b[A-Z]{4}\d{5}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public PS3ContentIntakeResult Analyze(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var warnings = new List<string>();
        if (!File.Exists(path))
        {
            warnings.Add("The selected PKG file was not found.");
            return BuildResult(path, PS3PkgMetadataInvalid(), warnings);
        }

        PS3PkgMetadata metadata = Inspect(path, warnings);
        return BuildResult(path, metadata, warnings);
    }

    public PS3PkgMetadata Inspect(string path, ICollection<string>? warnings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            byte[] header = new byte[Math.Min(HeaderProbeBytes, Math.Max(0, (int)Math.Min(HeaderProbeBytes, stream.Length)))];
            int read = stream.Read(header, 0, header.Length);
            ReadOnlySpan<byte> span = header.AsSpan(0, read);

            if (!HasPkgMagic(span))
            {
                warnings?.Add("The PKG header was not recognized.");
                return PS3PkgMetadataInvalid();
            }

            string searchableText = BytesToSearchableAscii(span);
            string? contentId = TryExtractContentId(searchableText);
            string? titleId = TryExtractTitleId(searchableText, contentId);
            PS3ContentKind contentKind = InferContentKind(titleId, contentId);

            if (string.IsNullOrWhiteSpace(contentId))
            {
                warnings?.Add("The PKG header is valid, but Content ID was not found in the probed header area.");
            }

            return new PS3PkgMetadata(
                IsValidPackage: true,
                ContentId: contentId,
                TitleId: titleId,
                ContentKind: contentKind,
                IsProbablyEncrypted: true);
        }
        catch (Exception ex) when (IsExpectedReadException(ex))
        {
            warnings?.Add("The PKG header could not be read.");
            return PS3PkgMetadataInvalid();
        }
    }

    private static PS3ContentIntakeResult BuildResult(
        string path,
        PS3PkgMetadata metadata,
        IReadOnlyList<string> warnings) => new(
            InputFormat: PS3InputFormat.Pkg,
            ContentKind: metadata.IsValidPackage ? metadata.ContentKind : PS3ContentKind.Unknown,
            SourcePath: path,
            TitleId: metadata.TitleId,
            TitleName: null,
            DiscId: metadata.ContentId,
            HasPs3GameFolder: false,
            HasParamSfo: false,
            HasEbootBin: false,
            HasPs3DiscSfb: false,
            IsProbablyEncrypted: metadata.IsProbablyEncrypted,
            CanConvertToChd: false,
            RecommendedPipeline: metadata.IsValidPackage
                ? "Install into RPCS3-like dev_hdd0/game layout or keep as package"
                : "Keep as package; unsupported or invalid PKG header",
            Warnings: warnings);

    private static bool HasPkgMagic(ReadOnlySpan<byte> header) =>
        header.Length >= PkgMagic.Length
        && header[0] == PkgMagic[0]
        && header[1] == PkgMagic[1]
        && header[2] == PkgMagic[2]
        && header[3] == PkgMagic[3];

    private static string? TryExtractContentId(string text)
    {
        Match match = ContentIdRegex.Match(text);
        return match.Success ? match.Value : null;
    }

    private static string? TryExtractTitleId(string text, string? contentId)
    {
        if (!string.IsNullOrWhiteSpace(contentId))
        {
            Match match = TitleIdRegex.Match(contentId);
            if (match.Success)
            {
                return match.Value;
            }
        }

        Match fallback = TitleIdRegex.Match(text);
        return fallback.Success ? fallback.Value : null;
    }

    private static PS3ContentKind InferContentKind(string? titleId, string? contentId)
    {
        string identity = ((titleId ?? string.Empty) + " " + (contentId ?? string.Empty)).ToUpperInvariant();

        if (ContainsAny(identity, "PATCH", "UPDATE", "UPD"))
        {
            return PS3ContentKind.GameUpdate;
        }

        if (ContainsAny(identity, "DLC", "ADDON", "ADD-ON", "CONTENT"))
        {
            return PS3ContentKind.Dlc;
        }

        if (!string.IsNullOrWhiteSpace(titleId) && titleId.StartsWith("NP", StringComparison.OrdinalIgnoreCase))
        {
            return PS3ContentKind.PsnGame;
        }

        return PS3ContentKind.Unknown;
    }

    private static bool ContainsAny(string source, params string[] values)
    {
        foreach (string value in values)
        {
            if (source.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static PS3PkgMetadata PS3PkgMetadataInvalid() => new(
        IsValidPackage: false,
        ContentId: null,
        TitleId: null,
        ContentKind: PS3ContentKind.Unknown,
        IsProbablyEncrypted: false);

    private static string BytesToSearchableAscii(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder(bytes.Length);

        foreach (byte value in bytes)
        {
            builder.Append(value is >= 32 and <= 126 ? (char)value : ' ');
        }

        return builder.ToString();
    }

    private static bool IsExpectedReadException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException
        or PathTooLongException
        or InvalidDataException;
}
