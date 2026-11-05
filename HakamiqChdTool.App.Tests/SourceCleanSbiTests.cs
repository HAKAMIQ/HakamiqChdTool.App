using HakamiqChdTool.App.Core.Workflow;
using Serilog;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // A CHD does not carry SBI (LibCrypt) data, so the source .sbi may only be deleted when an identical copy already sits
    // next to the verified CHD at another path. Otherwise it is the only copy and must survive source cleanup.
    private static void TestSourceCleanupKeepsSbiUnlessCopiedNextToOutput(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "srcclean-sbi-" + Guid.NewGuid().ToString("N"));
        var pipeline = new WorkflowSourceCleanupPipeline(new LoggerConfiguration().CreateLogger());

        // Output next to the source: <cue>.sbi and <chd>.sbi are the same file.
        string sameDir = Path.Combine(root, "same");
        (string cue, string bin, string sbi) = CreateSbiSourceSet(sameDir, [7, 7, 7]);
        string chd = Path.Combine(sameDir, "Game.chd");
        File.WriteAllBytes(chd, [9]);
        pipeline.Run(new WorkflowSourceCleanupRequest(cue, chd, WorkflowSourceCleanupMode.VerifiedConversion, IsVerified: true, IsEnabled: true));
        AssertFalse(File.Exists(bin) || File.Exists(cue), "Control: the cue and bin were deleted.");
        AssertTrue(File.Exists(sbi), "The only .sbi survives when the CHD sits next to the source.");

        // Output elsewhere, no copy (CopyMatchingSbi off or the copy failed).
        (cue, bin, sbi) = CreateSbiSourceSet(Path.Combine(root, "nocopy-in"), [7, 7, 7]);
        string outDir = Path.Combine(root, "nocopy-out");
        Directory.CreateDirectory(outDir);
        chd = Path.Combine(outDir, "Game.chd");
        File.WriteAllBytes(chd, [9]);
        pipeline.Run(new WorkflowSourceCleanupRequest(cue, chd, WorkflowSourceCleanupMode.VerifiedConversion, IsVerified: true, IsEnabled: true));
        AssertFalse(File.Exists(bin), "Control: the bin was deleted.");
        AssertTrue(File.Exists(sbi), "The .sbi survives when no copy exists next to the CHD.");

        // Output elsewhere, a different .sbi already next to the CHD (kept by skip-existing).
        (cue, _, sbi) = CreateSbiSourceSet(Path.Combine(root, "diff-in"), [7, 7, 7]);
        outDir = Path.Combine(root, "diff-out");
        Directory.CreateDirectory(outDir);
        chd = Path.Combine(outDir, "Game.chd");
        File.WriteAllBytes(chd, [9]);
        File.WriteAllBytes(Path.Combine(outDir, "Game.sbi"), [1, 2]);
        pipeline.Run(new WorkflowSourceCleanupRequest(cue, chd, WorkflowSourceCleanupMode.VerifiedConversion, IsVerified: true, IsEnabled: true));
        AssertTrue(File.Exists(sbi), "The .sbi survives when the copy next to the CHD has other content.");

        // A hard link next to the CHD is another name for the same file, not a copy.
        (cue, _, sbi) = CreateSbiSourceSet(Path.Combine(root, "link-in"), [7, 7, 7]);
        outDir = Path.Combine(root, "link-out");
        Directory.CreateDirectory(outDir);
        chd = Path.Combine(outDir, "Game.chd");
        File.WriteAllBytes(chd, [9]);
        AssertTrue(CreateHardLinkW(Path.Combine(outDir, "Game.sbi"), sbi, IntPtr.Zero), "Test setup: hard link created.");
        pipeline.Run(new WorkflowSourceCleanupRequest(cue, chd, WorkflowSourceCleanupMode.VerifiedConversion, IsVerified: true, IsEnabled: true));
        AssertTrue(File.Exists(sbi), "The .sbi survives when the file next to the CHD is a hard link to it.");

        // Output elsewhere with an identical copy: the source .sbi is redundant and is deleted with the set.
        (cue, bin, sbi) = CreateSbiSourceSet(Path.Combine(root, "copied-in"), [7, 7, 7]);
        outDir = Path.Combine(root, "copied-out");
        Directory.CreateDirectory(outDir);
        chd = Path.Combine(outDir, "Game.chd");
        File.WriteAllBytes(chd, [9]);
        File.WriteAllBytes(Path.Combine(outDir, "Game.sbi"), [7, 7, 7]);
        pipeline.Run(new WorkflowSourceCleanupRequest(cue, chd, WorkflowSourceCleanupMode.VerifiedConversion, IsVerified: true, IsEnabled: true));
        AssertFalse(File.Exists(sbi) || File.Exists(bin) || File.Exists(cue), "With an identical copy next to the CHD the whole source set is deleted.");
        AssertTrue(File.Exists(Path.Combine(outDir, "Game.sbi")), "The copy next to the CHD is kept.");

        Directory.Delete(root, recursive: true);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);

    private static (string Cue, string Bin, string Sbi) CreateSbiSourceSet(string directory, byte[] sbiBytes)
    {
        Directory.CreateDirectory(directory);
        string cue = Path.Combine(directory, "Game.cue");
        string bin = Path.Combine(directory, "Game.bin");
        string sbi = Path.Combine(directory, "Game.sbi");
        File.WriteAllBytes(bin, [1, 2, 3, 4]);
        File.WriteAllText(cue, "FILE \"Game.bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n");
        File.WriteAllBytes(sbi, sbiBytes);
        return (cue, bin, sbi);
    }
}
