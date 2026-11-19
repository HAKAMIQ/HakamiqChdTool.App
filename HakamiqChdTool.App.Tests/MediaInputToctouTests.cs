using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Services;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // SECURITY REGRESSION tests for the TOCTOU race between path validation and the header probe
    // (docs/SECURITY_TODO_TOCTOU.md). The swap happens in a seam right after validation. A junction
    // or symbolic link swapped in there must be refused (not MagicConfirmed). A plain file replaced
    // in place stays a regular file at the validated path and is classified as what it is; the
    // conversion source lease then keeps it from changing while a job runs.
    // Fixtures: A has wrong magic, B has CISO; the classifier reads only four bytes.
    private static byte[] ToctouOriginal => [0x41, 0x41, 0x41, 0x41, 0x11];
    private static byte[] ToctouAlternate => [0x43, 0x49, 0x53, 0x4f, 0x22];

    private static void TestToctouParentJunctionIsRefused(string workDirectory) =>
        RunToctouSwap(workDirectory, "junction", (path, _, root) =>
        {
            string parent = Path.GetDirectoryName(path)!;
            Directory.Move(parent, Path.Combine(root, "original-parent"));
            CreateToctouJunction(parent, Path.Combine(root, "alternate"));
        }, expectRefused: true, parentJunction: true);

    private static void TestToctouFileSymlinkIsRefused(string workDirectory) =>
        RunToctouSwap(workDirectory, "symlink", (path, alternate, _) => File.CreateSymbolicLink(path, alternate),
            expectRefused: true, requireSymlink: true);

    private static void TestToctouRegularReplacementIsClassifiedAsIs(string workDirectory) =>
        RunToctouSwap(workDirectory, "regular", (path, alternate, _) => File.Move(alternate, path), expectRefused: false);

    private static void RunToctouSwap(
        string workDirectory, string scenario, Action<string, string, string> replace,
        bool expectRefused, bool requireSymlink = false, bool parentJunction = false)
    {
        string root = Path.Combine(workDirectory, "toctou-" + scenario);
        string parent = Path.Combine(root, "input");
        string alternateParent = Path.Combine(root, "alternate");
        Directory.CreateDirectory(parent);
        Directory.CreateDirectory(alternateParent);
        string path = Path.Combine(parent, "disc.cso");
        string alternate = Path.Combine(alternateParent, "disc.cso");
        string saved = Path.Combine(parentJunction ? root : parent, "original.cso");
        File.WriteAllBytes(path, ToctouOriginal);
        File.WriteAllBytes(alternate, ToctouAlternate);
        if (requireSymlink)
        {
            RequireToctouSymlink(root, alternate);
        }

        if (parentJunction)
        {
            string trial = Path.Combine(root, "trial-junction");
            CreateToctouJunction(trial, alternateParent);
            Directory.Delete(trial);
        }

        AssertEqual(MediaInputProbeStatus.MagicConfirmed, new MediaInputClassifier().Classify(alternate).ProbeStatus,
            "The alternate fixture has CSO magic.");

        int calls = 0;
        string? link = null;
        try
        {
            var classifier = new MediaInputClassifier(validatedPath =>
            {
                calls++;
                // Windows cannot rename a directory that holds an open file, so A is moved out first.
                File.Move(path, saved);
                replace(path, alternate, root);
                if (requireSymlink || parentJunction)
                {
                    link = parentJunction ? parent : path;
                }
            });

            MediaInputDescriptor result = classifier.Classify(path);
            AssertEqual(1, calls, "The swap happens exactly once, after validation and before probing.");
            if (expectRefused)
            {
                AssertTrue(result.ProbeStatus != MediaInputProbeStatus.MagicConfirmed,
                    "A " + scenario + " swapped in after validation is not probed: " + result.ProbeStatus);
                AssertFalse(MediaInputPipeline.Decide(result).QueueRole == QueueInputRole.ConvertibleDiscImage,
                    "The swapped " + scenario + " target does not enter the queue.");
            }
            else
            {
                AssertEqual(MediaInputProbeStatus.MagicConfirmed, result.ProbeStatus,
                    "A plain file replaced in place is classified as the file now at the path.");
            }

            AssertTrue(File.ReadAllBytes(saved).SequenceEqual(ToctouOriginal), "The original source survives unchanged.");
        }
        finally
        {
            if (link is not null)
            {
                if (parentJunction)
                {
                    Directory.Delete(link);
                }
                else
                {
                    File.Delete(link);
                }
            }
        }
    }

    private static void TestToctouMissingThenSymlinkIsRefused(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "toctou-missing");
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "missing.cso");
        string target = Path.Combine(root, "target.cso");
        File.WriteAllBytes(target, ToctouAlternate);
        RequireToctouSymlink(root, target);
        try
        {
            var classifier = new MediaInputClassifier(p => File.CreateSymbolicLink(p, target));
            AssertTrue(classifier.Classify(path).ProbeStatus != MediaInputProbeStatus.MagicConfirmed,
                "A link created after the missing-path gate is not followed.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void TestToctouHookIsolationAndCancellation(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "toctou-controls");
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "disc.cso");
        File.WriteAllBytes(path, ToctouAlternate);
        int calls = 0;
        var isolated = new MediaInputClassifier(_ => calls++);
        AssertEqual(MediaInputProbeStatus.MagicConfirmed, isolated.Classify(path).ProbeStatus, "A hooked classifier still classifies a normal file.");
        AssertEqual(MediaInputProbeStatus.MagicConfirmed, MediaInputClassifier.Shared.Classify(path).ProbeStatus, "The shared classifier is unaffected.");
        AssertEqual(1, calls, "The callback belongs only to its own classifier.");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            isolated.ClassifyAsync(path, cancelled.Token).AsTask().GetAwaiter().GetResult();
            throw new InvalidOperationException("Expected cancellation before the hook.");
        }
        catch (OperationCanceledException)
        {
        }

        AssertEqual(1, calls, "A cancelled call does not reach the hook.");
        AssertEqual(1, Directory.GetFiles(root).Length, "The classifier creates no files.");
    }

    // The conversion source lease: readers may share the files; nobody may write, rename or delete
    // them or rename their folder while it is held; a path through a junction is refused.
    private static void TestSourceLeaseBlocksSwapsAndAllowsReaders(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "source-lease");
        string folder = Path.Combine(root, "game");
        Directory.CreateDirectory(folder);
        string source = Path.Combine(folder, "game.iso");
        File.WriteAllBytes(source, [1, 2, 3, 4]);

        AssertTrue(SourceFileLease.TryAcquire([source], out SourceFileLease? lease, out _), "A regular file is leased.");
        using (lease)
        {
            using (var reader = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                AssertEqual(4L, reader.Length, "Readers such as chdman can still open the file.");
            }

            AssertThrows<IOException>(() => File.WriteAllBytes(source, [9]), "The source cannot be written while leased.");
            AssertThrows<IOException>(() => File.Move(source, Path.Combine(folder, "moved.iso")), "The source cannot be renamed while leased.");
            AssertThrows<IOException>(() => File.Delete(source), "The source cannot be deleted while leased.");
            AssertThrowsAny(() => Directory.Move(folder, Path.Combine(root, "renamed")), "Its folder cannot be renamed while leased.");
        }

        File.Move(source, Path.Combine(folder, "moved.iso"));
        AssertTrue(File.Exists(Path.Combine(folder, "moved.iso")), "The lease is released when disposed.");

        string target = Path.Combine(root, "target");
        Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(target, "game.iso"), [5, 6]);
        string junction = Path.Combine(root, "via-junction");
        CreateToctouJunction(junction, target);
        try
        {
            AssertFalse(SourceFileLease.TryAcquire([Path.Combine(junction, "game.iso")], out _, out _), "A path through a junction is refused.");
        }
        finally
        {
            Directory.Delete(junction);
        }

        string busy = Path.Combine(folder, "busy.iso");
        File.WriteAllBytes(busy, [7]);
        using (new FileStream(busy, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            AssertFalse(SourceFileLease.TryAcquire([busy], out _, out string? failed), "A file another program is writing is not leased.");
            AssertEqual(busy, failed, "The failing path is reported.");
        }
    }

    private static void TestSourceDependencySetListsTrackFiles(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "source-deps");
        string cueDir = Path.Combine(root, "cue");
        Directory.CreateDirectory(cueDir);
        string cue = Path.Combine(cueDir, "game.cue");
        File.WriteAllText(cue, "FILE \"game (Track 1).bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\nFILE \"game (Track 2).bin\" BINARY\r\n  TRACK 02 AUDIO\r\n    INDEX 01 00:00:00\r\n");
        File.WriteAllBytes(Path.Combine(cueDir, "game (Track 1).bin"), [1]);
        File.WriteAllBytes(Path.Combine(cueDir, "game (Track 2).bin"), [2]);
        File.WriteAllBytes(Path.Combine(cueDir, "unrelated.bin"), [3]);
        string[] cueSet = [.. SourceDependencySet.Resolve(cue).Select(Path.GetFileName)!];
        AssertEqual("game.cue,game (Track 1).bin,game (Track 2).bin", string.Join(",", cueSet), "A CUE leases itself and its tracks only.");

        string ccd = WriteCcdSet(Path.Combine(root, "ccd"), "game", TwoTrackCcd, 4);
        AssertEqual("game.ccd,game.img,game.sub", string.Join(",", SourceDependencySet.Resolve(ccd).Select(Path.GetFileName)), "A CloneCD set is leased whole.");

        string mds = WriteMdsSet(Path.Combine(root, "mds"), "game", TwoTrackMds());
        AssertEqual("game.mds,game.mdf", string.Join(",", SourceDependencySet.Resolve(mds).Select(Path.GetFileName)), "An Alcohol set is leased whole.");

        string iso = Path.Combine(root, "single.iso");
        File.WriteAllBytes(iso, [1]);
        AssertEqual(1, SourceDependencySet.Resolve(iso).Count, "A single-file input leases only itself.");
    }

    private static void TestConversionRefusesSourceInUseForWriting(AppReflection app, string workDirectory)
    {
        string chdmanPath = app.GetRuntimeChdmanPath(app.CreateRuntimeToolService());
        string root = Path.Combine(workDirectory, "source-busy");
        string ccd = WriteCcdSet(Path.Combine(root, "in"), "game", TwoTrackCcd, 64);
        string outDir = Path.Combine(root, "out");
        Directory.CreateDirectory(outDir);
        string chd = Path.Combine(outDir, "game.chd");

        using (new FileStream(Path.ChangeExtension(ccd, ".img"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            ChdConversionResult refused = new ChdConversionService().ConvertToChdAsync(chdmanPath, ccd, chd).GetAwaiter().GetResult();
            AssertFalse(refused.IsSuccess, "A conversion whose track file is open for writing elsewhere does not run.");
            AssertTrue((refused.Message + refused.Error).Contains(SourceFileLease.SourceUnstableMessageKey, StringComparison.Ordinal),
                "The refusal names the reason: " + refused.Message);
            AssertFalse(File.Exists(chd), "No CHD is written.");
        }

        AssertTrue(new ChdConversionService().ConvertToChdAsync(chdmanPath, ccd, chd).GetAwaiter().GetResult().IsSuccess,
            "The same set converts once the other program lets go.");
    }

    private static void AssertThrowsAny(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void RequireToctouSymlink(string root, string target)
    {
        string trial = Path.Combine(root, "trial-link.cso");
        try
        {
            File.CreateSymbolicLink(trial, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException
            || ex is IOException io && (io.HResult & 0xffff) == 1314)
        {
            throw new TestSkippedException("NOT VERIFIED: file symlink unavailable: " + ex.Message);
        }

        File.Delete(trial);
    }

    private static void CreateToctouJunction(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new TestSkippedException("NOT VERIFIED: real junction requires Windows.");
        }

        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Arguments = $"/d /c mklink /J \"{link}\" \"{target}\""
        };
        try
        {
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Junction tool did not start.");
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new TestSkippedException("NOT VERIFIED: mklink /J exited " + process.ExitCode + ": " + output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
            }
        }
        catch (Win32Exception ex)
        {
            throw new TestSkippedException("NOT VERIFIED: junction tool unavailable: " + ex.Message);
        }

        AssertTrue((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0, "mklink /J creates a real reparse point.");
    }
}
