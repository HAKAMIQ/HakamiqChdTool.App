using HakamiqChdTool.App.Core.Verification;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static void TestRedumpIdenticalHashesAcrossDiscs(AppReflection app, string workDirectory)
    {
        TestRedumpIdenticalHashesAcrossDiscsAsync(app, workDirectory).GetAwaiter().GetResult();
    }

    private static async Task TestRedumpIdenticalHashesAcrossDiscsAsync(AppReflection app, string workDirectory)
    {
        string root = Path.Combine(workDirectory, "redump-ambiguity");
        Directory.CreateDirectory(root);

        byte[] earlyData = PolicyBytes(2352 * 200, 31);
        byte[] ownData = PolicyBytes(2352 * 220, 32);
        byte[] sharedAudio = PolicyBytes(2352 * 120, 33);
        byte[] foreignData = PolicyBytes(2352 * 210, 34);

        await File.WriteAllBytesAsync(Path.Combine(root, "Own (Track 1).bin"), ownData).ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(root, "Own (Track 2).bin"), sharedAudio).ConfigureAwait(false);
        await File.WriteAllTextAsync(
                Path.Combine(root, "own.cue"),
                "FILE \"Own (Track 1).bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n"
                + "FILE \"Own (Track 2).bin\" BINARY\r\n  TRACK 02 AUDIO\r\n    INDEX 01 00:00:00\r\n",
                Encoding.ASCII)
            .ConfigureAwait(false);

        // "Early Game" is imported first and owns the same audio track, so a first-row lookup
        // would attribute the shared track to it and report a conflict for the real disc.
        string dat = await WritePolicyDatAsync(root, "psx.dat", "Sony - PlayStation",
            ("Early Game", [
                PolicyRom("Early (Track 1).bin", earlyData),
                PolicyRom("Early (Track 2).bin", sharedAudio),
                PolicyRom("Early.cue", Encoding.ASCII.GetBytes("early"))
            ]),
            ("Foreign Game", [
                PolicyRom("Foreign (Track 1).bin", foreignData),
                PolicyRom("Foreign (Track 2).bin", sharedAudio),
                PolicyRom("Foreign.cue", Encoding.ASCII.GetBytes("foreign"))
            ]),
            ("Own Game", [
                PolicyRom("Own (Track 1).bin", ownData),
                PolicyRom("Own (Track 2).bin", sharedAudio),
                PolicyRom("Own.cue", Encoding.ASCII.GetBytes("own"))
            ])).ConfigureAwait(false);

        object databaseObject = app.CreateRedumpDatabase(Path.Combine(root, "redump.db"));
        AssertTrue(GetBool(app.CleanRebuildRedumpDatabase(databaseObject, [dat]), "Success"), "Ambiguity DAT import failed.");
        var database = (RedumpSqliteManager)databaseObject;
        string chdmanPath = app.GetRuntimeChdmanPath(app.CreateRuntimeToolService());

        DeepHashAnalysisResult own = await ScanPolicyAsync(root, "own.cue", database, chdmanPath).ConfigureAwait(false);
        AssertEqual(IntegrityValidationState.Verified, own.State, "A track shared with other discs must not break the real disc. " + DescribeRedumpResult(own));
        AssertEqual(RedumpVerification.TrackVerified, own.RedumpVerification, "Shared track proof level.");
        AssertEqual("Own Game", own.MatchedGameName, "The disc owning every track is the match.");

        // The shared track alone cannot identify a disc, but it still hashes into the candidate set.
        var query = new[] { new RedumpHashQuery(Md5Hex(sharedAudio), Sha1Hex(sharedAudio), Crc32Hex(sharedAudio), sharedAudio.Length) };
        AssertEqual(3, database.FindCommonDiscs(query).Count, "Every owning disc is a candidate for a shared hash.");

        var both = new[]
        {
            query[0],
            new RedumpHashQuery(Md5Hex(ownData), Sha1Hex(ownData), Crc32Hex(ownData), ownData.Length)
        };
        IReadOnlyList<RedumpDiscCandidate> common = database.FindCommonDiscs(both);
        AssertEqual(1, common.Count, "Only one disc owns both hashes.");
        AssertEqual("Own Game", common[0].GameName, "Intersection picks the disc owning both.");

        var contradictory = new[]
        {
            new RedumpHashQuery(Md5Hex(earlyData), Sha1Hex(earlyData), Crc32Hex(earlyData), earlyData.Length),
            new RedumpHashQuery(Md5Hex(foreignData), Sha1Hex(foreignData), Crc32Hex(foreignData), foreignData.Length)
        };
        AssertEqual(0, database.FindCommonDiscs(contradictory).Count, "Tracks of different discs share no common disc.");

        // A size that disagrees with the hashed bytes never matches.
        var wrongSize = new[] { new RedumpHashQuery(Md5Hex(ownData), Sha1Hex(ownData), Crc32Hex(ownData), ownData.Length + 1) };
        AssertEqual(0, database.FindCommonDiscs(wrongSize).Count, "A stale size must not match.");
    }

    private static string Sha1Hex(byte[] bytes) =>
        System.Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes)).ToLowerInvariant();
}
