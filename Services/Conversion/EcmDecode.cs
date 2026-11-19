using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public sealed record EcmScanResult(bool IsValid, string MessageKey, long OutputBytes)
{
    public static EcmScanResult Fail(string messageKey) => new(false, messageKey, 0);
}

/// <summary>
/// Decodes ECM ("Error Code Modeler") CD images. Written from the public
/// container description (psx-spx, "CDROM Disk Image/Containers ECM") and the
/// ECMA-130 EDC/ECC definitions; no third-party code. Sector types 1-3 are
/// rebuilt with their sync, EDC and ECC; type 0 bytes are copied as stored.
/// The decoded data must match the EDC stored after the end marker, so a
/// damaged file or a decoding error is refused instead of producing a bad image.
/// </summary>
public static class EcmDecoder
{
    public const string InvalidMessageKey = "LocWorkflow_EcmInvalid";
    public const string ChecksumMismatchMessageKey = "LocWorkflow_EcmChecksumMismatch";
    public const string StagingFailedMessageKey = "LocWorkflow_EcmStagingFailed";

    public const int SectorBytes = 2352;
    public const long MaxOutputBytes = 64L * 1024 * 1024 * 1024;

    private const uint EndMarker = 0xFFFFFFFF;
    private const int CopyBufferBytes = 1024 * 1024;
    private static readonly byte[] Magic = [(byte)'E', (byte)'C', (byte)'M', 0];

    // Bytes read from the ECM file and written to the output, per sector type.
    private const int Mode1StoredBytes = 0x803;
    private const int Mode2Form1StoredBytes = 0x804;
    private const int Mode2Form2StoredBytes = 0x918;
    private const int Mode2OutputBytes = 0x920;

    private static readonly uint[] EdcTable = BuildEdcTable();
    private static readonly byte[] EccForward = new byte[256];
    private static readonly byte[] EccBackward = new byte[256];

    static EcmDecoder()
    {
        for (int i = 0; i < 256; i++)
        {
            int j = (i << 1) ^ ((i & 0x80) != 0 ? 0x11D : 0);
            EccForward[i] = (byte)j;
            EccBackward[i ^ j] = (byte)i;
        }
    }

    /// <summary>
    /// Walks the record headers without decoding, checks the structure and returns
    /// the exact decoded size.
    /// </summary>
    public static EcmScanResult Scan(string ecmPath)
    {
        try
        {
            using var stream = new FileStream(ecmPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
            if (!ReadMagic(stream))
            {
                return EcmScanResult.Fail(InvalidMessageKey);
            }

            long output = 0;
            while (true)
            {
                if (!TryReadRecordHeader(stream, out int type, out uint count))
                {
                    return EcmScanResult.Fail(InvalidMessageKey);
                }

                if (count == EndMarker)
                {
                    // Exactly four EDC bytes may follow the end marker.
                    return stream.Length - stream.Position == 4 && output > 0
                        ? new EcmScanResult(true, string.Empty, output)
                        : EcmScanResult.Fail(InvalidMessageKey);
                }

                long items = (long)count + 1;
                (long stored, long written) = type switch
                {
                    0 => (1L, 1L),
                    1 => (Mode1StoredBytes, SectorBytes),
                    2 => (Mode2Form1StoredBytes, Mode2OutputBytes),
                    _ => (Mode2Form2StoredBytes, Mode2OutputBytes)
                };

                long skip = items * stored;
                if (skip > stream.Length - stream.Position)
                {
                    return EcmScanResult.Fail(InvalidMessageKey);
                }

                stream.Seek(skip, SeekOrigin.Current);
                output += items * written;
                if (output > MaxOutputBytes)
                {
                    return EcmScanResult.Fail(InvalidMessageKey);
                }
            }
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or System.Security.SecurityException)
        {
            return EcmScanResult.Fail(InvalidMessageKey);
        }
    }

    /// <summary>
    /// Decodes into <paramref name="outputPath"/> (created new, never overwritten).
    /// On any failure the partial output is deleted and the reason key is returned.
    /// </summary>
    public static async Task<string?> DecodeToFileAsync(string ecmPath, string outputPath, CancellationToken cancellationToken)
    {
        bool created = false;
        bool completed = false;
        try
        {
            await using var input = new FileStream(ecmPath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
            created = true;
            string? failure = await DecodeAsync(input, output, cancellationToken).ConfigureAwait(false);
            completed = failure is null;
            return failure;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return InvalidMessageKey;
        }
        finally
        {
            // Only a file this call created is removed; an existing file is never touched.
            if (created && !completed)
            {
                TryDelete(outputPath);
            }
        }
    }

    /// <summary>Decodes a whole ECM stream. Returns null on success or the reason key.</summary>
    public static async Task<string?> DecodeAsync(Stream input, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        var reader = new BufferedStream(input, CopyBufferBytes);
        if (!ReadMagic(reader))
        {
            return InvalidMessageKey;
        }

        byte[] sector = new byte[SectorBytes];
        byte[] copy = new byte[CopyBufferBytes];
        uint edc = 0;
        long written = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryReadRecordHeader(reader, out int type, out uint count))
            {
                return InvalidMessageKey;
            }

            if (count == EndMarker)
            {
                break;
            }

            long items = (long)count + 1;
            if (type == 0)
            {
                long remaining = items;
                while (remaining > 0)
                {
                    int chunk = (int)Math.Min(remaining, copy.Length);
                    if (!await TryReadExactlyAsync(reader, copy.AsMemory(0, chunk), cancellationToken).ConfigureAwait(false))
                    {
                        return InvalidMessageKey;
                    }

                    edc = ComputeEdc(edc, copy.AsSpan(0, chunk));
                    await output.WriteAsync(copy.AsMemory(0, chunk), cancellationToken).ConfigureAwait(false);
                    remaining -= chunk;
                }

                written += items;
            }
            else
            {
                for (long i = 0; i < items; i++)
                {
                    int offset;
                    int length;
                    switch (type)
                    {
                        case 1:
                            if (!await TryReadExactlyAsync(reader, sector.AsMemory(0x0C, 3), cancellationToken).ConfigureAwait(false)
                                || !await TryReadExactlyAsync(reader, sector.AsMemory(0x10, 0x800), cancellationToken).ConfigureAwait(false))
                            {
                                return InvalidMessageKey;
                            }

                            RebuildMode1(sector);
                            offset = 0;
                            length = SectorBytes;
                            break;
                        case 2:
                            if (!await TryReadExactlyAsync(reader, sector.AsMemory(0x14, Mode2Form1StoredBytes), cancellationToken).ConfigureAwait(false))
                            {
                                return InvalidMessageKey;
                            }

                            RebuildMode2Form1(sector);
                            offset = 0x10;
                            length = Mode2OutputBytes;
                            break;
                        default:
                            if (!await TryReadExactlyAsync(reader, sector.AsMemory(0x14, Mode2Form2StoredBytes), cancellationToken).ConfigureAwait(false))
                            {
                                return InvalidMessageKey;
                            }

                            RebuildMode2Form2(sector);
                            offset = 0x10;
                            length = Mode2OutputBytes;
                            break;
                    }

                    edc = ComputeEdc(edc, sector.AsSpan(offset, length));
                    await output.WriteAsync(sector.AsMemory(offset, length), cancellationToken).ConfigureAwait(false);
                    written += length;
                }
            }

            if (written > MaxOutputBytes)
            {
                return InvalidMessageKey;
            }
        }

        byte[] stored = new byte[4];
        if (!await TryReadExactlyAsync(reader, stored, cancellationToken).ConfigureAwait(false)
            || reader.ReadByte() != -1
            || written == 0)
        {
            return InvalidMessageKey;
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return BinaryPrimitives.ReadUInt32LittleEndian(stored) == edc ? null : ChecksumMismatchMessageKey;
    }

    /// <summary>EDC as defined by ECMA-130 (CRC-32, reflected polynomial 0xD8018001, no inversion).</summary>
    public static uint ComputeEdc(uint edc, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            edc = (edc >> 8) ^ EdcTable[(edc ^ value) & 0xFF];
        }

        return edc;
    }

    /// <summary>Fills sync, mode, EDC and ECC of a Mode 1 sector whose address (0Ch..0Eh) and data (10h..80Fh) are set.</summary>
    public static void RebuildMode1(Span<byte> sector)
    {
        WriteSync(sector);
        sector[0x0F] = 0x01;
        BinaryPrimitives.WriteUInt32LittleEndian(sector[0x810..], ComputeEdc(0, sector[..0x810]));
        sector.Slice(0x814, 8).Clear();
        WriteEcc(sector);
    }

    /// <summary>Fills the subheader copy, EDC and ECC of a Mode 2 Form 1 sector whose subheader (14h..17h) and data (18h..817h) are set.</summary>
    public static void RebuildMode2Form1(Span<byte> sector)
    {
        sector.Slice(0x14, 4).CopyTo(sector[0x10..]);
        BinaryPrimitives.WriteUInt32LittleEndian(sector[0x818..], ComputeEdc(0, sector[0x10..0x818]));

        // ECC of a Mode 2 sector is computed with a zero header.
        Span<byte> header = stackalloc byte[4];
        sector.Slice(0x0C, 4).CopyTo(header);
        sector.Slice(0x0C, 4).Clear();
        WriteEcc(sector);
        header.CopyTo(sector[0x0C..]);
    }

    /// <summary>Fills the subheader copy and EDC of a Mode 2 Form 2 sector whose subheader (14h..17h) and data (18h..92Bh) are set.</summary>
    public static void RebuildMode2Form2(Span<byte> sector)
    {
        sector.Slice(0x14, 4).CopyTo(sector[0x10..]);
        BinaryPrimitives.WriteUInt32LittleEndian(sector[0x92C..], ComputeEdc(0, sector[0x10..0x92C]));
    }

    private static void WriteSync(Span<byte> sector)
    {
        sector[0] = 0x00;
        sector.Slice(1, 10).Fill(0xFF);
        sector[11] = 0x00;
    }

    // ECMA-130 Annex A: P parity over 86 columns of 24 bytes, Q parity over 52 diagonals of 43 bytes,
    // both starting at the header (0Ch).
    private static void WriteEcc(Span<byte> sector)
    {
        ComputeEccBlock(sector, 86, 24, 2, 86, 0x81C);
        ComputeEccBlock(sector, 52, 43, 86, 88, 0x8C8);
    }

    private static void ComputeEccBlock(Span<byte> sector, int majorCount, int minorCount, int majorMultiplier, int minorIncrement, int destination)
    {
        Span<byte> source = sector[0x0C..];
        int size = majorCount * minorCount;
        for (int major = 0; major < majorCount; major++)
        {
            int index = (major >> 1) * majorMultiplier + (major & 1);
            byte eccA = 0;
            byte eccB = 0;
            for (int minor = 0; minor < minorCount; minor++)
            {
                byte value = source[index];
                index += minorIncrement;
                if (index >= size)
                {
                    index -= size;
                }

                eccA ^= value;
                eccB ^= value;
                eccA = EccForward[eccA];
            }

            eccA = EccBackward[EccForward[eccA] ^ eccB];
            sector[destination + major] = eccA;
            sector[destination + major + majorCount] = (byte)(eccA ^ eccB);
        }
    }

    private static uint[] BuildEdcTable()
    {
        uint[] table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value >> 1) ^ ((value & 1) != 0 ? 0xD8018001u : 0u);
            }

            table[i] = value;
        }

        return table;
    }

    private static bool ReadMagic(Stream stream)
    {
        Span<byte> magic = stackalloc byte[4];
        return stream.ReadAtLeast(magic, 4, throwOnEndOfStream: false) == 4 && magic.SequenceEqual(Magic);
    }

    // 1st byte: bit7 more, bits6-2 count bits 0-4, bits1-0 type; then up to four bytes of 7, 7, 7 and 6 count bits.
    private static bool TryReadRecordHeader(Stream stream, out int type, out uint count)
    {
        type = 0;
        count = 0;
        int value = stream.ReadByte();
        if (value < 0)
        {
            return false;
        }

        type = value & 3;
        ulong number = (ulong)((value >> 2) & 0x1F);
        int shift = 5;
        int bytes = 1;
        while ((value & 0x80) != 0)
        {
            value = stream.ReadByte();
            if (value < 0 || ++bytes > 5)
            {
                return false;
            }

            number |= (ulong)(value & 0x7F) << shift;
            shift += 7;
        }

        if (number > uint.MaxValue)
        {
            return false;
        }

        count = (uint)number;
        // Only the end marker may use the top bit; any other record that large is not a real image.
        return count == EndMarker || count < 0x80000000u;
    }

    private static async Task<bool> TryReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        return read == buffer.Length;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The temp workspace is deleted as a whole afterwards.
        }
    }
}
