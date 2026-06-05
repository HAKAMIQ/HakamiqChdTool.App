using DiscUtils;
using DiscUtils.Iso9660;
using DiscUtils.Udf;
using HakamiqChdTool.App.Models.PlayStation;
using System.Collections.Generic;
using System.IO;

namespace HakamiqChdTool.App.Services.PlayStation.PS3ContentIntake;

public sealed class PS3IsoLayoutReader
{
    private const int MaxMetadataBytes = 1024 * 1024;

    private readonly PS3ParamSfoReader _paramSfoReader;
    private readonly PS3DiscSfbReader _discSfbReader;

    public PS3IsoLayoutReader()
        : this(new PS3ParamSfoReader(), new PS3DiscSfbReader())
    {
    }

    public PS3IsoLayoutReader(PS3ParamSfoReader paramSfoReader, PS3DiscSfbReader discSfbReader)
    {
        ArgumentNullException.ThrowIfNull(paramSfoReader);
        ArgumentNullException.ThrowIfNull(discSfbReader);

        _paramSfoReader = paramSfoReader;
        _discSfbReader = discSfbReader;
    }

    public PS3ContentIntakeResult Analyze(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var warnings = new List<string>();
        if (!File.Exists(path))
        {
            warnings.Add("The selected ISO file was not found.");
            return BuildUnreadable(path, isProbablyEncrypted: false, warnings);
        }

        bool hasDkey = HasAdjacentDkey(path);

        if (TryAnalyzeIso9660(path, out PS3ContentIntakeResult? iso9660Result)
            && iso9660Result is not null)
        {
            return iso9660Result;
        }

        if (TryAnalyzeUdf(path, out PS3ContentIntakeResult? udfResult)
            && udfResult is not null)
        {
            return udfResult;
        }

        warnings.Add(hasDkey
            ? "The ISO file system could not be read. A matching .dkey file exists, but decryption is outside this intake step."
            : "The ISO file system could not be read. The image may be encrypted or incomplete.");

        return BuildUnreadable(path, isProbablyEncrypted: true, warnings);
    }

    private bool TryAnalyzeIso9660(string path, out PS3ContentIntakeResult? result)
    {
        result = null;

        if (!LooksLikeIso9660Volume(path))
        {
            return false;
        }

        try
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var cd = new CDReader(stream, true);
            result = AnalyzeFileSystem(path, cd);
            return true;
        }
        catch (Exception ex) when (IsExpectedReadException(ex) || IsDiscUtilsReadException(ex))
        {
            result = null;
            return false;
        }
    }

    private bool TryAnalyzeUdf(string path, out PS3ContentIntakeResult? result)
    {
        result = null;

        if (!LooksLikeUdfVolume(path))
        {
            return false;
        }

        try
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var udf = new UdfReader(stream);
            result = AnalyzeFileSystem(path, udf);
            return true;
        }
        catch (Exception ex) when (IsExpectedReadException(ex) || IsDiscUtilsReadException(ex))
        {
            result = null;
            return false;
        }
    }

    private PS3ContentIntakeResult AnalyzeFileSystem(string path, DiscFileSystem fileSystem)
    {
        var warnings = new List<string>();

        bool hasParam = TryFileExists(fileSystem, @"\PS3_GAME\PARAM.SFO");
        bool hasEboot = TryFileExists(fileSystem, @"\PS3_GAME\USRDIR\EBOOT.BIN");
        bool hasDiscSfb = TryFileExists(fileSystem, @"\PS3_DISC.SFB");
        bool hasPs3Game = TryDirectoryExists(fileSystem, @"\PS3_GAME") || hasParam || hasEboot;

        if (!hasPs3Game)
        {
            warnings.Add("The ISO does not contain a PS3_GAME directory.");
        }

        if (hasPs3Game && !hasParam)
        {
            warnings.Add("PARAM.SFO was not found inside the ISO.");
        }

        if (hasPs3Game && !hasEboot)
        {
            warnings.Add("EBOOT.BIN was not found inside the ISO.");
        }

        if (hasPs3Game && !hasDiscSfb)
        {
            warnings.Add("PS3_DISC.SFB was not found in the ISO.");
        }

        PS3ContentIdentity identity = PS3ContentIdentity.Empty;
        if (hasParam
            && TryOpenFile(fileSystem, @"\PS3_GAME\PARAM.SFO", out Stream? paramStream)
            && paramStream is not null)
        {
            using (paramStream)
            {
                identity = _paramSfoReader.Read(paramStream);
            }
        }

        string? discId = null;
        if (hasDiscSfb
            && TryOpenFile(fileSystem, @"\PS3_DISC.SFB", out Stream? discStream)
            && discStream is not null)
        {
            using (discStream)
            {
                discId = _discSfbReader.ReadDiscId(discStream);
            }
        }

        string? titleId = FirstNonEmpty(identity.TitleId, discId);
        bool canConvert = hasPs3Game && hasParam && hasEboot;
        PS3ContentKind contentKind = hasDiscSfb
            ? PS3ContentKind.DiscGame
            : InferContentKind(identity.Category);

        return new PS3ContentIntakeResult(
            InputFormat: PS3InputFormat.Iso,
            ContentKind: contentKind,
            SourcePath: path,
            TitleId: titleId,
            TitleName: identity.TitleName,
            DiscId: discId,
            HasPs3GameFolder: hasPs3Game,
            HasParamSfo: hasParam,
            HasEbootBin: hasEboot,
            HasPs3DiscSfb: hasDiscSfb,
            IsProbablyEncrypted: false,
            CanConvertToChd: canConvert,
            RecommendedPipeline: canConvert
                ? "ISO -> chdman createdvd -> CHD"
                : "Unsupported or incomplete PS3 ISO",
            Warnings: warnings);
    }

    private static PS3ContentIntakeResult BuildUnreadable(
        string path,
        bool isProbablyEncrypted,
        IReadOnlyList<string> warnings) => new(
            InputFormat: PS3InputFormat.Iso,
            ContentKind: PS3ContentKind.Unknown,
            SourcePath: path,
            TitleId: null,
            TitleName: null,
            DiscId: null,
            HasPs3GameFolder: false,
            HasParamSfo: false,
            HasEbootBin: false,
            HasPs3DiscSfb: false,
            IsProbablyEncrypted: isProbablyEncrypted,
            CanConvertToChd: false,
            RecommendedPipeline: isProbablyEncrypted
                ? "Encrypted or unreadable ISO; user-owned keys are required before conversion"
                : "Unsupported or incomplete PS3 ISO",
            Warnings: warnings);

    private static bool TryOpenFile(DiscFileSystem fileSystem, string path, out Stream? stream)
    {
        stream = null;

        foreach (string candidate in EnumerateIsoPathCandidates(path))
        {
            try
            {
                if (!fileSystem.FileExists(candidate))
                {
                    continue;
                }

                stream = fileSystem.OpenFile(candidate, FileMode.Open);
                if (stream.CanSeek && stream.Length > MaxMetadataBytes)
                {
                    stream.Dispose();
                    stream = null;
                    return false;
                }

                return true;
            }
            catch (Exception ex) when (IsExpectedReadException(ex) || IsDiscUtilsReadException(ex))
            {
                stream?.Dispose();
                stream = null;
            }
        }

        return false;
    }

    private static bool TryFileExists(DiscFileSystem fileSystem, string path)
    {
        foreach (string candidate in EnumerateIsoPathCandidates(path))
        {
            try
            {
                if (fileSystem.FileExists(candidate))
                {
                    return true;
                }
            }
            catch (Exception ex) when (IsExpectedReadException(ex) || IsDiscUtilsReadException(ex))
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryDirectoryExists(DiscFileSystem fileSystem, string path)
    {
        foreach (string candidate in EnumerateIsoPathCandidates(path))
        {
            try
            {
                if (fileSystem.DirectoryExists(candidate))
                {
                    return true;
                }
            }
            catch (Exception ex) when (IsExpectedReadException(ex) || IsDiscUtilsReadException(ex))
            {
                return false;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateIsoPathCandidates(string path)
    {
        string normalized = NormalizeIsoPath(path);

        string withoutVersion = normalized.EndsWith(";1", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^2]
            : normalized;

        foreach (string candidate in new[]
        {
            normalized,
            withoutVersion + ";1",
            withoutVersion.ToUpperInvariant(),
            withoutVersion.ToUpperInvariant() + ";1"
        })
        {
            yield return candidate;

            string slashCandidate = candidate.Replace('\\', '/');
            if (!string.Equals(candidate, slashCandidate, StringComparison.Ordinal))
            {
                yield return slashCandidate;
            }
        }
    }

    private static string NormalizeIsoPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "\\";
        }

        string normalized = path.Replace("/", "\\");
        if (!normalized.StartsWith('\\'))
        {
            normalized = "\\" + normalized;
        }

        return normalized;
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

    private static bool LooksLikeUdfVolume(string isoPath)
    {
        try
        {
            using FileStream stream = File.Open(isoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return UdfReader.Detect(stream);
        }
        catch (Exception ex) when (IsExpectedReadException(ex) || IsDiscUtilsReadException(ex))
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

    private static bool HasAdjacentDkey(string isoPath)
    {
        string? directory = Path.GetDirectoryName(isoPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        string baseName = Path.GetFileNameWithoutExtension(isoPath);
        return File.Exists(Path.Combine(directory, baseName + ".dkey"))
            || File.Exists(isoPath + ".dkey");
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static PS3ContentKind InferContentKind(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return PS3ContentKind.Unknown;
        }

        return category.Trim().ToUpperInvariant() switch
        {
            "DG" => PS3ContentKind.DiscGame,
            "HG" => PS3ContentKind.PsnGame,
            "GD" => PS3ContentKind.GameUpdate,
            "AP" or "HM" or "CB" => PS3ContentKind.Application,
            _ => PS3ContentKind.Unknown
        };
    }

    private static bool IsExpectedReadException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException
        or PathTooLongException
        or InvalidDataException;

    private static bool IsDiscUtilsReadException(Exception ex) =>
        ex.GetType().FullName?.Contains("DiscUtils", StringComparison.OrdinalIgnoreCase) == true
        || ex.GetType().Name.Contains("FileSystem", StringComparison.OrdinalIgnoreCase);
}
