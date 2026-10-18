using HakamiqChdTool.App.Services.PostProcessing;
using System;
using System.IO;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // A .sbi that already sits next to the output belongs to the user. With skip-existing on it is kept; only with
    // skip-existing off (the output itself is being replaced) is the companion replaced too.
    private static void TestSbiCopyPreservesExistingSbiUnlessReplacing(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "sbi-copy-" + Guid.NewGuid().ToString("N"));
        string input = Path.Combine(root, "in");
        string output = Path.Combine(root, "out");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);

        string cue = Path.Combine(input, "Game.cue");
        File.WriteAllText(cue, "FILE \"Game.bin\" BINARY\r\n");
        File.WriteAllBytes(Path.Combine(input, "Game.sbi"), [1, 1, 1, 1, 1]);
        string chd = Path.Combine(output, "Game.chd");
        File.WriteAllBytes(chd, [0]);
        string existingSbi = Path.Combine(output, "Game.sbi");
        File.WriteAllBytes(existingSbi, [9, 9]);

        var copier = new SbiArtifactCopier();

        var kept = copier.CopyMatchingSbiIfExists(cue, chd, replaceExisting: false);
        AssertEqual(2L, new FileInfo(existingSbi).Length, "A pre-existing .sbi is kept when not replacing.");
        AssertEqual(0, kept.SbiCopiedCount, "Nothing is reported as copied.");
        AssertEqual(0, Directory.GetFiles(output, "*.tmp").Length, "No temporary file is left behind.");

        var replaced = copier.CopyMatchingSbiIfExists(cue, chd, replaceExisting: true);
        AssertEqual(5L, new FileInfo(existingSbi).Length, "The .sbi is replaced when the output is being replaced.");
        AssertEqual(1, replaced.SbiCopiedCount, "The copy is reported.");

        File.Delete(existingSbi);
        var fresh = copier.CopyMatchingSbiIfExists(cue, chd, replaceExisting: false);
        AssertEqual(1, fresh.SbiCopiedCount, "A free destination is still copied when not replacing.");

        Directory.Delete(root, recursive: true);
    }
}
