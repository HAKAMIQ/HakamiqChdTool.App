using HakamiqChdTool.App.Core.Workflow;
using System.IO;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // A CUE reference that passes through a junction inside the cue folder can resolve outside it.
    // The string-prefix containment check cannot see that, so the reference must be rejected.
    private static void TestCueReferenceThroughJunctionIsRejected(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "cue-link-" + System.Guid.NewGuid().ToString("N"));
        string cueDir = Path.Combine(root, "disc");
        string outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(cueDir);
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "track.bin"), [1, 2, 3, 4]);
        File.WriteAllBytes(Path.Combine(cueDir, "plain.bin"), [1, 2, 3, 4]);

        string plainCue = Path.Combine(cueDir, "plain.cue");
        File.WriteAllText(plainCue, "FILE \"plain.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n");
        AssertTrue(WorkflowSafePathValidator.AllCueFileReferencesAreUsable(plainCue), "A plain in-folder reference is usable.");

        CreateJunction(Path.Combine(cueDir, "link"), outside);
        string linkedCue = Path.Combine(cueDir, "linked.cue");
        File.WriteAllText(linkedCue, "FILE \"link\\track.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n");
        AssertFalse(WorkflowSafePathValidator.AllCueFileReferencesAreUsable(linkedCue), "A reference through a junction is refused.");

        Directory.Delete(Path.Combine(cueDir, "link")); // removes the junction only, not its target
        AssertTrue(File.Exists(Path.Combine(outside, "track.bin")), "The junction target is untouched.");
        Directory.Delete(root, recursive: true);
    }

    // A cue sheet above the size bound is refused instead of being read into memory.
    private static void TestOversizedCueSheetIsRefused(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "cue-big-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "a.bin"), [1, 2, 3, 4]);
        string cue = Path.Combine(root, "big.cue");
        using (FileStream stream = File.Create(cue))
        {
            byte[] header = System.Text.Encoding.UTF8.GetBytes("FILE \"a.bin\" BINARY\r\n");
            stream.Write(header);
            stream.SetLength(2L * 1024 * 1024);
        }

        AssertFalse(WorkflowSafePathValidator.AllCueFileReferencesAreUsable(cue), "A cue above 1 MiB is refused.");
        Directory.Delete(root, recursive: true);
    }
}
