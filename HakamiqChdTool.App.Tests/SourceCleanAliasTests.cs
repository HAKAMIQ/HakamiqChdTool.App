using HakamiqChdTool.App.Core.Workflow;
using Serilog;
using System;
using System.IO;
using System.Text;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // Characterization, not a fix: the descriptor reference set already normalizes 8.3 aliases to the long path, so a cue
    // naming the verified output by its short name reaches the "never delete the output" check and is refused. This test
    // locks that in; it passes without any alias-specific code in FlowSrcClean.
    private static void TestSourceCleanupNeverDeletesOutputThroughShortNameAlias(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "srcclean-alias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string cue = Path.Combine(root, "game-with-a-long-name.cue");
        string bin = Path.Combine(root, "game-with-a-long-name.bin");
        string chd = Path.Combine(root, "game-with-a-long-name.chd");
        File.WriteAllBytes(bin, [1, 2, 3, 4]);
        File.WriteAllBytes(chd, [9, 9, 9, 9]);

        StringBuilder shortChd = new(260);
        uint written = GetShortPathNameW(chd, shortChd, (uint)shortChd.Capacity);
        if (written == 0 || written >= shortChd.Capacity || !shortChd.ToString().Contains('~'))
        {
            Console.WriteLine("[NOTE] 8.3 names are disabled on this volume; alias case not exercised.");
            Directory.Delete(root, recursive: true);
            return;
        }

        string aliasName = Path.GetFileName(shortChd.ToString());
        File.WriteAllText(cue, "FILE \"game-with-a-long-name.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\nFILE \"" + aliasName + "\" BINARY\r\n  TRACK 02 AUDIO\r\n    INDEX 01 00:00:00\r\n");

        var pipeline = new WorkflowSourceCleanupPipeline(new LoggerConfiguration().CreateLogger());
        pipeline.Run(new WorkflowSourceCleanupRequest(cue, chd, WorkflowSourceCleanupMode.VerifiedConversion, IsVerified: true, IsEnabled: true));

        AssertFalse(File.Exists(bin), "Control: the cleanup pipeline ran and removed the plain source file.");
        AssertTrue(File.Exists(chd), "The verified output survives source cleanup even when the descriptor names it by its 8.3 alias.");
        Directory.Delete(root, recursive: true);
    }
}
