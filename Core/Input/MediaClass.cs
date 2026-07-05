using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Input;

public sealed class MediaInputClassifier : IMediaInputClassifier
{
    private const int MaxProbeBytes = 4096;

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
            MediaInputKind.CHD => await ClassifyWithMagicAsync(
                    originalPath,
                    fullPath,
                    kind,
                    sizeBytes,
                    extension,
                    ChdMagic,
                    "magic-chd",
                    "extension-chd",
                    "extension-chd-probe-failed",
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
        string detectionReason;
        try
        {
            byte[] header = await ReadHeaderAsync(fullPath, expectedMagic.Length, cancellationToken)
                .ConfigureAwait(false);
            detectionReason = HasPrefix(header, expectedMagic)
                ? magicReason
                : extensionReason;
        }
        catch (Exception ex) when (IsExpectedProbeException(ex))
        {
            detectionReason = probeFailedReason;
        }

        return new MediaInputDescriptor(
            originalPath,
            fullPath,
            kind,
            Exists: true,
            IsDirectory: false,
            sizeBytes,
            extension,
            detectionReason);
    }

    private static async ValueTask<byte[]> ReadHeaderAsync(
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

        int read = await stream.ReadAsync(buffer.AsMemory(0, byteCount), cancellationToken)
            .ConfigureAwait(false);

        if (read == buffer.Length)
        {
            return buffer;
        }

        Array.Resize(ref buffer, read);
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
