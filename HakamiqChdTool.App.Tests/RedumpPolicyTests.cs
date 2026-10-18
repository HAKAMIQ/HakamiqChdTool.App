using HakamiqChdTool.App.Core.Verification;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private const string SpecializedPlatformDetail = "LocRedumpV2_SpecializedPlatform";
    private const string MediaSystemMismatchDetail = "LocRedumpV2_MediaSystemMismatch";
    private const string ProofIncompleteDetail = "LocRedumpV2_ProofIncomplete";

    private static void TestRedumpPolicyEnforcement(AppReflection app, string workDirectory)
    {
        TestRedumpPolicyEnforcementAsync(app, workDirectory).GetAwaiter().GetResult();
    }

    private static async Task TestRedumpPolicyEnforcementAsync(AppReflection app, string workDirectory)
    {
        string root = Path.Combine(workDirectory, "redump-policy");
        Directory.CreateDirectory(root);

        byte[] fastIso = PolicyBytes(2048 * 256, 11);
        byte[] weakIso = PolicyBytes(2048 * 128, 12);
        byte[] unknownIso = PolicyBytes(2048 * 128, 13);
        byte[] xboxIso = PolicyBytes(2048 * 128, 14);
        byte[] dvdAsPs1Iso = PolicyBytes(2048 * 128, 15);
        byte[] rawIso = PolicyBytes(2048 * 128, 16);
        byte[] track1 = PolicyBytes(2352 * 300, 17);
        byte[] track2 = PolicyBytes(2352 * 150, 18);

        await File.WriteAllBytesAsync(Path.Combine(root, "fast.iso"), fastIso).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(root, "weak.iso"), weakIso).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(root, "unknown.iso"), unknownIso).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(root, "xbox.iso"), xboxIso).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(root, "dvd-as-ps1.iso"), dvdAsPs1Iso).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(root, "raw.iso"), rawIso).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(root, "Game (Track 1).bin"), track1).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(root, "Game (Track 2).bin"), track2).ConfigureAwait(false);

        await File.WriteAllTextAsync(
                Path.Combine(root, "complete.cue"),
                "FILE \"Game (Track 1).bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n"
                + "FILE \"Game (Track 2).bin\" BINARY\r\n  TRACK 02 AUDIO\r\n    INDEX 01 00:00:00\r\n",
                Encoding.ASCII)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(
                Path.Combine(root, "missing-track.cue"),
                "FILE \"Game (Track 1).bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n",
                Encoding.ASCII)
            .ConfigureAwait(false);

        string weakRom = PolicyRom("weak.iso", weakIso) // correct size + SHA-1, wrong MD5 and CRC32
            .Replace($"md5=\"{Md5Hex(weakIso)}\"", "md5=\"00000000000000000000000000000000\"", StringComparison.Ordinal)
            .Replace($"crc=\"{Crc32Hex(weakIso)}\"", "crc=\"00000000\"", StringComparison.Ordinal);

        string[] dats =
        [
            await WritePolicyDatAsync(root, "psp.dat", "Sony - PlayStation Portable",
                ("Fast Game", [PolicyRom("fast.iso", fastIso)]),
                ("Weak Game", [weakRom])).ConfigureAwait(false),
            await WritePolicyDatAsync(root, "unknown.dat", "Test System",
                ("Unknown Game", [PolicyRom("unknown.iso", unknownIso)])).ConfigureAwait(false),
            await WritePolicyDatAsync(root, "xbox.dat", "Microsoft - Xbox",
                ("Xbox Game", [PolicyRom("xbox.iso", xboxIso)])).ConfigureAwait(false),
            await WritePolicyDatAsync(root, "psx.dat", "Sony - PlayStation",
                ("Mismatched Game", [PolicyRom("dvd-as-ps1.iso", dvdAsPs1Iso)]),
                ("Game", [
                    PolicyRom("Game (Track 1).bin", track1),
                    PolicyRom("Game (Track 2).bin", track2),
                    PolicyRom("Game.cue", Encoding.ASCII.GetBytes("cue"))
                ])).ConfigureAwait(false)
        ];

        object databaseObject = app.CreateRedumpDatabase(Path.Combine(root, "redump.db"));
        AssertTrue(GetBool(app.CleanRebuildRedumpDatabase(databaseObject, dats), "Success"), "Policy DAT import failed.");
        var database = (RedumpSqliteManager)databaseObject;
        string chdmanPath = app.GetRuntimeChdmanPath(app.CreateRuntimeToolService());

        // Fast path: only for a proven system, and then without hashing extracted data.
        string fastChd = await CreatePolicyChdAsync(chdmanPath, Path.Combine(root, "fast.iso"), Path.Combine(root, "fast.chd"), ["createdvd"]).ConfigureAwait(false);
        var fastEvents = new ConcurrentQueue<ProgressEvent>();
        DeepHashAnalysisResult proven = await ScanWithOuterLeaseAsync(
                fastChd, database, chdmanPath, fastEvents, new FastPathProofRegistry([DiscSystem.PlayStationPortable]))
            .ConfigureAwait(false);
        AssertEqual(IntegrityValidationState.Verified, proven.State, "Proven PSP fast path should verify. " + DescribeRedumpResult(proven));
        AssertEqual(RedumpVerification.RawSha1AndSize, proven.RedumpVerification, "Fast path proof level.");
        AssertEqual("LocRedumpV2_StatusVerifiedFastPath", proven.StatusMessageKey, "Fast path status.");
        AssertFalse(fastEvents.Any(static e => e.MessageKey == "LocRedumpV2_StepHash"), "Fast path must not extract and hash.");

        DeepHashAnalysisResult unproven = await ScanWithOuterLeaseAsync(fastChd, database, chdmanPath, new ConcurrentQueue<ProgressEvent>())
            .ConfigureAwait(false);
        AssertEqual(RedumpVerification.Full, unproven.RedumpVerification, "Without a proof the CHD is extracted and fully hashed.");
        AssertEqual("LocDeepHash_StatusVerifiedNormalized", unproven.StatusMessageKey, "Unproven fast path falls back to extraction.");

        // Weak hash: size + SHA-1 matches, but MD5 and CRC32 disagree.
        DeepHashAnalysisResult weak = await ScanPolicyAsync(root, "weak.iso", database, chdmanPath).ConfigureAwait(false);
        AssertEqual(IntegrityValidationState.Failed, weak.State, "A single-hash match is not Redump proof.");
        AssertEqual(ProofIncompleteDetail, weak.DetailTooltipKey, "Weak hash detail.");
        AssertEqual(RedumpVerification.NotVerified, weak.RedumpVerification, "Weak hash proof level.");

        // Unmapped and specialized systems fail closed.
        DeepHashAnalysisResult unknown = await ScanPolicyAsync(root, "unknown.iso", database, chdmanPath).ConfigureAwait(false);
        AssertEqual(IntegrityValidationState.Unsupported, unknown.State, "An unmapped system fails closed.");
        AssertEqual(SpecializedPlatformDetail, unknown.DetailTooltipKey, "Unmapped system detail.");

        DeepHashAnalysisResult xbox = await ScanPolicyAsync(root, "xbox.iso", database, chdmanPath).ConfigureAwait(false);
        AssertEqual(IntegrityValidationState.Unsupported, xbox.State, "Xbox Redump equivalence is not inferred.");
        AssertEqual(SpecializedPlatformDetail, xbox.DetailTooltipKey, "Xbox detail.");
        AssertEqual(RedumpVerification.NotVerified, xbox.RedumpVerification, "Xbox proof level.");

        // A DVD-structured image matching a PlayStation (CD) entry is a contradiction.
        DeepHashAnalysisResult mismatch = await ScanPolicyAsync(root, "dvd-as-ps1.iso", database, chdmanPath).ConfigureAwait(false);
        AssertEqual(IntegrityValidationState.Failed, mismatch.State, "Structure/system contradiction fails.");
        AssertEqual(MediaSystemMismatchDetail, mismatch.DetailTooltipKey, "Contradiction detail.");

        // A CHD without disc metadata is not assumed to be a DVD.
        string rawChd = await CreatePolicyChdAsync(
                chdmanPath, Path.Combine(root, "raw.iso"), Path.Combine(root, "raw.chd"), ["createraw", "-hs", "4096", "-us", "2048"])
            .ConfigureAwait(false);
        DeepHashAnalysisResult untagged = await ScanWithOuterLeaseAsync(rawChd, database, chdmanPath, new ConcurrentQueue<ProgressEvent>())
            .ConfigureAwait(false);
        AssertEqual(IntegrityValidationState.Unsupported, untagged.State, "Untagged CHD fails closed. " + DescribeRedumpResult(untagged));
        AssertEqual(MediaSemantics.Unknown, untagged.MediaSemantics, "Untagged CHD semantics.");

        // Track-structured CD: every track is required.
        DeepHashAnalysisResult complete = await ScanPolicyAsync(root, "complete.cue", database, chdmanPath).ConfigureAwait(false);
        AssertEqual(IntegrityValidationState.Verified, complete.State, "All tracks present -> verified. " + DescribeRedumpResult(complete));
        AssertEqual(RedumpVerification.TrackVerified, complete.RedumpVerification, "CD proof level.");
        AssertEqual(MediaSemantics.TrackStructuredCd, complete.MediaSemantics, "CD semantics.");
        AssertEqual(StorageCompatibility.Native, complete.StorageCompatibility, "CUE/BIN is native for DuckStation.");

        DeepHashAnalysisResult missing = await ScanPolicyAsync(root, "missing-track.cue", database, chdmanPath).ConfigureAwait(false);
        AssertEqual(IntegrityValidationState.Failed, missing.State, "A missing track is not verified.");
        AssertEqual(ProofIncompleteDetail, missing.DetailTooltipKey, "Missing track detail.");
    }

    private static Task<DeepHashAnalysisResult> ScanPolicyAsync(
        string root,
        string fileName,
        RedumpSqliteManager database,
        string chdmanPath) =>
        ScanWithOuterLeaseAsync(Path.Combine(root, fileName), database, chdmanPath, new ConcurrentQueue<ProgressEvent>());

    private static async Task<string> CreatePolicyChdAsync(
        string chdmanPath,
        string inputPath,
        string outputPath,
        IReadOnlyList<string> command)
    {
        List<string> arguments = [command[0], "-i", inputPath, "-o", outputPath, .. command.Skip(1), "-f"];

        if (command[0] == "createraw")
        {
            // Fixture only: the app's chdman runner intentionally does not allow createraw.
            var start = new System.Diagnostics.ProcessStartInfo(chdmanPath)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = System.Diagnostics.Process.Start(start)
                ?? throw new InvalidOperationException("chdman could not be started.");
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            _ = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            AssertEqual(0, process.ExitCode, "chdman createraw failed: " + await stderr.ConfigureAwait(false));
            return outputPath;
        }

        ChdmanCliRunner.Result result = await ChdmanCliRunner.ExecuteAsync(
                chdmanPath,
                arguments,
                parseProgressPercent: true,
                progress: null,
                onProcessStarted: null,
                cancellationToken: CancellationToken.None,
                exclusiveFileAccessPath: inputPath)
            .ConfigureAwait(false);

        AssertEqual(0, result.ExitCode, $"chdman {command[0]} failed: {result.StandardError}");
        return outputPath;
    }

    private static async Task<string> WritePolicyDatAsync(
        string root,
        string fileName,
        string systemName,
        params (string Game, string[] Roms)[] games)
    {
        var xml = new StringBuilder();
        xml.Append($"<datafile><header><name>{systemName} - Datfile</name></header>");

        foreach ((string game, string[] roms) in games)
        {
            xml.Append($"<game name=\"{game}\"><description>{game}</description>");
            foreach (string rom in roms)
            {
                xml.Append(rom);
            }

            xml.Append("</game>");
        }

        xml.Append("</datafile>");

        string path = Path.Combine(root, fileName);
        await File.WriteAllTextAsync(path, xml.ToString().Replace("&", "&amp;", StringComparison.Ordinal), Encoding.UTF8)
            .ConfigureAwait(false);
        return path;
    }

    private static string PolicyRom(string name, byte[] bytes) =>
        $"<rom name=\"{name}\" size=\"{bytes.Length}\" crc=\"{Crc32Hex(bytes)}\" md5=\"{Md5Hex(bytes)}\" sha1=\"{Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant()}\" />";

    private static byte[] PolicyBytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static string Md5Hex(byte[] bytes) =>
        Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();

    private static string Crc32Hex(byte[] bytes)
    {
        uint crc = uint.MaxValue;

        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return (crc ^ uint.MaxValue).ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
    }
}
