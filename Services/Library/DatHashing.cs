using HakamiqChdTool.App.Core.Library;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services.Library;

/// <summary>
/// Supplies <see cref="DatMatcher"/> with streaming hashes. SHA1 and MD5 reuse
/// <see cref="FileHashService"/> (1 MiB buffer, cancellable, path-validated). The existing
/// service has no CRC32, so <see cref="Crc32Streaming"/> adds it with the same bounds.
/// </summary>
public static class DatHashing
{
    public static DatContentHasher Hasher { get; } = HashAsync;

    private static async Task<string> HashAsync(string path, DatHashKind kind, CancellationToken cancellationToken)
    {
        if (kind == DatHashKind.Crc32)
        {
            return await Crc32Streaming.ComputeAsync(path, cancellationToken).ConfigureAwait(false);
        }

        FileHashResult result = await FileHashService
            .ComputeAsync(
                path,
                kind == DatHashKind.Sha1 ? FileHashAlgorithm.SHA1 : FileHashAlgorithm.MD5,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Hex;
    }
}

/// <summary>
/// Bounded-memory CRC-32 (IEEE 802.3, reflected, polynomial 0xEDB88320; the CRC used by ZIP,
/// PNG and DAT files). Reads in 1 MiB chunks and observes cancellation between chunks.
/// </summary>
public static class Crc32Streaming
{
    private const int BufferSize = 1024 * 1024;
    private static readonly uint[] Table = BuildTable();

    /// <summary>Folds <paramref name="data"/> into a running CRC. Start with 0.</summary>
    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        uint value = ~crc;

        foreach (byte b in data)
        {
            value = Table[(value ^ b) & 0xFF] ^ (value >> 8);
        }

        return ~value;
    }

    public static string ToHex(uint crc) => crc.ToString("x8");

    public static async Task<string> ComputeAsync(string path, CancellationToken cancellationToken)
    {
        string fullPath;

        try
        {
            fullPath = Path.GetFullPath(path);
            ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(path));
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or NotSupportedException
                                   or PathTooLongException
                                   or UnauthorizedAccessException
                                   or System.Security.SecurityException)
        {
            throw new ArgumentException("Invalid path for hashing.", nameof(path), ex);
        }

        await using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: BufferSize,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        byte[] buffer = new byte[BufferSize];
        uint crc = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int read = await stream
                .ReadAsync(buffer.AsMemory(), cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                return ToHex(crc);
            }

            crc = Update(crc, buffer.AsSpan(0, read));
        }
    }

    private static uint[] BuildTable()
    {
        uint[] table = new uint[256];

        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;

            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}
