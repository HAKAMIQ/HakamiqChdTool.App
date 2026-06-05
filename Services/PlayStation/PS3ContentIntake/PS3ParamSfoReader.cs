using HakamiqChdTool.App.Models.PlayStation;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HakamiqChdTool.App.Services.PlayStation.PS3ContentIntake;

public sealed class PS3ParamSfoReader
{
    private const int MaxParamSfoBytes = 1024 * 1024;
    private const uint ParamSfoMagic = 0x46535000; // "\0PSF" in little-endian form.
    private const uint MaxEntryCount = 4096;
    private const int EntrySize = 16;
    private const int HeaderSize = 20;

    public PS3ContentIdentity ReadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return PS3ContentIdentity.Empty;
        }

        try
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Read(stream);
        }
        catch (Exception ex) when (IsExpectedReadException(ex))
        {
            return PS3ContentIdentity.Empty;
        }
    }

    public PS3ContentIdentity Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            byte[] data = ReadBounded(stream, MaxParamSfoBytes);
            IReadOnlyDictionary<string, string> fields = ParseFields(data);

            fields.TryGetValue("TITLE_ID", out string? titleId);
            fields.TryGetValue("TITLE", out string? titleName);
            fields.TryGetValue("CATEGORY", out string? category);

            return new PS3ContentIdentity(
                TitleId: NormalizeEmpty(titleId),
                TitleName: NormalizeEmpty(titleName),
                DiscId: null,
                Category: NormalizeEmpty(category),
                Fields: fields);
        }
        catch (Exception ex) when (IsExpectedReadException(ex))
        {
            return PS3ContentIdentity.Empty;
        }
    }

    private static IReadOnlyDictionary<string, string> ParseFields(ReadOnlySpan<byte> data)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (data.Length < HeaderSize)
        {
            return result;
        }

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        if (magic != ParamSfoMagic)
        {
            return result;
        }

        uint keyTableOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8, 4));
        uint dataTableOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12, 4));
        uint entryCount = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(16, 4));

        if (keyTableOffset >= data.Length
            || dataTableOffset >= data.Length
            || entryCount > MaxEntryCount)
        {
            return result;
        }

        ulong indexTableEnd = HeaderSize + ((ulong)entryCount * EntrySize);
        if (indexTableEnd > (ulong)data.Length)
        {
            return result;
        }

        for (uint index = 0; index < entryCount; index++)
        {
            int entryOffset = HeaderSize + ((int)index * EntrySize);

            ushort keyOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(entryOffset, 2));
            ushort dataFormat = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(entryOffset + 2, 2));
            uint dataLength = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(entryOffset + 4, 4));
            uint dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(entryOffset + 12, 4));

            ulong absoluteKeyOffset = (ulong)keyTableOffset + keyOffset;
            ulong absoluteDataOffset = (ulong)dataTableOffset + dataOffset;

            if (absoluteKeyOffset >= (ulong)data.Length
                || absoluteDataOffset >= (ulong)data.Length)
            {
                continue;
            }

            int keyStart = (int)absoluteKeyOffset;
            int dataStart = (int)absoluteDataOffset;

            string key = ReadNullTerminatedAscii(data[keyStart..]);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            int availableDataLength = data.Length - dataStart;
            int safeDataLength = (int)Math.Min(dataLength, (uint)availableDataLength);

            if (safeDataLength <= 0)
            {
                result[key] = string.Empty;
                continue;
            }

            ReadOnlySpan<byte> valueBytes = data.Slice(dataStart, safeDataLength);
            string value = dataFormat switch
            {
                0x0404 when valueBytes.Length >= 4 => BinaryPrimitives.ReadUInt32LittleEndian(valueBytes[..4]).ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => ReadNullTerminatedUtf8(valueBytes)
            };

            result[key] = value.Trim();
        }

        return result;
    }

    private static byte[] ReadBounded(Stream stream, int maxBytes)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        int length = stream.CanSeek
            ? (int)Math.Min(maxBytes, Math.Max(0, stream.Length))
            : maxBytes;

        byte[] buffer = new byte[length];
        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
            if (read <= 0)
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

    private static string ReadNullTerminatedAscii(ReadOnlySpan<byte> bytes)
    {
        int terminator = bytes.IndexOf((byte)0);
        if (terminator >= 0)
        {
            bytes = bytes[..terminator];
        }

        return Encoding.ASCII.GetString(bytes);
    }

    private static string ReadNullTerminatedUtf8(ReadOnlySpan<byte> bytes)
    {
        int terminator = bytes.IndexOf((byte)0);
        if (terminator >= 0)
        {
            bytes = bytes[..terminator];
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static string? NormalizeEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsExpectedReadException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException
        or PathTooLongException
        or InvalidDataException
        or DecoderFallbackException
        or OverflowException;
}