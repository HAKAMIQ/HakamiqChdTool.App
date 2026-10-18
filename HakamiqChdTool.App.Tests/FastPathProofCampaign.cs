using HakamiqChdTool.App.Core.Verification;
using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Tests;

// Corpus proof for the Raw-SHA1 + logical-size fast path. For every source ISO:
//   source SHA-1 == CHD raw data SHA-1, source size == CHD logical size, the CHD carries DVD
//   metadata, and a Redump lookup by (SHA-1, size) returns exactly one entry of a candidate
//   system that the fast-path assessment accepts. Nothing is enabled by this run.
//
// Safety: corpus files are opened read-only and never written; CHDs are created in the
// output folder and deleted after each file; the Redump database is copied first.
internal static partial class Program
{
    private static int RunFastPathProofCampaign(string[] args, AppReflection app, string outputRoot)
    {
        string corpus = Path.GetFullPath(ReadRequiredArgument(args, "--corpus"));
        if (!Directory.Exists(corpus))
        {
            throw new DirectoryNotFoundException("Corpus folder was not found: " + corpus);
        }

        if (IsSameOrChildPath(outputRoot, corpus))
        {
            throw new ArgumentException("The output folder must not be inside the corpus folder.");
        }

        int maxFiles = ReadBoundedInt(args, "--max-files", 500, 1, 100_000);

        string workRoot = Path.Combine(outputRoot, "fastpath-proof");
        string tempRoot = Path.Combine(workRoot, "tmp");
        Directory.CreateDirectory(tempRoot);

        // Either Redump DAT files (a fresh database is built in the output folder) or a copy
        // of an existing database. The original database is never opened.
        string? datList = ReadOptionalArgument(args, "--dat");
        string sourceDatabase;
        RedumpSqliteManager database;

        if (!string.IsNullOrWhiteSpace(datList))
        {
            string[] dats = datList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Path.GetFullPath)
                .ToArray();
            sourceDatabase = string.Join(", ", dats);
            database = (RedumpSqliteManager)app.CreateRedumpDatabase(Path.Combine(workRoot, "redump-from-dat.db"));
            AssertTrue(GetBool(app.CleanRebuildRedumpDatabase(database, dats), "Success"), "DAT import failed: " + sourceDatabase);
        }
        else
        {
            sourceDatabase = Path.GetFullPath(
                ReadOptionalArgument(args, "--redump-db") ?? RedumpSqliteManager.GetDefaultDatabasePath());
            database = (RedumpSqliteManager)app.CreateRedumpDatabase(CopyDatabase(sourceDatabase, workRoot));
        }
        if (!database.HasAnyRows())
        {
            throw new InvalidOperationException("The Redump database copy has no rows: " + sourceDatabase);
        }

        string chdmanPath = app.GetRuntimeChdmanPath(app.CreateRuntimeToolService());
        var chdInfo = new ChdInfoService();

        List<string> sources = Directory
            .EnumerateFiles(corpus, "*.iso", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Take(maxFiles)
            .ToList();

        var results = new List<FastPathProofRow>();

        foreach (string source in sources)
        {
            FastPathProofRow row = ProveOneAsync(source, chdmanPath, chdInfo, database, tempRoot).GetAwaiter().GetResult();
            results.Add(row);
            Console.WriteLine($"[{(row.Passed ? "PASS" : "FAIL")}] {row.System,-22} {Path.GetFileName(source)}{(row.Passed ? "" : " -> " + row.Failure)}");
        }

        var perSystem = results
            .GroupBy(static row => row.System)
            .Select(static group => new
            {
                system = group.Key,
                files = group.Count(),
                passed = group.Count(static row => row.Passed),
                verdict = group.All(static row => row.Passed) ? "PROOF PASSED" : "PROOF FAILED"
            })
            .OrderBy(static item => item.system, StringComparer.Ordinal)
            .ToList();

        WriteJsonAtomically(
            Path.Combine(workRoot, "fastpath-proof-report.json"),
            new
            {
                format = "HakamiqFastPathProof.v1",
                completedAtUtc = DateTimeOffset.UtcNow,
                corpus,
                redumpDatabase = sourceDatabase,
                chdmanPath,
                note = "Nothing is enabled by this report. A proven system must be added to FastPathProofRegistry.Production in a reviewed change.",
                perSystem,
                files = results
            });

        foreach (var summary in perSystem)
        {
            Console.WriteLine($"[{summary.verdict}] {summary.system}: {summary.passed}/{summary.files}");
        }

        Directory.Delete(tempRoot, recursive: true);
        return results.Count > 0 && results.All(static row => row.Passed) ? 0 : 1;
    }

    private static async Task<FastPathProofRow> ProveOneAsync(
        string source,
        string chdmanPath,
        ChdInfoService chdInfo,
        RedumpSqliteManager database,
        string tempRoot)
    {
        (long size, string sha1, string md5, string crc) = await HashReadOnlyAsync(source).ConfigureAwait(false);

        var hits = database.FindRomsBySha1AndSize(sha1, size);
        string system = hits.Count == 1 ? hits[0].SystemName : hits.Count == 0 ? "(no Redump hit)" : "(ambiguous)";

        string chdPath = Path.Combine(tempRoot, Guid.NewGuid().ToString("N") + ".chd");
        try
        {
            ChdmanCliRunner.Result create = await ChdmanCliRunner.ExecuteAsync(
                    chdmanPath,
                    ["createdvd", "-i", source, "-o", chdPath, "-f"],
                    parseProgressPercent: true,
                    progress: null,
                    onProcessStarted: null,
                    cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);

            if (create.ExitCode != 0)
            {
                return FastPathProofRow.Fail(source, system, size, sha1, "createdvd failed: " + create.StandardError.Trim());
            }

            ChdInfoResult info = await chdInfo.ReadInfoAsync(chdmanPath, chdPath).ConfigureAwait(false);

            string? failure =
                !info.IsSuccess ? "chdman info failed"
                : info.MetadataStructure != DiscStructure.SingleImage2048 ? "CHD has no DVD metadata"
                : !string.Equals(info.DataSha1, sha1, StringComparison.OrdinalIgnoreCase) ? $"raw SHA-1 {info.DataSha1} != source {sha1}"
                : info.LogicalBytes != size ? $"logical size {info.LogicalBytes} != source {size}"
                : hits.Count != 1 ? $"Redump lookup returned {hits.Count} entries"
                : null;

            if (failure is null)
            {
                (string systemName, string gameName, RedumpReferenceRom rom) = hits[0];
                DiscSystem discSystem = DiscPolicy.ResolveSystem(systemName);
                DiscVerificationPolicy policy = DiscPolicy.Resolve(
                    DiscStructure.SingleImage2048,
                    DiscContainer.Chd,
                    discSystem,
                    new FastPathProofRegistry([discSystem]));

                failure =
                    policy.FastPath != RedumpFastPath.RawSha1AndLogicalSize ? $"{systemName} is not a fast-path candidate"
                    : DiscPolicy.AssessFastPath(policy, info.DataSha1, info.LogicalBytes, [rom]) != RedumpVerification.RawSha1AndSize ? "fast-path assessment rejected the hit"
                    : !string.Equals(rom.Md5, md5, StringComparison.OrdinalIgnoreCase) || !string.Equals(rom.Crc32, crc, StringComparison.OrdinalIgnoreCase)
                        ? "Redump MD5/CRC32 disagree with the source" : null;

                if (failure is null)
                {
                    return new FastPathProofRow(source, systemName, gameName, size, sha1, info.DataSha1, info.LogicalBytes, true, string.Empty);
                }
            }

            return FastPathProofRow.Fail(source, system, size, sha1, failure, info.DataSha1, info.LogicalBytes);
        }
        finally
        {
            File.Delete(chdPath);
        }
    }

    private static async Task<(long Size, string Sha1, string Md5, string Crc)> HashReadOnlyAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan | FileOptions.Asynchronous);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        uint crc = uint.MaxValue;
        byte[] buffer = new byte[1024 * 1024];
        long total = 0;
        int read;

        while ((read = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            sha1.AppendData(buffer, 0, read);
            md5.AppendData(buffer, 0, read);
            crc = UpdateCrc32(crc, buffer.AsSpan(0, read));
            total += read;
        }

        return (
            total,
            Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant(),
            Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant(),
            (crc ^ uint.MaxValue).ToString("x8", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static readonly uint[] Crc32Table = BuildCrc32Table();

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? (value >> 1) ^ 0xEDB88320u : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }

    private static uint UpdateCrc32(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            crc = Crc32Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static string CopyDatabase(string sourceDatabase, string workRoot)
    {
        if (!File.Exists(sourceDatabase))
        {
            throw new FileNotFoundException("Redump database was not found.", sourceDatabase);
        }

        string copy = Path.Combine(workRoot, "redump-copy.db");
        foreach (string suffix in new[] { "", "-wal", "-shm" })
        {
            if (File.Exists(sourceDatabase + suffix))
            {
                File.Copy(sourceDatabase + suffix, copy + suffix, overwrite: true);
            }
        }

        return copy;
    }

    private static bool IsSameOrChildPath(string candidate, string parent)
    {
        string c = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record FastPathProofRow(
        string Source,
        string System,
        string Game,
        long SourceBytes,
        string SourceSha1,
        string ChdRawSha1,
        long? ChdLogicalBytes,
        bool Passed,
        string Failure)
    {
        public static FastPathProofRow Fail(
            string source,
            string system,
            long size,
            string sha1,
            string failure,
            string chdRawSha1 = "",
            long? logical = null) =>
            new(source, system, string.Empty, size, sha1, chdRawSha1, logical, false, failure);
    }
}
