using HakamiqChdTool.App.Core.Disc;
using HakamiqChdTool.App.Models;
using System.Buffers.Binary;
using System.IO.Compression;
using Serilog;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace HakamiqChdTool.App.Services;

public sealed record DeepHashAnalysisResult(
    IntegrityValidationState State,
    string StatusMessageKey,
    string DetailTooltipKey,
    IReadOnlyList<object?> DetailArgs,
    IReadOnlyList<DeepHashFileDigest> HashedFiles,
    IReadOnlyList<DeepHashMatch> Matches,
    IReadOnlyList<string> UnmatchedFileNames,
    string SuggestedStandardName = "",
    string MatchedSystemName = "",
    string MatchedGameName = "",
    int MatchedFileCount = 0,
    int HashedFileCount = 0,
    string FailureCode = "")
{
    public bool IsFatalInputReadFailure =>
        string.Equals(
            FailureCode,
            DeepHashAnalyzer.InputReadCrcOrIoFailureCode,
            StringComparison.Ordinal);
}

public sealed record DeepHashFileDigest(
    string Path,
    long SizeBytes,
    string Md5,
    string Sha1);

public sealed record DeepHashMatch(
    string FilePath,
    long SizeBytes,
    string Md5,
    string Sha1,
    string SystemName,
    string GameName,
    string RomName,
    string MatchSource,
    string Crc);

public static class DeepHashAnalyzer
{
    private const int BufferSize = 1024 * 1024;

    public const string InputReadCrcOrIoFailureCode = "InputReadCrcOrIoFailure";

    private const string StatusErrorKey = "LocDeepHash_StatusError";
    private const string StatusInputReadFailureKey = "LocDeepHash_StatusInputReadFailure";
    private const string StatusRequiresRawImageKey = "LocDeepHash_StatusRequiresRawImage";
    private const string StatusUnsupportedDirectKey = "LocDeepHash_StatusUnsupportedDirect";
    private const string StatusUnsupportedKey = "LocDeepHash_StatusUnsupported";
    private const string StatusNoDatabaseKey = "LocDeepHash_StatusNoDatabase";
    private const string StatusConflictingMatchKey = "LocDeepHash_StatusConflictingMatch";
    private const string StatusVerifiedKey = "LocDeepHash_StatusVerified";
    private const string StatusVerifiedCompleteKey = "LocDeepHash_StatusVerifiedComplete";
    private const string StatusIncompleteKey = "LocDeepHash_StatusIncomplete";
    private const string StatusModifiedKey = "LocDeepHash_StatusModified";
    private const string StatusCsoVirtualIsoNoRedumpMatchKey = "LocDeepHash_StatusCsoVirtualIsoNoRedumpMatch";

    private const string TipNoPathKey = "LocDeepHash_TipNoPath";
    private const string TipInvalidPathKey = "LocDeepHash_TipInvalidPath";
    private const string TipFileNotFoundKey = "LocDeepHash_TipFileNotFound";
    private const string TipChdNeedsExtractionKey = "LocDeepHash_TipChdNeedsExtraction";
    private const string TipArchiveNeedsExtractionKey = "LocDeepHash_TipArchiveNeedsExtraction";
    private const string TipCsoNeedsIsoKey = "LocDeepHash_TipCsoNeedsIso";
    private const string TipUnsupportedExtensionKey = "LocDeepHash_TipUnsupportedExtension";
    private const string TipNoTrackFilesKey = "LocDeepHash_TipNoTrackFiles";
    private const string TipResolveFailedKey = "LocDeepHash_TipResolveFailed";
    private const string TipHashFailedKey = "LocDeepHash_TipHashFailed";
    private const string TipInputReadCrcOrIoFailureKey = "LocDeepHash_TipInputReadCrcOrIoFailure";
    private const string StatusMissingPlatformDatabaseKey = "LocDeepHash_StatusMissingPlatformDatabase";
    private const string TipNoDatabaseKey = "LocDeepHash_TipNoDatabase";
    private const string TipMissingPlatformDatabaseKey = "LocDeepHash_TipMissingPlatformDatabase";
    private const string TipConflictingMatchesKey = "LocDeepHash_TipConflictingMatches";
    private const string TipVerifiedHeaderKey = "LocDeepHash_TipVerifiedHeader";
    private const string TipPartialMatchKey = "LocDeepHash_TipPartialMatch";
    private const string TipNoRedumpMatchKey = "LocDeepHash_TipNoRedumpMatch";
    private const string TipCsoVirtualIsoNoRedumpMatchKey = "LocDeepHash_TipCsoVirtualIsoNoRedumpMatch";

    private static readonly HashSet<string> HashableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cue", ".gdi", ".iso", ".bin", ".img", ".raw"
    };

    private static readonly HashSet<string> ArchiveNoDirectExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z"
    };

    public static async Task<DeepHashAnalysisResult> DeepHashAnalyzeAsync(
        string probePath,
        RedumpSqliteManager? redumpDatabase,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(probePath))
        {
            return Error(TipNoPathKey);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(probePath.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Log.Debug(ex, "DeepHashAnalyzer: invalid probe path. Path={Path}", probePath);
            return Error(TipInvalidPathKey);
        }

        if (!File.Exists(fullPath))
        {
            return Error(TipFileNotFoundKey);
        }

        string extension = Path.GetExtension(fullPath);

        if (string.Equals(extension, ".chd", StringComparison.OrdinalIgnoreCase))
        {
            return Result(
                IntegrityValidationState.NoDirectRedump,
                StatusRequiresRawImageKey,
                TipChdNeedsExtractionKey,
                [fullPath]);
        }

        if (string.Equals(extension, ".cso", StringComparison.OrdinalIgnoreCase))
        {
            return await AnalyzeCsoVirtualIsoAsync(
                    fullPath,
                    redumpDatabase,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (ArchiveNoDirectExtensions.Contains(extension))
        {
            return Result(
                IntegrityValidationState.NoDirectRedump,
                StatusUnsupportedDirectKey,
                TipArchiveNeedsExtractionKey,
                [fullPath]);
        }

        if (!HashableExtensions.Contains(extension))
        {
            Log.Debug("DeepHashAnalyzer: skipped unsupported Redump extension {Extension} for {Path}", extension, fullPath);
            return Result(
                IntegrityValidationState.Unsupported,
                StatusUnsupportedKey,
                TipUnsupportedExtensionKey,
                [extension, fullPath]);
        }

        IReadOnlyList<string> filesToHash;
        try
        {
            filesToHash = ResolveFilesToHash(fullPath);
        }
        catch (Exception ex) when (IsInputReadFailureException(ex))
        {
            Log.Warning(ex, "DeepHashAnalyzer: input read failed while resolving hash files. Path={Path}; FailureCode={FailureCode}", fullPath, InputReadCrcOrIoFailureCode);
            return InputReadFailure();
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            Log.Debug(ex, "DeepHashAnalyzer: failed to resolve files to hash. Path={Path}", fullPath);
            return Error(TipResolveFailedKey);
        }

        if (filesToHash.Count == 0)
        {
            return Error(TipNoTrackFilesKey);
        }

        List<DeepHashFileDigest> hashed;
        try
        {
            hashed = await Task.Run(
                () => HashAllFiles(filesToHash, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsInputReadFailureException(ex))
        {
            Log.Warning(ex, "DeepHashAnalyzer: input read failed while hashing. Path={Path}; FailureCode={FailureCode}", fullPath, InputReadCrcOrIoFailureCode);
            return InputReadFailure();
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or NotSupportedException)
        {
            Log.Debug(ex, "DeepHashAnalyzer: hashing failed. Path={Path}", fullPath);
            return Error(TipHashFailedKey);
        }

        if (redumpDatabase is null || !redumpDatabase.HasAnyRows())
        {
            return Result(
                IntegrityValidationState.NoDat,
                StatusNoDatabaseKey,
                TipNoDatabaseKey,
                hashedFiles: hashed);
        }

        var matches = new List<DeepHashMatch>();
        var misses = new List<string>();

        foreach (DeepHashFileDigest file in hashed)
        {
            if (redumpDatabase.TryMatchHash(file.Md5, file.Sha1, file.SizeBytes, out RedumpRomHit hit))
            {
                matches.Add(ToMatch(file, hit));
            }
            else
            {
                misses.Add(Path.GetFileName(file.Path));
            }
        }

        if (matches.Count == hashed.Count)
        {
            return BuildFullMatchResult(fullPath, hashed, matches);
        }

        if (matches.Count > 0)
        {
            return Result(
                IntegrityValidationState.Failed,
                StatusIncompleteKey,
                TipPartialMatchKey,
                [matches.Count, hashed.Count],
                hashed,
                matches,
                misses);
        }

        DeepHashAnalysisResult? missingPlatformDatabase = await TryBuildMissingPlatformDatabaseResultAsync(
                fullPath,
                redumpDatabase,
                hashed,
                cancellationToken)
            .ConfigureAwait(false);

        if (missingPlatformDatabase is not null)
        {
            return missingPlatformDatabase;
        }

        return Result(
            IntegrityValidationState.NoRedumpMatch,
            StatusModifiedKey,
            TipNoRedumpMatchKey,
            hashedFiles: hashed);
    }

    private static async Task<DeepHashAnalysisResult> AnalyzeCsoVirtualIsoAsync(
        string fullPath,
        RedumpSqliteManager? redumpDatabase,
        CancellationToken cancellationToken)
    {
        if (redumpDatabase is null)
        {
            return Result(
                IntegrityValidationState.NoDirectRedump,
                StatusRequiresRawImageKey,
                TipCsoNeedsIsoKey,
                [fullPath]);
        }

        if (!redumpDatabase.HasAnyRows())
        {
            return Result(
                IntegrityValidationState.NoDat,
                StatusNoDatabaseKey,
                TipNoDatabaseKey);
        }

        List<DeepHashFileDigest> hashed;

        try
        {
            DeepHashFileDigest digest = await Task.Run(
                    () => HashCsoVirtualIso(fullPath, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            hashed = [digest];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsInputReadFailureException(ex))
        {
            Log.Warning(ex, "DeepHashAnalyzer: CSO input read failed while hashing virtual ISO. Path={Path}; FailureCode={FailureCode}", fullPath, InputReadCrcOrIoFailureCode);
            return InputReadFailure();
        }
        catch (Exception ex) when (ex is InvalidDataException or CryptographicException or ArgumentException or NotSupportedException)
        {
            Log.Debug(ex, "DeepHashAnalyzer: CSO virtual ISO hashing failed. Path={Path}", fullPath);
            return Error(TipHashFailedKey);
        }

        var matches = new List<DeepHashMatch>();

        foreach (DeepHashFileDigest file in hashed)
        {
            if (redumpDatabase.TryMatchHash(file.Md5, file.Sha1, file.SizeBytes, out RedumpRomHit hit))
            {
                matches.Add(ToMatch(file, hit));
            }
        }

        if (matches.Count == hashed.Count)
        {
            return BuildFullMatchResult(fullPath, hashed, matches);
        }


        return Result(
            IntegrityValidationState.NoRedumpMatch,
            StatusCsoVirtualIsoNoRedumpMatchKey,
            TipCsoVirtualIsoNoRedumpMatchKey,
            hashedFiles: hashed);
    }

    private static DeepHashFileDigest HashCsoVirtualIso(
        string csoPath,
        CancellationToken cancellationToken)
    {
        using FileStream stream = new(
            csoPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: BufferSize,
            FileOptions.SequentialScan);

        CsoHeader header = ReadCsoHeader(stream);

        long blockCount64 = checked((long)((header.TotalBytes + header.BlockSize - 1UL) / header.BlockSize));
        if (blockCount64 <= 0 || blockCount64 > int.MaxValue - 1)
        {
            throw new InvalidDataException("Unsupported CSO block count.");
        }

        int blockCount = (int)blockCount64;
        uint[] index = ReadCsoIndex(stream, blockCount);

        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);

        byte[] output = new byte[checked((int)header.BlockSize)];
        ulong logicalOffset = 0;

        for (int block = 0; block < blockCount; block++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CsoBlockLocation location = ResolveCsoBlockLocation(
                index,
                block,
                header.AlignmentShift,
                stream.Length);

            int expectedBytes = checked((int)Math.Min(
                (ulong)header.BlockSize,
                header.TotalBytes - logicalOffset));

            if (expectedBytes <= 0)
            {
                throw new InvalidDataException("Invalid CSO logical block size.");
            }

            if (location.IsPlain)
            {
                stream.Position = location.Offset;
                ReadExactly(stream, output, expectedBytes);
            }
            else
            {
                if (location.Length <= 0 || location.Length > int.MaxValue)
                {
                    throw new InvalidDataException("Invalid CSO compressed block size.");
                }

                byte[] compressed = new byte[(int)location.Length];
                stream.Position = location.Offset;
                ReadExactly(stream, compressed, compressed.Length);
                DecompressCsoBlock(compressed, output, expectedBytes);
            }

            ReadOnlySpan<byte> span = output.AsSpan(0, expectedBytes);
            md5.AppendData(span);
            sha1.AppendData(span);

            logicalOffset += (ulong)expectedBytes;
        }

        if (logicalOffset != header.TotalBytes)
        {
            throw new InvalidDataException("CSO logical size mismatch.");
        }

        string md5Hex = Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant();
        string sha1Hex = Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant();

        if (header.TotalBytes > long.MaxValue)
        {
            throw new InvalidDataException("CSO logical size is too large.");
        }

        return new DeepHashFileDigest(
            BuildCsoVirtualIsoDisplayPath(csoPath),
            (long)header.TotalBytes,
            md5Hex,
            sha1Hex);
    }

    private static string BuildCsoVirtualIsoDisplayPath(string csoPath)
    {
        string? directory = Path.GetDirectoryName(csoPath);
        string nameWithoutExtension = Path.GetFileNameWithoutExtension(csoPath);

        string displayName = string.IsNullOrWhiteSpace(nameWithoutExtension)
            ? "CSO virtual ISO.iso"
            : $"{nameWithoutExtension} [CSO virtual ISO].iso";

        return string.IsNullOrWhiteSpace(directory)
            ? displayName
            : Path.Combine(directory, displayName);
    }
    private static CsoHeader ReadCsoHeader(FileStream stream)
    {
        byte[] header = new byte[24];
        ReadExactly(stream, header, header.Length);

        if (header[0] != (byte)'C'
            || header[1] != (byte)'I'
            || header[2] != (byte)'S'
            || header[3] != (byte)'O')
        {
            throw new InvalidDataException("Invalid CSO magic.");
        }

        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));
        ulong totalBytes = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(8, 8));
        uint blockSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16, 4));
        byte alignmentShift = header[21];

        if (headerSize < 24 || headerSize > stream.Length)
        {
            throw new InvalidDataException("Invalid CSO header size.");
        }

        if (totalBytes == 0 || blockSize == 0 || blockSize > 1024 * 1024)
        {
            throw new InvalidDataException("Invalid CSO logical geometry.");
        }

        if (alignmentShift > 31)
        {
            throw new InvalidDataException("Invalid CSO alignment shift.");
        }

        stream.Position = headerSize;

        return new CsoHeader(
            totalBytes,
            blockSize,
            alignmentShift);
    }

    private static uint[] ReadCsoIndex(
        FileStream stream,
        int blockCount)
    {
        uint[] index = new uint[blockCount + 1];
        byte[] raw = new byte[checked((blockCount + 1) * sizeof(uint))];

        ReadExactly(stream, raw, raw.Length);

        for (int i = 0; i < index.Length; i++)
        {
            index[i] = BinaryPrimitives.ReadUInt32LittleEndian(
                raw.AsSpan(i * sizeof(uint), sizeof(uint)));
        }

        return index;
    }

    private static CsoBlockLocation ResolveCsoBlockLocation(
        uint[] index,
        int block,
        byte alignmentShift,
        long streamLength)
    {
        uint rawCurrent = index[block];
        uint rawNext = index[block + 1];

        bool isPlain = (rawCurrent & 0x80000000U) != 0;

        long current = checked((long)(rawCurrent & 0x7FFFFFFFU) << alignmentShift);
        long next = checked((long)(rawNext & 0x7FFFFFFFU) << alignmentShift);

        if (current < 0 || next < current || next > streamLength)
        {
            throw new InvalidDataException("Invalid CSO block index.");
        }

        return new CsoBlockLocation(
            current,
            next - current,
            isPlain);
    }

    private static void DecompressCsoBlock(
        byte[] compressed,
        byte[] output,
        int expectedBytes)
    {
        if (TryDecompressCsoBlock(compressed, output, expectedBytes, useZLib: true))
        {
            return;
        }

        if (TryDecompressCsoBlock(compressed, output, expectedBytes, useZLib: false))
        {
            return;
        }

        throw new InvalidDataException("CSO compressed block could not be decompressed.");
    }

    private static bool TryDecompressCsoBlock(
        byte[] compressed,
        byte[] output,
        int expectedBytes,
        bool useZLib)
    {
        try
        {
            using var input = new MemoryStream(compressed, writable: false);
            using Stream inflater = useZLib
                ? new ZLibStream(input, CompressionMode.Decompress, leaveOpen: false)
                : new DeflateStream(input, CompressionMode.Decompress, leaveOpen: false);

            ReadExactly(inflater, output, expectedBytes);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static void ReadExactly(
        Stream stream,
        byte[] buffer,
        int count)
    {
        int offset = 0;

        while (offset < count)
        {
            int read = stream.Read(buffer, offset, count - offset);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of stream.");
            }

            offset += read;
        }
    }

    private readonly record struct CsoHeader(
        ulong TotalBytes,
        uint BlockSize,
        byte AlignmentShift);

    private readonly record struct CsoBlockLocation(
        long Offset,
        long Length,
        bool IsPlain);

    private static async Task<DeepHashAnalysisResult?> TryBuildMissingPlatformDatabaseResultAsync(
        string fullPath,
        RedumpSqliteManager redumpDatabase,
        IReadOnlyList<DeepHashFileDigest> hashed,
        CancellationToken cancellationToken)
    {
        ConsoleIdResult detected = await ConsoleIdSvc
            .DetectAsync(fullPath, TimeSpan.FromSeconds(5), cancellationToken)
            .ConfigureAwait(false);

        if (!detected.IsIdentified)
        {
            return null;
        }

        if (redumpDatabase.HasSystemRowsForDetectedPlatform(detected.PlatformName))
        {
            return null;
        }

        return Result(
            IntegrityValidationState.NoDat,
            StatusMissingPlatformDatabaseKey,
            TipMissingPlatformDatabaseKey,
            [detected.PlatformName],
            hashedFiles: hashed);
    }

    private static DeepHashAnalysisResult BuildFullMatchResult(
        string fullPath,
        IReadOnlyList<DeepHashFileDigest> hashed,
        IReadOnlyList<DeepHashMatch> matches)
    {
        if (!MatchesBelongToOneDisc(matches))
        {
            return Result(
                IntegrityValidationState.Failed,
                StatusConflictingMatchKey,
                TipConflictingMatchesKey,
                hashedFiles: hashed,
                matches: matches);
        }

        DeepHashMatch first = matches[0];
        string statusKey = matches.Count == 1
            ? StatusVerifiedKey
            : StatusVerifiedCompleteKey;

        return Result(
            IntegrityValidationState.Verified,
            statusKey,
            TipVerifiedHeaderKey,
            hashedFiles: hashed,
            matches: matches,
            suggestedStandardName: BuildSuggestedStandardFileName(first.GameName, fullPath),
            matchedSystemName: first.SystemName,
            matchedGameName: first.GameName,
            matchedFileCount: matches.Count,
            hashedFileCount: hashed.Count);
    }

    private static DeepHashAnalysisResult Error(string detailKey) =>
        Result(IntegrityValidationState.Error, StatusErrorKey, detailKey);

    private static DeepHashAnalysisResult InputReadFailure() =>
        Result(
            IntegrityValidationState.Error,
            StatusInputReadFailureKey,
            TipInputReadCrcOrIoFailureKey,
            failureCode: InputReadCrcOrIoFailureCode);

    private static DeepHashAnalysisResult Result(
        IntegrityValidationState state,
        string statusKey,
        string detailKey,
        IReadOnlyList<object?>? detailArgs = null,
        IReadOnlyList<DeepHashFileDigest>? hashedFiles = null,
        IReadOnlyList<DeepHashMatch>? matches = null,
        IReadOnlyList<string>? unmatchedFileNames = null,
        string suggestedStandardName = "",
        string matchedSystemName = "",
        string matchedGameName = "",
        int? matchedFileCount = null,
        int? hashedFileCount = null,
        string failureCode = "")
    {
        IReadOnlyList<DeepHashFileDigest> resolvedHashedFiles = hashedFiles ?? [];
        IReadOnlyList<DeepHashMatch> resolvedMatches = matches ?? [];

        return new DeepHashAnalysisResult(
            state,
            statusKey,
            detailKey,
            detailArgs ?? [],
            resolvedHashedFiles,
            resolvedMatches,
            unmatchedFileNames ?? [],
            suggestedStandardName,
            matchedSystemName,
            matchedGameName,
            matchedFileCount ?? resolvedMatches.Count,
            hashedFileCount ?? resolvedHashedFiles.Count,
            failureCode);
    }

    private static List<DeepHashFileDigest> HashAllFiles(
        IReadOnlyList<string> files,
        CancellationToken cancellationToken)
    {
        var result = new List<DeepHashFileDigest>(files.Count);

        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileInfo info = new(file);
            (string md5, string sha1) = ComputeMd5Sha1Sequential(file, cancellationToken);
            result.Add(new DeepHashFileDigest(file, info.Length, md5, sha1));
        }

        return result;
    }

    private static (string Md5Lower, string Sha1Lower) ComputeMd5Sha1Sequential(
        string filePath,
        CancellationToken cancellationToken)
    {
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buffer = new byte[BufferSize];

        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: BufferSize,
            FileOptions.SequentialScan);

        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ReadOnlySpan<byte> span = buffer.AsSpan(0, read);
            md5.AppendData(span);
            sha1.AppendData(span);
        }

        string md5Hex = Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant();
        string sha1Hex = Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant();
        return (md5Hex, sha1Hex);
    }

    private static IReadOnlyList<string> ResolveFilesToHash(string fullProbePath)
    {
        string extension = Path.GetExtension(fullProbePath);
        string normalized = Path.GetFullPath(fullProbePath);

        return extension.ToLowerInvariant() switch
        {
            ".cue" => ResolveCueBinFiles(normalized),
            ".gdi" => ResolveGdiTrackFiles(normalized),
            ".iso" or ".bin" or ".img" or ".raw" => [normalized],
            _ => []
        };
    }

    private static IReadOnlyList<string> ResolveCueBinFiles(string cuePath)
    {
        string? directory = Path.GetDirectoryName(cuePath);
        if (string.IsNullOrEmpty(directory))
        {
            return [];
        }

        string baseDirectory = Path.GetFullPath(directory);
        var names = new List<string>();

        foreach (string line in File.ReadLines(cuePath, Encoding.UTF8))
        {
            if (CueSheetFileStatementReader.TryRead(line, out string name, out _))
            {
                names.Add(name);
            }
        }

        return ResolveDescriptorFileNames(baseDirectory, names);
    }

    private static IReadOnlyList<string> ResolveGdiTrackFiles(string gdiPath)
    {
        string? directory = Path.GetDirectoryName(gdiPath);
        if (string.IsNullOrEmpty(directory))
        {
            return [];
        }

        string baseDirectory = Path.GetFullPath(directory);
        var names = new List<string>();
        bool skippedHeader = false;

        foreach (string line in File.ReadLines(gdiPath, Encoding.UTF8))
        {
            if (!skippedHeader)
            {
                skippedHeader = true;
                continue;
            }

            if (TryExtractGdiTrackFileName(line, out string name))
            {
                names.Add(name);
            }
        }

        return ResolveDescriptorFileNames(baseDirectory, names);
    }

    private static bool TryExtractGdiTrackFileName(string line, out string fileName)
    {
        fileName = string.Empty;

        ReadOnlySpan<char> span = line.AsSpan().Trim();
        if (span.Length == 0 || span[0] == '#')
        {
            return false;
        }

        for (int field = 0; field < 4; field++)
        {
            if (!TryConsumeToken(ref span, out _))
            {
                return false;
            }
        }

        span = span.TrimStart();
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

        if (!TryConsumeToken(ref span, out ReadOnlySpan<char> token))
        {
            return false;
        }

        fileName = token.Trim('"').ToString().Trim();
        return fileName.Length > 0;
    }

    private static bool TryConsumeToken(ref ReadOnlySpan<char> span, out ReadOnlySpan<char> token)
    {
        span = span.TrimStart();
        token = default;

        if (span.Length == 0)
        {
            return false;
        }

        int separator = IndexOfWhiteSpace(span);
        if (separator < 0)
        {
            token = span;
            span = [];
            return token.Length > 0;
        }

        token = span[..separator];
        span = span[(separator + 1)..];
        return token.Length > 0;
    }

    private static IReadOnlyList<string> ResolveDescriptorFileNames(string baseDirectory, IReadOnlyList<string> names)
    {
        var resolved = new List<string>();
        var missing = new List<string>();

        foreach (string relativePath in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string combined = Path.GetFullPath(Path.Combine(baseDirectory, relativePath));
            if (!IsUnderDirectory(baseDirectory, combined))
            {
                missing.Add(relativePath);
                continue;
            }

            if (File.Exists(combined))
            {
                resolved.Add(combined);
            }
            else
            {
                missing.Add(relativePath);
            }
        }

        if (missing.Count > 0)
        {
            throw new FileNotFoundException("Disc descriptor references missing files.");
        }

        return resolved;
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

    private static DeepHashMatch ToMatch(DeepHashFileDigest file, RedumpRomHit hit) => new(
        file.Path,
        file.SizeBytes,
        file.Md5,
        file.Sha1,
        hit.SystemName,
        hit.GameName,
        hit.RomName,
        hit.MatchSource,
        hit.Crc ?? string.Empty);

    private static string BuildSuggestedStandardFileName(string redumpGameName, string originalPath)
    {
        string extension = Path.GetExtension(originalPath);
        string safeBaseName = SanitizeFileName(redumpGameName);
        return string.IsNullOrWhiteSpace(safeBaseName) ? string.Empty : safeBaseName + extension;
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (char character in value.Trim())
        {
            builder.Append(invalid.Contains(character) ? ' ' : character);
        }

        string collapsed = System.Text.RegularExpressions.Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
        return collapsed.TrimEnd('.', ' ');
    }

    private static bool MatchesBelongToOneDisc(IEnumerable<DeepHashMatch> matches)
    {
        string? system = null;
        string? game = null;

        foreach (DeepHashMatch match in matches)
        {
            if (system is null)
            {
                system = match.SystemName;
                game = match.GameName;
                continue;
            }

            if (!string.Equals(system, match.SystemName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(game, match.GameName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
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

    private static bool IsInputReadFailureException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException;
}
