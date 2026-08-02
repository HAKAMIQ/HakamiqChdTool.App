using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Input;

public sealed class MediaInputClassifier : IMediaInputClassifier
{
    private const int MaxProbeBytes = 4096;
    private const int ChdMaxHeaderSize = 124;

    private static readonly byte[] ChdMagic = [(byte)'M', (byte)'C', (byte)'o', (byte)'m', (byte)'p', (byte)'r', (byte)'H', (byte)'D'];
    private static readonly byte[] CsoMagic = [(byte)'C', (byte)'I', (byte)'S', (byte)'O'];
    private static readonly byte[] PkgMagic = [0x7F, 0x50, 0x4B, 0x47];

    public static readonly MediaInputClassifier Shared = new();

    public MediaInputDescriptor Classify(string? path)
    {
        return ClassifyAsync(path ?? string.Empty, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    public async ValueTask<MediaInputDescriptor> ClassifyAsync(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string originalPath = path ?? string.Empty;
        if (string.IsNullOrWhiteSpace(originalPath))
        {
            return new MediaInputDescriptor(
                originalPath,
                FullPath: null,
                MediaInputKind.Unknown,
                Exists: false,
                IsDirectory: false,
                SizeBytes: null,
                Extension: null,
                "path-empty");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(originalPath.Trim());
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return new MediaInputDescriptor(
                originalPath,
                FullPath: null,
                MediaInputKind.Unknown,
                Exists: false,
                IsDirectory: false,
                SizeBytes: null,
                Extension: null,
                "path-invalid");
        }

        if (Directory.Exists(fullPath))
        {
            return new MediaInputDescriptor(
                originalPath,
                fullPath,
                MediaInputKind.Folder,
                Exists: true,
                IsDirectory: true,
                SizeBytes: null,
                Extension: null,
                "directory-exists");
        }

        string? extension = ResolveExtension(fullPath);
        if (!File.Exists(fullPath))
        {
            return new MediaInputDescriptor(
                originalPath,
                fullPath,
                MediaInputKind.Unknown,
                Exists: false,
                IsDirectory: false,
                SizeBytes: null,
                extension,
                "path-missing");
        }

        MediaInputKind kind = ResolveKind(extension);
        long? sizeBytes = TryGetSizeBytes(fullPath);
        if (kind == MediaInputKind.Other)
        {
            return new MediaInputDescriptor(
                originalPath,
                fullPath,
                kind,
                Exists: true,
                IsDirectory: false,
                sizeBytes,
                extension,
                "file-other");
        }

        return kind switch
        {
            MediaInputKind.CHD => await ClassifyChdAsync(
                    originalPath,
                    fullPath,
                    sizeBytes,
                    extension,
                    cancellationToken)
                .ConfigureAwait(false),
            MediaInputKind.CSO => await ClassifyWithMagicAsync(
                    originalPath,
                    fullPath,
                    kind,
                    sizeBytes,
                    extension,
                    CsoMagic,
                    "magic-cso",
                    "extension-cso",
                    "extension-cso-probe-failed",
                    cancellationToken)
                .ConfigureAwait(false),
            MediaInputKind.PKG => await ClassifyWithMagicAsync(
                    originalPath,
                    fullPath,
                    kind,
                    sizeBytes,
                    extension,
                    PkgMagic,
                    "magic-pkg",
                    "extension-pkg",
                    "extension-pkg-probe-failed",
                    cancellationToken)
                .ConfigureAwait(false),
            _ => new MediaInputDescriptor(
                originalPath,
                fullPath,
                kind,
                Exists: true,
                IsDirectory: false,
                sizeBytes,
                extension,
                "extension-" + (extension?.TrimStart('.') ?? "unknown"))
        };
    }

    private static async ValueTask<MediaInputDescriptor> ClassifyChdAsync(
        string originalPath,
        string fullPath,
        long? sizeBytes,
        string? extension,
        CancellationToken cancellationToken)
    {
        MediaInputProbeStatus probeStatus;
        try
        {
            byte[] header = await ReadHeaderAtMostAsync(
                    fullPath,
                    ChdMaxHeaderSize,
                    cancellationToken)
                .ConfigureAwait(false);

            probeStatus = ProbeChdHeader(header);
        }
        catch (Exception ex) when (IsExpectedProbeException(ex))
        {
            probeStatus = MediaInputProbeStatus.ProbeUnavailable;
        }

        return new MediaInputDescriptor(
            originalPath,
            fullPath,
            MediaInputKind.CHD,
            Exists: true,
            IsDirectory: false,
            sizeBytes,
            extension,
            DetectionReasonFor(probeStatus, "chd"),
            probeStatus);
    }

    private static async ValueTask<MediaInputDescriptor> ClassifyWithMagicAsync(
        string originalPath,
        string fullPath,
        MediaInputKind kind,
        long? sizeBytes,
        string? extension,
        byte[] expectedMagic,
        string magicReason,
        string extensionReason,
        string probeFailedReason,
        CancellationToken cancellationToken)
    {
        MediaInputProbeStatus probeStatus;
        try
        {
            byte[] header = await ReadHeaderAtMostAsync(fullPath, expectedMagic.Length, cancellationToken)
                .ConfigureAwait(false);
            probeStatus = header.Length < expectedMagic.Length
                ? MediaInputProbeStatus.HeaderTruncated
                : HasPrefix(header, expectedMagic)
                    ? MediaInputProbeStatus.MagicConfirmed
                    : MediaInputProbeStatus.HeaderMismatch;
        }
        catch (Exception ex) when (IsExpectedProbeException(ex))
        {
            probeStatus = MediaInputProbeStatus.ProbeUnavailable;
        }

        string detectionReason = probeStatus switch
        {
            MediaInputProbeStatus.MagicConfirmed => magicReason,
            MediaInputProbeStatus.ProbeUnavailable => probeFailedReason,
            MediaInputProbeStatus.HeaderTruncated => $"{extensionReason}-truncated",
            _ => extensionReason
        };

        return new MediaInputDescriptor(
            originalPath,
            fullPath,
            kind,
            Exists: true,
            IsDirectory: false,
            sizeBytes,
            extension,
            detectionReason,
            probeStatus);
    }

    private static MediaInputProbeStatus ProbeChdHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < ChdMagic.Length)
        {
            return MediaInputProbeStatus.HeaderTruncated;
        }

        if (!header[..ChdMagic.Length].SequenceEqual(ChdMagic))
        {
            return MediaInputProbeStatus.HeaderMismatch;
        }

        if (header.Length < 16)
        {
            return MediaInputProbeStatus.HeaderTruncated;
        }

        uint headerLength = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(8, 4));
        uint version = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(12, 4));
        uint expectedHeaderLength = version switch
        {
            3 => 120,
            4 => 108,
            5 => 124,
            _ => 0
        };

        if (expectedHeaderLength == 0)
        {
            return MediaInputProbeStatus.UnsupportedVersion;
        }

        if (headerLength != expectedHeaderLength)
        {
            return MediaInputProbeStatus.InvalidHeaderLength;
        }

        // MAME reads MAX_HEADER_SIZE (124 bytes) before parsing any supported
        // version, so a shorter file is not a complete CHD input envelope.
        return header.Length < ChdMaxHeaderSize
            ? MediaInputProbeStatus.HeaderTruncated
            : MediaInputProbeStatus.HeaderEnvelopeValid;
    }

    private static string DetectionReasonFor(MediaInputProbeStatus status, string format) => status switch
    {
        MediaInputProbeStatus.HeaderEnvelopeValid => $"header-{format}-valid",
        MediaInputProbeStatus.HeaderMismatch => $"header-{format}-mismatch",
        MediaInputProbeStatus.HeaderTruncated => $"header-{format}-truncated",
        MediaInputProbeStatus.UnsupportedVersion => $"header-{format}-unsupported-version",
        MediaInputProbeStatus.InvalidHeaderLength => $"header-{format}-invalid-length",
        MediaInputProbeStatus.ProbeUnavailable => $"header-{format}-probe-unavailable",
        _ => $"header-{format}-not-confirmed"
    };

    private static async ValueTask<byte[]> ReadHeaderAtMostAsync(
        string path,
        int byteCount,
        CancellationToken cancellationToken)
    {
        if (byteCount is <= 0 or > MaxProbeBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        }

        byte[] buffer = new byte[byteCount];

        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: byteCount,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream
                .ReadAsync(buffer.AsMemory(totalRead), cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        if (totalRead == buffer.Length)
        {
            return buffer;
        }

        Array.Resize(ref buffer, totalRead);
        return buffer;
    }

    private static bool HasPrefix(byte[] header, byte[] expectedMagic)
    {
        if (header.Length < expectedMagic.Length)
        {
            return false;
        }

        for (int index = 0; index < expectedMagic.Length; index++)
        {
            if (header[index] != expectedMagic[index])
            {
                return false;
            }
        }

        return true;
    }

    private static long? TryGetSizeBytes(string fullPath)
    {
        try
        {
            return new FileInfo(fullPath).Length;
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return null;
        }
    }

    private static string? ResolveExtension(string fullPath)
    {
        try
        {
            string extension = Path.GetExtension(fullPath).ToLowerInvariant();
            return string.IsNullOrEmpty(extension) ? null : extension;
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return null;
        }
    }

    private static MediaInputKind ResolveKind(string? extension) => extension switch
    {
        ".iso" => MediaInputKind.ISO,
        ".pkg" => MediaInputKind.PKG,
        ".chd" => MediaInputKind.CHD,
        ".cso" => MediaInputKind.CSO,
        ".cue" => MediaInputKind.CUE,
        ".bin" => MediaInputKind.BIN,
        ".gdi" => MediaInputKind.GDI,
        _ => MediaInputKind.Other
    };

    private static bool IsExpectedProbeException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or NotSupportedException
        or PathTooLongException
        or System.Security.SecurityException;

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or IOException
        or NotSupportedException
        or PathTooLongException
        or UnauthorizedAccessException
        or System.Security.SecurityException;
}
