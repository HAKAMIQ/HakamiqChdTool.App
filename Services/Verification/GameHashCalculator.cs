using HakamiqChdTool.App.Core.Verification;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services.Verification;

public sealed record GameHashCalculationResult(
    string Path,
    long SizeBytes,
    string CRC32,
    string MD5,
    string SHA1,
    string SHA256);

public sealed class GameHashCalculator
{
    private const int BufferSize = 1024 * 1024;

    public async Task<GameHashCalculationResult> CalculateAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path.Trim());
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("LocFileHash_FileNotFound", fullPath);
        }

        using IncrementalHash md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using IncrementalHash sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        using IncrementalHash sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var crc32 = new Crc32();
        byte[] buffer = new byte[BufferSize];
        long total = 0;

        await using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: BufferSize,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int read = await stream
                .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                .ConfigureAwait(false);

            if (read <= 0)
            {
                break;
            }

            total += read;
            crc32.Append(buffer, read);
            md5.AppendData(buffer, 0, read);
            sha1.AppendData(buffer, 0, read);
            sha256.AppendData(buffer, 0, read);
        }

        return new GameHashCalculationResult(
            fullPath,
            total,
            crc32.GetCurrentHash(),
            ToHex(md5.GetHashAndReset()),
            ToHex(sha1.GetHashAndReset()),
            ToHex(sha256.GetHashAndReset()));
    }

    public static GameHashMatch Match(GameHashCalculationResult hash, GameHashSet database)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(database);

        if (database.Entries.Count == 0)
        {
            return new GameHashMatch(
                GameVerificationStatus.NotInDatabase,
                null,
                null,
                "LocVerify_NoDatabaseImported",
                0,
                []);
        }

        foreach (GameHashEntry entry in database.Entries)
        {
            if (entry.SizeBytes.HasValue && entry.SizeBytes.Value != hash.SizeBytes)
            {
                continue;
            }

            if (EqualsHash(entry.SHA256, hash.SHA256))
            {
                return Match(entry, GameHashAlgorithm.SHA256, 1.0);
            }

            if (EqualsHash(entry.SHA1, hash.SHA1))
            {
                return Match(entry, GameHashAlgorithm.SHA1, 0.95, ["LocVerify_Md5Sha1LegacyWarning"]);
            }

            if (EqualsHash(entry.MD5, hash.MD5))
            {
                return Match(entry, GameHashAlgorithm.MD5, 0.8, ["LocVerify_Md5Sha1LegacyWarning"]);
            }

            if (EqualsHash(entry.CRC32, hash.CRC32))
            {
                return Match(entry, GameHashAlgorithm.CRC32, 0.7, ["LocVerify_Md5Sha1LegacyWarning"]);
            }
        }

        return new GameHashMatch(
            GameVerificationStatus.NotInDatabase,
            null,
            null,
            "LocVerify_NotInDatabase",
            0,
            []);
    }

    private static GameHashMatch Match(
        GameHashEntry entry,
        GameHashAlgorithm algorithm,
        double confidence,
        IReadOnlyList<string>? warnings = null) => new(
        GameVerificationStatus.Match,
        entry,
        algorithm,
        "LocVerify_Match",
        confidence,
        warnings ?? []);

    private static bool EqualsHash(string left, string right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(NormalizeHex(left), NormalizeHex(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeHex(string value) =>
        value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);

    private static string ToHex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    private sealed class Crc32
    {
        private static readonly uint[] Table = CreateTable();
        private uint _crc = 0xFFFFFFFFu;

        public void Append(byte[] buffer, int count)
        {
            for (int index = 0; index < count; index++)
            {
                byte value = buffer[index];
                _crc = Table[(_crc ^ value) & 0xFF] ^ (_crc >> 8);
            }
        }

        public string GetCurrentHash()
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, ~_crc);
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static uint[] CreateTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++)
                {
                    value = (value & 1) == 1
                        ? 0xEDB88320u ^ (value >> 1)
                        : value >> 1;
                }

                table[i] = value;
            }

            return table;
        }
    }
}
