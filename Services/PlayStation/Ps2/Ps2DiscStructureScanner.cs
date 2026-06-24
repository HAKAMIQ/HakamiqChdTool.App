using HakamiqChdTool.App.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HakamiqChdTool.App.Services.PlayStation.Ps2;

internal static class Ps2DiscStructureScanner
{
    private const int UserDataSectorSize = 2048;
    private const int PrimaryVolumeDescriptorLba = 16;
    private const int RootDirectoryRecordOffset = 156;
    private const int MaximumSystemCnfBytes = 64 * 1024;
    private const int MaximumDescriptorBytes = 1024 * 1024;

    private static readonly IsoSectorLayout[] SectorLayouts =
    [
        new(2048, 0, "ISO 2048"),
        new(2352, 16, "CD MODE1/2352"),
        new(2352, 24, "CD MODE2 FORM1/2352"),
        new(2336, 8, "CD MODE2/2336"),
        new(2340, 16, "CD MODE1/2340")
    ];

    public static bool TryScan(string? path, out Ps2DiscStructure structure)
    {
        structure = Ps2DiscStructure.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }

        if (!File.Exists(fullPath))
        {
            return false;
        }

        foreach (string candidate in EnumerateProbeFiles(fullPath))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            if (TryReadSystemCnf(candidate, out string systemCnf, out string sourceLayout)
                && TryBuildStructure(systemCnf, candidate, sourceLayout, out structure))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateProbeFiles(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();

        if (extension == ".cue")
        {
            foreach (string referenced in EnumerateCueReferences(path))
            {
                yield return referenced;
            }

            yield break;
        }

        yield return path;
    }

    private static IEnumerable<string> EnumerateCueReferences(string cuePath)
    {
        string? directory = Path.GetDirectoryName(cuePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            yield break;
        }

        string baseDirectory;
        try
        {
            baseDirectory = Path.GetFullPath(directory);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            yield break;
        }

        foreach (string line in ReadBoundedDescriptorLines(cuePath))
        {
            if (!TryExtractCueFileName(line, out string fileName))
            {
                continue;
            }

            if (TryResolveDescriptorReference(baseDirectory, fileName, out string resolved))
            {
                yield return resolved;
            }
        }
    }

    private static bool TryReadSystemCnf(string path, out string systemCnf, out string sourceLayout)
    {
        systemCnf = string.Empty;
        sourceLayout = string.Empty;

        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                options: FileOptions.SequentialScan);

            foreach (IsoSectorLayout layout in SectorLayouts)
            {
                if (TryReadSystemCnf(stream, layout, out systemCnf))
                {
                    sourceLayout = layout.Name;
                    return true;
                }
            }
        }
        catch (Exception ex) when (IsExpectedReadException(ex) || IsExpectedPathException(ex))
        {
            return false;
        }

        return false;
    }

    private static bool TryReadSystemCnf(FileStream stream, IsoSectorLayout layout, out string systemCnf)
    {
        systemCnf = string.Empty;

        if (!TryReadUserSector(stream, layout, PrimaryVolumeDescriptorLba, out byte[] pvd)
            || !IsPrimaryVolumeDescriptor(pvd))
        {
            return false;
        }

        if (!TryReadDirectoryRecord(pvd, RootDirectoryRecordOffset, out IsoDirectoryRecord root)
            || root.ExtentLba <= 0
            || root.DataLength <= 0)
        {
            return false;
        }

        int directoryLength = (int)Math.Min(root.DataLength, 512 * 1024L);
        if (!TryReadUserData(stream, layout, root.ExtentLba, directoryLength, out byte[] rootDirectory))
        {
            return false;
        }

        if (!TryFindDirectoryFile(rootDirectory, "SYSTEM.CNF", out IsoDirectoryRecord systemCnfRecord)
            || systemCnfRecord.ExtentLba <= 0
            || systemCnfRecord.DataLength <= 0
            || systemCnfRecord.DataLength > MaximumSystemCnfBytes)
        {
            return false;
        }

        if (!TryReadUserData(stream, layout, systemCnfRecord.ExtentLba, (int)systemCnfRecord.DataLength, out byte[] content))
        {
            return false;
        }

        systemCnf = DecodeText(content);
        return !string.IsNullOrWhiteSpace(systemCnf);
    }

    private static bool TryBuildStructure(
        string systemCnf,
        string sourceFile,
        string sourceLayout,
        out Ps2DiscStructure structure)
    {
        structure = Ps2DiscStructure.Empty;

        if (!TryExtractBootLine(systemCnf, out string bootDirective, out string bootPath))
        {
            return false;
        }

        string bootExecutable = NormalizeBootExecutable(bootPath);
        string serial = string.Empty;
        string region = string.Empty;

        if (DiscSerialCatalog.TryExtract(
            bootPath,
            DiscSerialScanProfile.Metadata,
            "PlayStation 2",
            includeOptionalTail: true,
            serialSeparator: "-",
            out DiscSerialCatalogResult bootSerial))
        {
            serial = bootSerial.Serial;
            region = bootSerial.Region;
        }
        else if (DiscSerialCatalog.TryExtract(
            systemCnf,
            DiscSerialScanProfile.Metadata,
            "PlayStation 2",
            includeOptionalTail: true,
            serialSeparator: "-",
            out DiscSerialCatalogResult cnfSerial))
        {
            serial = cnfSerial.Serial;
            region = cnfSerial.Region;
        }

        bool isPlayStation2 = string.Equals(bootDirective, "BOOT2", StringComparison.OrdinalIgnoreCase);
        if (!isPlayStation2)
        {
            return false;
        }

        structure = new Ps2DiscStructure(
            HasSystemCnf: true,
            IsPlayStation2: true,
            BootDirective: bootDirective,
            BootPath: bootPath,
            BootExecutable: bootExecutable,
            Serial: serial,
            Region: region,
            SourceFile: sourceFile,
            SourceLayout: sourceLayout);

        return true;
    }

    private static bool TryExtractBootLine(string systemCnf, out string directive, out string bootPath)
    {
        directive = string.Empty;
        bootPath = string.Empty;

        string? fallbackDirective = null;
        string? fallbackPath = null;

        using var reader = new StringReader(systemCnf);
        while (reader.ReadLine() is { } rawLine)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            int equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            string key = line[..equalsIndex].Trim();
            string value = line[(equalsIndex + 1)..].Trim().Trim('"');
            if (value.Length == 0)
            {
                continue;
            }

            if (string.Equals(key, "BOOT2", StringComparison.OrdinalIgnoreCase))
            {
                directive = "BOOT2";
                bootPath = value;
                return true;
            }

            if (string.Equals(key, "BOOT", StringComparison.OrdinalIgnoreCase))
            {
                fallbackDirective = "BOOT";
                fallbackPath = value;
            }
        }

        if (!string.IsNullOrWhiteSpace(fallbackDirective) && !string.IsNullOrWhiteSpace(fallbackPath))
        {
            directive = fallbackDirective;
            bootPath = fallbackPath;
            return true;
        }

        return false;
    }

    private static string NormalizeBootExecutable(string bootPath)
    {
        if (string.IsNullOrWhiteSpace(bootPath))
        {
            return string.Empty;
        }

        string value = bootPath.Trim().Trim('"');
        int semicolon = value.IndexOf(';');
        if (semicolon >= 0)
        {
            value = value[..semicolon];
        }

        value = value.Replace('/', '\\');
        int colon = value.LastIndexOf(':');
        if (colon >= 0 && colon + 1 < value.Length)
        {
            value = value[(colon + 1)..];
        }

        int slash = value.LastIndexOf('\\');
        if (slash >= 0 && slash + 1 < value.Length)
        {
            value = value[(slash + 1)..];
        }

        return value.Trim('\\').Trim();
    }

    private static bool TryReadUserSector(FileStream stream, IsoSectorLayout layout, int lba, out byte[] sector)
    {
        sector = new byte[UserDataSectorSize];
        long physicalOffset = ((long)lba * layout.PhysicalSectorSize) + layout.DataOffset;

        if (physicalOffset < 0 || physicalOffset + UserDataSectorSize > stream.Length)
        {
            return false;
        }

        stream.Position = physicalOffset;
        return ReadExactly(stream, sector, UserDataSectorSize);
    }

    private static bool TryReadUserData(
        FileStream stream,
        IsoSectorLayout layout,
        int startLba,
        int byteCount,
        out byte[] data)
    {
        data = [];

        if (startLba < 0 || byteCount <= 0)
        {
            return false;
        }

        data = new byte[byteCount];
        int copied = 0;
        int currentLba = startLba;

        while (copied < byteCount)
        {
            if (!TryReadUserSector(stream, layout, currentLba, out byte[] sector))
            {
                data = [];
                return false;
            }

            int bytesToCopy = Math.Min(UserDataSectorSize, byteCount - copied);
            Buffer.BlockCopy(sector, 0, data, copied, bytesToCopy);
            copied += bytesToCopy;
            currentLba++;
        }

        return true;
    }

    private static bool ReadExactly(FileStream stream, byte[] buffer, int count)
    {
        int offset = 0;
        while (offset < count)
        {
            int read = stream.Read(buffer, offset, count - offset);
            if (read <= 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static bool IsPrimaryVolumeDescriptor(ReadOnlySpan<byte> sector) =>
        sector.Length >= 7
        && sector[0] == 1
        && sector[1] == (byte)'C'
        && sector[2] == (byte)'D'
        && sector[3] == (byte)'0'
        && sector[4] == (byte)'0'
        && sector[5] == (byte)'1'
        && sector[6] == 1;

    private static bool TryFindDirectoryFile(ReadOnlySpan<byte> directory, string fileName, out IsoDirectoryRecord record)
    {
        record = default;
        int offset = 0;

        while (offset < directory.Length)
        {
            int recordLength = directory[offset];
            if (recordLength == 0)
            {
                offset = AlignToNextSector(offset);
                continue;
            }

            if (offset + recordLength > directory.Length)
            {
                return false;
            }

            if (TryReadDirectoryRecord(directory, offset, out IsoDirectoryRecord candidate)
                && !candidate.IsDirectory
                && IsMatchingIsoFileName(candidate.FileName, fileName))
            {
                record = candidate;
                return true;
            }

            offset += recordLength;
        }

        return false;
    }

    private static bool TryReadDirectoryRecord(ReadOnlySpan<byte> buffer, int offset, out IsoDirectoryRecord record)
    {
        record = default;

        if (offset < 0 || offset >= buffer.Length)
        {
            return false;
        }

        int length = buffer[offset];
        if (length < 34 || offset + length > buffer.Length)
        {
            return false;
        }

        int nameLength = buffer[offset + 32];
        if (33 + nameLength > length)
        {
            return false;
        }

        int extentLba = ReadInt32LittleEndian(buffer[(offset + 2)..]);
        uint dataLength = ReadUInt32LittleEndian(buffer[(offset + 10)..]);
        byte flags = buffer[offset + 25];
        string fileName = DecodeFileName(buffer.Slice(offset + 33, nameLength));

        record = new IsoDirectoryRecord(
            extentLba,
            dataLength,
            (flags & 0x02) != 0,
            fileName);

        return true;
    }

    private static int AlignToNextSector(int offset)
    {
        int next = ((offset / UserDataSectorSize) + 1) * UserDataSectorSize;
        return next <= offset ? offset + 1 : next;
    }

    private static bool IsMatchingIsoFileName(string candidate, string expected)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        string normalized = candidate.Trim();
        int versionSeparator = normalized.IndexOf(';');
        if (versionSeparator >= 0)
        {
            normalized = normalized[..versionSeparator];
        }

        return string.Equals(normalized, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadInt32LittleEndian(ReadOnlySpan<byte> value)
    {
        if (value.Length < 4)
        {
            return 0;
        }

        return value[0]
            | (value[1] << 8)
            | (value[2] << 16)
            | (value[3] << 24);
    }

    private static uint ReadUInt32LittleEndian(ReadOnlySpan<byte> value)
    {
        if (value.Length < 4)
        {
            return 0;
        }

        return (uint)(value[0]
            | (value[1] << 8)
            | (value[2] << 16)
            | (value[3] << 24));
    }

    private static string DecodeFileName(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 1 && bytes[0] == 0)
        {
            return ".";
        }

        if (bytes.Length == 1 && bytes[0] == 1)
        {
            return "..";
        }

        return Encoding.ASCII.GetString(bytes).Trim();
    }

    private static string DecodeText(byte[] content)
    {
        int length = content.Length;
        while (length > 0 && content[length - 1] == 0)
        {
            length--;
        }

        return Encoding.UTF8.GetString(content, 0, length);
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

    private static IReadOnlyList<string> ReadBoundedDescriptorLines(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaximumDescriptorBytes)
            {
                return [];
            }

            var lines = new List<string>();

            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 16 * 1024,
                options: FileOptions.SequentialScan);

            using StreamReader reader = new(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 16 * 1024,
                leaveOpen: false);

            while (reader.ReadLine() is { } line)
            {
                lines.Add(line);
            }

            return lines;
        }
        catch (Exception ex) when (IsExpectedReadException(ex) || IsExpectedPathException(ex))
        {
            return [];
        }
    }

    private static bool TryResolveDescriptorReference(string baseDirectory, string relativePath, out string resolved)
    {
        resolved = string.Empty;

        try
        {
            string candidate = Path.GetFullPath(Path.Combine(baseDirectory, relativePath));
            if (!IsUnderDirectory(baseDirectory, candidate))
            {
                return false;
            }

            resolved = candidate;
            return true;
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }
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

    private static int IndexOfWhiteSpace(ReadOnlySpan<char> span)
    {
        for (int index = 0; index < span.Length; index++)
        {
            if (char.IsWhiteSpace(span[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or IOException
        or NotSupportedException
        or PathTooLongException
        or UnauthorizedAccessException
        or System.Security.SecurityException;

    private static bool IsExpectedReadException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or InvalidDataException
        or OverflowException
        or ArgumentException
        or NotSupportedException
        or PathTooLongException;

    private readonly record struct IsoSectorLayout(int PhysicalSectorSize, int DataOffset, string Name);

    private readonly record struct IsoDirectoryRecord(
        int ExtentLba,
        uint DataLength,
        bool IsDirectory,
        string FileName);
}
