using HakamiqChdTool.App.Services;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static void AddZipText(ZipArchive zip, string name, string text)
    {
        using Stream stream = zip.CreateEntry(name).Open();
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    // Real 7-Zip, real extraction: an archive entry literally named "d*.bin" must not be passed to 7-Zip as a wildcard
    // that also pulls out sibling entries ("disc.bin", "gz.chd").
    private static void TestRealExtractionDoesNotExpandWildcardReferences(string workDirectory)
    {
        var service = new SevenZipArchiveExtractionService();
        if (!service.IsAvailable)
        {
            Console.WriteLine("[NOTE] 7-Zip is not available; real wildcard extraction not exercised.");
            return;
        }

        string root = Path.Combine(workDirectory, "wildcard-real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string zipPath = Path.Combine(root, "game.zip");
        using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddZipText(zip, "g*.chd", new string('c', 512));
            AddZipText(zip, "gz.chd", new string('b', 512));
        }

        string destination = Path.Combine(root, "out");
        Directory.CreateDirectory(destination);
        ArchiveExtractionResult result = service.ExtractFirstChdAsync(zipPath, destination).GetAwaiter().GetResult();
        Console.WriteLine("[NOTE] wildcard extraction success=" + result.IsSuccess);

        string[] extracted = Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetFileName(path)).ToArray();
        Console.WriteLine("[NOTE] wildcard extraction produced: " + string.Join(", ", extracted));
        // "g*.chd" is not selectable; "gz.chd" is the only plain CHD, so exactly that one file may appear. With the wildcard
        // passed through, 7-Zip extracts every match of "g*.chd" (here two files).
        AssertEqual(1, extracted.Length, "Exactly one archive entry was extracted: " + string.Join(", ", extracted));
        AssertEqual("gz.chd", extracted[0], "Only the plain entry was extracted.");
        Directory.Delete(root, recursive: true);
    }

    // Cancellation through the real service: a cancelled token yields a cancelled result and leaves nothing behind.
    private static void TestRealExtractionHonoursCancellation(string workDirectory)
    {
        var service = new SevenZipArchiveExtractionService();
        if (!service.IsAvailable)
        {
            return;
        }

        string root = Path.Combine(workDirectory, "cancel-real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string zipPath = Path.Combine(root, "game.zip");
        using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddZipText(zip, "game.chd", new string('c', 512));
        }

        string destination = Path.Combine(root, "out");
        Directory.CreateDirectory(destination);
        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        ArchiveExtractionResult? result = null;
        try
        {
            result = service.ExtractFirstChdAsync(zipPath, destination, null, cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        AssertTrue(result is null || (result.WasCancelled && !result.IsSuccess), "A cancelled extraction does not report success.");
        AssertEqual(0, Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories).Count(), "A cancelled extraction leaves no extracted files.");
        Directory.Delete(root, recursive: true);
    }

    // The extraction destination must be empty: 7-Zip overwrites with -y and failure cleanup empties the directory, so a
    // pre-existing user file there would be replaced or deleted. It is refused instead and the file survives.
    private static void TestExtractionRefusesNonEmptyDestinationAndKeepsUserFiles(string workDirectory)
    {
        var service = new SevenZipArchiveExtractionService();
        if (!service.IsAvailable)
        {
            return;
        }

        string root = Path.Combine(workDirectory, "nonempty-dest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string zipPath = Path.Combine(root, "game.zip");
        using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddZipText(zip, "game.chd", new string('c', 512));
        }

        string destination = Path.Combine(root, "out");
        Directory.CreateDirectory(destination);
        string userFile = Path.Combine(destination, "game.chd");
        File.WriteAllText(userFile, "user data");
        string userSub = Path.Combine(destination, "notes");
        Directory.CreateDirectory(userSub);
        File.WriteAllText(Path.Combine(userSub, "a.txt"), "keep");

        bool refused = false;
        try
        {
            ArchiveExtractionResult result = service.ExtractFirstChdAsync(zipPath, destination).GetAwaiter().GetResult();
            refused = !result.IsSuccess;
        }
        catch (InvalidOperationException)
        {
            refused = true;
        }

        AssertTrue(refused, "Extraction into a non-empty directory is refused.");
        AssertEqual("user data", File.ReadAllText(userFile), "The pre-existing file with the archive entry's name is not overwritten.");
        AssertEqual("keep", File.ReadAllText(Path.Combine(userSub, "a.txt")), "Other pre-existing files survive.");
        Directory.Delete(root, recursive: true);
    }

    // Descriptor reads stream one entry to memory with `7z e -so`; a wildcard entry name must not select several.
    private static void TestDescriptorReadDoesNotExpandWildcardEntry(string workDirectory)
    {
        if (!SevenZipToolService.Instance.TryGetExecutablePath(out string sevenZip))
        {
            return;
        }

        string root = Path.Combine(workDirectory, "descriptor-wildcard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string zipPath = Path.Combine(root, "game.zip");
        using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddZipText(zip, "g*.cue", "FILE \"a.bin\" BINARY");
            AddZipText(zip, "gz.cue", "FILE \"b.bin\" BINARY");
        }

        var wildcard = SevenZipArchiveInspector.ReadDescriptorTextAsync(sevenZip, zipPath, "g*.cue", System.Threading.CancellationToken.None)
            .GetAwaiter().GetResult();
        AssertFalse(wildcard.IsSuccess, "A wildcard entry name is not read.");

        var plain = SevenZipArchiveInspector.ReadDescriptorTextAsync(sevenZip, zipPath, "gz.cue", System.Threading.CancellationToken.None)
            .GetAwaiter().GetResult();
        AssertTrue(plain.IsSuccess && plain.Text.Contains("b.bin", StringComparison.Ordinal) && !plain.Text.Contains("a.bin", StringComparison.Ordinal),
            "A plain entry name reads exactly that entry.");
        Directory.Delete(root, recursive: true);
    }

    // The real cue normalization path refuses an oversized cue and leaves it untouched.
    private static void TestCueNormalizationRefusesOversizedCueAndKeepsFile(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "cue-norm-big-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string cue = Path.Combine(root, "big.cue");
        File.WriteAllBytes(Path.Combine(root, "big.bin"), [1, 2, 3, 4]);
        using (FileStream stream = File.Create(cue))
        {
            stream.SetLength(2L * 1024 * 1024);
        }

        bool ok = HakamiqChdTool.App.Core.Workflow.WorkflowSafePathValidator.TryNormalizeCuePrimaryBinReference(cue, out _);
        AssertFalse(ok, "An oversized cue is not normalized.");
        AssertEqual(2L * 1024 * 1024, new FileInfo(cue).Length, "The oversized cue is not rewritten.");
        Directory.Delete(root, recursive: true);
    }
}
