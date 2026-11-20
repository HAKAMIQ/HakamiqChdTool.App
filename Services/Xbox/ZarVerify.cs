using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SharpCompress.Compressors.ZStandard;

namespace HakamiqChdTool.App.Services.Xbox;

/// <summary>
/// Checks a ZArchive (.zar) against the folder it was made from without using zarchive.exe: it reads the footer, the
/// block offset table and the file tree, decompresses every block in order and compares each file's SHA-256 with the
/// source file. zarchive.exe 0.1.2 is not used for this because its extraction stops early on large archives
/// (seen with a 7 GB Xbox 360 game) while the archive itself is correct.
/// Format: big-endian; 64 KiB blocks, each zstd-compressed or stored as is when compression does not help.
/// </summary>
internal static class ZarVerifier
{
    private const int BlockSize = 64 * 1024;
    private const int BlocksPerRecord = 16;
    private const int RecordSize = 8 + 2 * BlocksPerRecord;
    private const int EntrySize = 16;
    private const int FooterSize = 6 * 16 + 32 + 8 + 4 + 4;
    private const uint Magic = 0x169f52d6;
    private const uint Version1 = 0x61bf3a01;
    private const long MaxTableBytes = 256L * 1024 * 1024;

    private sealed record ZarFile(string RelativePath, ulong Offset, ulong Size);

    /// <summary>True when the archive holds exactly the files of <paramref name="folder"/> with the same content.</summary>
    public static bool MatchesFolder(string archivePath, string folder, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        try
        {
            using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
            if (stream.Length < FooterSize)
            {
                return false;
            }

            byte[] footer = ReadAt(stream, stream.Length - FooterSize, FooterSize);
            if (BinaryPrimitives.ReadUInt32BigEndian(footer.AsSpan(FooterSize - 4)) != Magic
                || BinaryPrimitives.ReadUInt32BigEndian(footer.AsSpan(FooterSize - 8)) != Version1
                || BinaryPrimitives.ReadUInt64BigEndian(footer.AsSpan(FooterSize - 16)) != (ulong)stream.Length)
            {
                return false;
            }

            (ulong Offset, ulong Size) Section(int index) => (
                BinaryPrimitives.ReadUInt64BigEndian(footer.AsSpan(index * 16)),
                BinaryPrimitives.ReadUInt64BigEndian(footer.AsSpan(index * 16 + 8)));

            (ulong dataOffset, ulong dataSize) = Section(0);
            (ulong recordsOffset, ulong recordsSize) = Section(1);
            (ulong namesOffset, ulong namesSize) = Section(2);
            (ulong treeOffset, ulong treeSize) = Section(3);
            foreach ((ulong offset, ulong size) in new[] { (dataOffset, dataSize), (recordsOffset, recordsSize), (namesOffset, namesSize), (treeOffset, treeSize) })
            {
                if (offset + size > (ulong)stream.Length || offset + size < offset)
                {
                    return false;
                }
            }

            if (recordsSize % RecordSize != 0 || treeSize % EntrySize != 0 || treeSize == 0
                || recordsSize > MaxTableBytes || namesSize > MaxTableBytes || treeSize > MaxTableBytes)
            {
                return false;
            }

            byte[] records = ReadAt(stream, (long)recordsOffset, (int)recordsSize);
            byte[] names = ReadAt(stream, (long)namesOffset, (int)namesSize);
            byte[] tree = ReadAt(stream, (long)treeOffset, (int)treeSize);

            List<ZarFile>? files = ReadFileTree(tree, names);
            if (files is null)
            {
                return false;
            }

            Dictionary<string, string> sources = Directory
                .EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetRelativePath(folder, path).Replace('\\', '/'), path => path, StringComparer.OrdinalIgnoreCase);
            if (sources.Count != files.Count || files.Any(file => !sources.TryGetValue(file.RelativePath, out string? source) || (ulong)new FileInfo(source).Length != file.Size))
            {
                return false;
            }

            Dictionary<string, byte[]> actual = HashArchiveFiles(stream, records, dataOffset, dataSize, files, cancellationToken);
            foreach (ZarFile file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using FileStream source = new(sources[file.RelativePath], FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
                if (!SHA256.HashData(source).AsSpan().SequenceEqual(actual[file.RelativePath]))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or ZstdException or OverflowException)
        {
            return false;
        }
    }

    private static List<ZarFile>? ReadFileTree(byte[] tree, byte[] names)
    {
        int count = tree.Length / EntrySize;
        var files = new List<ZarFile>();
        var queue = new Queue<(int Index, string Path)>();
        queue.Enqueue((0, string.Empty));
        int visited = 0;

        while (queue.Count > 0)
        {
            (int index, string path) = queue.Dequeue();
            if (index < 0 || index >= count || ++visited > count)
            {
                return null;
            }

            ReadOnlySpan<byte> entry = tree.AsSpan(index * EntrySize, EntrySize);
            uint nameAndFlag = BinaryPrimitives.ReadUInt32BigEndian(entry);
            uint a = BinaryPrimitives.ReadUInt32BigEndian(entry[4..]);
            uint b = BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            uint c = BinaryPrimitives.ReadUInt32BigEndian(entry[12..]);
            bool isFile = (nameAndFlag & 0x80000000) != 0;

            if (isFile)
            {
                ulong offset = a | ((ulong)(c & 0xFFFF) << 32);
                ulong size = b | ((ulong)(c & 0xFFFF0000) << 16);
                files.Add(new ZarFile(path, offset, size));
                continue;
            }

            // A directory: a = first child index, b = number of children.
            if (a > count || b > count - a)
            {
                return null;
            }

            for (int child = (int)a; child < a + b; child++)
            {
                string? name = ReadName(names, BinaryPrimitives.ReadUInt32BigEndian(tree.AsSpan(child * EntrySize)) & 0x7FFFFFFF);
                if (string.IsNullOrEmpty(name) || name is "." or ".." || name.Contains('/') || name.Contains('\\'))
                {
                    return null;
                }

                queue.Enqueue((child, path.Length == 0 ? name : path + "/" + name));
            }
        }

        return files;
    }

    private static string? ReadName(byte[] names, uint offset)
    {
        if (offset >= names.Length)
        {
            return null;
        }

        int length = names[offset] & 0x7F;
        int start = (int)offset + 1;
        if ((names[offset] & 0x80) != 0)
        {
            if (offset + 1 >= names.Length)
            {
                return null;
            }

            length |= names[offset + 1] << 7;
            start++;
        }

        return start + length > names.Length ? null : Encoding.UTF8.GetString(names, start, length);
    }

    /// <summary>Decompresses the blocks in order and hashes each file's byte range.</summary>
    private static Dictionary<string, byte[]> HashArchiveFiles(
        FileStream stream,
        byte[] records,
        ulong dataOffset,
        ulong dataSize,
        List<ZarFile> files,
        CancellationToken cancellationToken)
    {
        List<ZarFile> ordered = [.. files.OrderBy(file => file.Offset)];
        var hashes = new Dictionary<string, IncrementalHash>(StringComparer.OrdinalIgnoreCase);
        foreach (ZarFile file in ordered)
        {
            hashes[file.RelativePath] = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        }

        try
        {
            ulong end = ordered.Count == 0 ? 0 : ordered.Max(file => file.Offset + file.Size);
            ulong blockCount = (end + BlockSize - 1) / BlockSize;
            if (blockCount > (ulong)(records.Length / RecordSize) * BlocksPerRecord)
            {
                throw new InvalidDataException("The archive has fewer blocks than its files need.");
            }

            byte[] compressed = new byte[BlockSize];
            byte[] block = new byte[BlockSize];
            using var decompressor = new Decompressor();
            int first = 0;

            for (ulong blockIndex = 0; blockIndex < blockCount; blockIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int recordStart = (int)(blockIndex / BlocksPerRecord) * RecordSize;
                int slot = (int)(blockIndex % BlocksPerRecord);
                ulong offset = BinaryPrimitives.ReadUInt64BigEndian(records.AsSpan(recordStart));
                for (int i = 0; i < slot; i++)
                {
                    offset += (ulong)BinaryPrimitives.ReadUInt16BigEndian(records.AsSpan(recordStart + 8 + i * 2)) + 1;
                }

                int size = BinaryPrimitives.ReadUInt16BigEndian(records.AsSpan(recordStart + 8 + slot * 2)) + 1;
                if (offset + (ulong)size > dataSize)
                {
                    throw new InvalidDataException("A block lies outside the compressed data.");
                }

                stream.Position = (long)(dataOffset + offset);
                stream.ReadExactly(compressed, 0, size);
                if (size == BlockSize)
                {
                    Buffer.BlockCopy(compressed, 0, block, 0, BlockSize);
                }
                else if (decompressor.Unwrap(compressed.AsSpan(0, size), block) != BlockSize)
                {
                    throw new InvalidDataException("A block did not decompress to 64 KiB.");
                }

                ulong blockStart = blockIndex * BlockSize;
                ulong blockEnd = blockStart + BlockSize;
                while (first < ordered.Count && ordered[first].Offset + ordered[first].Size <= blockStart)
                {
                    first++;
                }

                for (int i = first; i < ordered.Count && ordered[i].Offset < blockEnd; i++)
                {
                    ZarFile file = ordered[i];
                    ulong from = Math.Max(file.Offset, blockStart);
                    ulong to = Math.Min(file.Offset + file.Size, blockEnd);
                    if (to > from)
                    {
                        hashes[file.RelativePath].AppendData(block, (int)(from - blockStart), (int)(to - from));
                    }
                }
            }

            return hashes.ToDictionary(pair => pair.Key, pair => pair.Value.GetHashAndReset(), StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            foreach (IncrementalHash hash in hashes.Values)
            {
                hash.Dispose();
            }
        }
    }

    private static byte[] ReadAt(FileStream stream, long offset, int count)
    {
        byte[] buffer = new byte[count];
        stream.Position = offset;
        stream.ReadExactly(buffer);
        return buffer;
    }
}
