using HakamiqChdTool.App.Core.Workflow;
using Serilog;
using System;
using System.IO;
using System.Linq;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static WorkflowSourceCleanupResult RunVerifiedConversionCleanup(string source, string chd, bool verified = true) =>
        new WorkflowSourceCleanupPipeline(new LoggerConfiguration().CreateLogger())
            .Run(new WorkflowSourceCleanupRequest(source, chd, WorkflowSourceCleanupMode.VerifiedConversion, IsVerified: verified, IsEnabled: true));

    private static string WriteFakeChd(string directory, string name = "game.chd")
    {
        Directory.CreateDirectory(directory);
        string chd = Path.Combine(directory, name);
        File.WriteAllBytes(chd, [0x4D, 0x43, 0x6F, 0x6D]);
        return chd;
    }

    private static string[] FileNames(string directory) =>
        [.. Directory.EnumerateFiles(directory).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase)!];

    // "Delete the source after a verified conversion" covers ZSO, CloneCD, Alcohol and ECM sets, and keeps
    // a whole set when part of it (subchannel data) is not stored in the CHD.
    private static void TestSourceCleanupCoversNewFormats(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "cleanup-new-formats");

        // ZSO: the single file.
        string zsoDir = Path.Combine(root, "zso");
        Directory.CreateDirectory(zsoDir);
        string zso = Path.Combine(zsoDir, "game.zso");
        File.WriteAllBytes(zso, BuildStoredZso(CreateIsoPayload(8, 1)));
        File.WriteAllText(Path.Combine(zsoDir, "notes.txt"), "keep");
        RunVerifiedConversionCleanup(zso, WriteFakeChd(Path.Combine(root, "zso-out")));
        AssertEqual("notes.txt", string.Join(",", FileNames(zsoDir)), "The ZSO is deleted; unrelated files stay.");

        AssertTrue(File.Exists(zso) is false, "The ZSO is gone after a verified conversion.");

        // CloneCD with a .sub and no copy of it next to the output: the whole set is kept.
        string ccdKeepDir = Path.Combine(root, "ccd-keep");
        string ccdKeep = WriteCcdSet(ccdKeepDir, "game", TwoTrackCcd, 16);
        WorkflowSourceCleanupResult kept = RunVerifiedConversionCleanup(ccdKeep, WriteFakeChd(Path.Combine(root, "ccd-keep-out")));
        AssertEqual(0, kept.DeletedFiles, "A CloneCD set whose .sub is not preserved is kept whole.");
        AssertEqual("game.ccd,game.img,game.sub", string.Join(",", FileNames(ccdKeepDir)), "All CloneCD files are still there.");

        // CloneCD with an identical .sub next to the output: .img, .sub and .ccd are deleted.
        string ccdCopyDir = Path.Combine(root, "ccd-copy");
        string ccdCopy = WriteCcdSet(ccdCopyDir, "game", TwoTrackCcd, 16);
        string ccdCopyOut = Path.Combine(root, "ccd-copy-out");
        string ccdCopyChd = WriteFakeChd(ccdCopyOut);
        File.Copy(Path.Combine(ccdCopyDir, "game.sub"), Path.Combine(ccdCopyOut, "game.sub"));
        WorkflowSourceCleanupResult ccdDeleted = RunVerifiedConversionCleanup(ccdCopy, ccdCopyChd);
        AssertEqual(3, ccdDeleted.DeletedFiles, "The CloneCD .img, .sub and .ccd are deleted.");
        AssertEqual(0, FileNames(ccdCopyDir).Length, "Nothing of the CloneCD set is left.");
        AssertTrue(File.Exists(Path.Combine(ccdCopyOut, "game.sub")), "The .sub copy next to the CHD is kept.");

        // CloneCD without a .sub: .img and .ccd.
        string ccdNoSubDir = Path.Combine(root, "ccd-nosub");
        string ccdNoSub = WriteCcdSet(ccdNoSubDir, "game", TwoTrackCcd, 16);
        File.Delete(Path.Combine(ccdNoSubDir, "game.sub"));
        AssertEqual(2, RunVerifiedConversionCleanup(ccdNoSub, WriteFakeChd(Path.Combine(root, "ccd-nosub-out"))).DeletedFiles, "A CloneCD set without .sub is deleted.");

        // Alcohol raw image: .mdf and .mds.
        string mdsDir = Path.Combine(root, "mds");
        string mds = WriteMdsSet(mdsDir, "game", TwoTrackMds());
        AssertEqual(2, RunVerifiedConversionCleanup(mds, WriteFakeChd(Path.Combine(root, "mds-out"))).DeletedFiles, "The .mdf and .mds are deleted.");
        AssertEqual(0, FileNames(mdsDir).Length, "Nothing of the Alcohol set is left.");

        // Alcohol image with subchannel data: kept whole.
        string mdsSubDir = Path.Combine(root, "mds-sub");
        string mdsSub = WriteMdsSet(mdsSubDir, "game", TwoTrackMds(sectorSize: 2448, subchannels: 8));
        AssertEqual(0, RunVerifiedConversionCleanup(mdsSub, WriteFakeChd(Path.Combine(root, "mds-sub-out"))).DeletedFiles, "An Alcohol set with subchannel data is kept.");
        AssertEqual(2, FileNames(mdsSubDir).Length, "Both Alcohol files are still there.");

        // ECM without a CUE: the single .ecm.
        string loneDir = Path.Combine(root, "ecm-lone");
        Directory.CreateDirectory(loneDir);
        string lone = Path.Combine(loneDir, "disc.bin.ecm");
        File.WriteAllBytes(lone, LoadEcmReferenceFixture());
        AssertEqual(1, RunVerifiedConversionCleanup(lone, WriteFakeChd(Path.Combine(root, "ecm-lone-out"))).DeletedFiles, "A standalone .ecm is deleted.");

        // ECM CUE set: .ecm track, plain track and CUE are deleted; an .sbi without a copy is kept.
        string setDir = Path.Combine(root, "ecm-set");
        WriteEcmCueSet(setDir,
            "FILE \"game (Track 1).bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n" +
            "FILE \"game (Track 2).bin\" BINARY\r\n  TRACK 02 AUDIO\r\n    INDEX 01 00:00:00\r\n",
            ("game (Track 1).bin.ecm", LoadEcmReferenceFixture()), ("game (Track 2).bin", new byte[2352 * 2]), ("game.sbi", [1, 2, 3]));
        WorkflowSourceCleanupResult setDeleted = RunVerifiedConversionCleanup(Path.Combine(setDir, "game (Track 1).bin.ecm"), WriteFakeChd(Path.Combine(root, "ecm-set-out")));
        AssertEqual(3, setDeleted.DeletedFiles, "The .ecm track, the plain track and the CUE are deleted.");
        AssertEqual("game.sbi", string.Join(",", FileNames(setDir)), "The .sbi without a copy next to the output is kept.");

        // Nothing is deleted when the conversion was not verified.
        string unverifiedDir = Path.Combine(root, "unverified");
        string unverified = WriteMdsSet(unverifiedDir, "game", TwoTrackMds());
        AssertEqual(0, RunVerifiedConversionCleanup(unverified, WriteFakeChd(Path.Combine(root, "unverified-out")), verified: false).DeletedFiles, "An unverified conversion deletes nothing.");
    }
}
