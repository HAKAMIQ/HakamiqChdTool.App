using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Services;
using System;
using System.IO;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // The extractcd scratch cue is the only auxiliary file the converter may delete; user files and
    // anything reached through a junction must survive every cleanup path.
    private static void TestAuxiliaryOutputDeleteIsLimitedToRestoreScratchCue(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "aux-cue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        // The path the restore policy really produces.
        RestoreTargetDecision decision = new RestoreTargetPolicy().Resolve(new RestoreTargetRequest(
            new MetadataAwareChdExtractionDecision(
                true, ChdmanExtractionKind.ExtractDvd, ".iso", "", "", "", "msg", "", "", default, ChdRestoreTargetMode.LegacyCdProfileToIso),
            Path.Combine(root, "Game.iso"),
            Path.Combine(root, "final", "Game.iso")));
        string scratch = decision.ExtractCdCueOutputPath!;
        File.WriteAllText(scratch, "scratch");
        ChdConversionServiceSupport.TryDeleteAuxiliaryOutputFile(scratch, "test");
        AssertFalse(File.Exists(scratch), "The restore scratch cue is deleted.");

        string userCue = Path.Combine(root, "Game.cue");
        File.WriteAllText(userCue, "user data");
        ChdConversionServiceSupport.TryDeleteAuxiliaryOutputFile(userCue, "test");
        AssertTrue(File.Exists(userCue), "A user cue with another name is never deleted.");

        string outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(outside);
        string victim = Path.Combine(outside, "Game.legacy-restore.cue");
        File.WriteAllText(victim, "not ours");
        string link = Path.Combine(root, "link");
        CreateJunction(link, outside);
        ChdConversionServiceSupport.TryDeleteAuxiliaryOutputFile(Path.Combine(link, "Game.legacy-restore.cue"), "test");
        AssertTrue(File.Exists(victim), "A scratch-named file reached through a junction is not deleted.");

        Directory.Delete(link);
        Directory.Delete(root, recursive: true);
    }
}
