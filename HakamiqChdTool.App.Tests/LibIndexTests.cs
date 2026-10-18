using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Core.Library;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static LibraryIndexResult IndexLibrary(
        string root,
        CancellationToken cancellationToken = default,
        IMediaInputClassifier? classifier = null) =>
        (classifier is null ? new LibraryIndexer() : new LibraryIndexer(classifier))
            .IndexAsync(root, cancellationToken)
            .GetAwaiter()
            .GetResult();

    private static Dictionary<string, string> SnapshotTree(string root) =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => path,
                path => Directory.Exists(path)
                    ? "dir"
                    : $"{new FileInfo(path).Length}|{File.GetLastWriteTimeUtc(path).Ticks}|{Convert.ToHexString(File.ReadAllBytes(path))}");

    private static void TestLibraryIndexKindsNestingAndCounts(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "lib-index-basic");
        Directory.CreateDirectory(Path.Combine(root, "empty"));
        Directory.CreateDirectory(Path.Combine(root, "a", "deep"));
        Directory.CreateDirectory(Path.Combine(root, "b"));

        WriteMediaFile(root, "game.iso", [1, 2, 3, 4]);
        WriteMediaFile(root, "readme.txt", [1]);
        WriteMediaFile(root, "game.nes", [1, 2]);
        WriteMediaFile(Path.Combine(root, "a"), "disc.cue", [1]);
        WriteMediaFile(Path.Combine(root, "a"), "disc.bin", [1, 2, 3]);
        WriteMediaFile(Path.Combine(root, "a", "deep"), "game.iso", [9, 9, 9, 9, 9]);
        WriteMediaFile(Path.Combine(root, "b"), "game.iso", [7]);
        WriteMediaFile(Path.Combine(root, "b"), "ok.chd", BuildChdHeader(version: 5));
        WriteMediaFile(Path.Combine(root, "b"), "bad.chd", new byte[16]);
        WriteMediaFile(Path.Combine(root, "b"), "ok.cso", Encoding.ASCII.GetBytes("CISO"));
        WriteMediaFile(Path.Combine(root, "b"), "disc.gdi", [1]);
        WriteMediaFile(Path.Combine(root, "b"), "app.pkg", [0x7F, 0x50, 0x4B, 0x47]);

        Dictionary<string, string> before = SnapshotTree(root);
        LibraryIndexResult result = IndexLibrary(root);

        AssertEqual(LibraryIndexOutcome.Completed, result.Outcome, "Clean library must complete.");
        AssertTrue(result.IsComplete, "Clean library must report complete.");
        AssertEqual(0, result.Issues.Count, "Clean library must have no issues.");
        AssertEqual(12, result.Entries.Count, "Every file must be indexed exactly once.");
        AssertEqual(5, result.DirectoriesVisited, "Root, empty, a, deep and b must be visited.");

        AssertEqual(3, result.CountsByKind[MediaInputKind.ISO], "ISO count (same name in three directories).");
        AssertEqual(2, result.CountsByKind[MediaInputKind.CHD], "CHD count includes the unconfirmed header.");
        AssertEqual(1, result.CountsByKind[MediaInputKind.CSO], "CSO count.");
        AssertEqual(1, result.CountsByKind[MediaInputKind.PKG], "PKG count.");
        AssertEqual(1, result.CountsByKind[MediaInputKind.CUE], "CUE count.");
        AssertEqual(1, result.CountsByKind[MediaInputKind.BIN], "BIN count.");
        AssertEqual(1, result.CountsByKind[MediaInputKind.GDI], "GDI count.");
        AssertEqual(2, result.CountsByKind[MediaInputKind.Other], "Unsupported files stay Other.");
        AssertEqual(0, result.CountsByKind[MediaInputKind.Unknown], "No Unknown expected.");
        AssertFalse(result.CountsByKind.ContainsKey(MediaInputKind.Folder), "Folders are not file entries.");
        AssertEqual(result.Entries.Count, result.CountsByKind.Values.Sum(), "Counts must add up to entries.");

        LibraryIndexEntry[] isos = result.Entries.Where(e => e.Kind == MediaInputKind.ISO).ToArray();
        AssertEqual(3, isos.Select(e => e.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "Duplicate filenames must keep distinct relative paths.");
        AssertTrue(isos.All(e => e.FileName == "game.iso" && e.Extension == ".iso"), "Filename and extension must be reported.");
        AssertEqual(5L, isos.Single(e => e.RelativePath == Path.Combine("a", "deep", "game.iso")).SizeBytes, "Size must come from file metadata.");

        LibraryIndexEntry good = result.Entries.Single(e => e.RelativePath == Path.Combine("b", "ok.chd"));
        LibraryIndexEntry bad = result.Entries.Single(e => e.RelativePath == Path.Combine("b", "bad.chd"));
        AssertEqual(LibraryIndexEntryStatus.Indexed, good.Status, "Valid CHD header must index normally.");
        AssertEqual(LibraryIndexEntryStatus.HeaderNotConfirmed, bad.Status, "Mismatched CHD header must be flagged, not hidden.");
        AssertEqual(MediaInputProbeStatus.HeaderMismatch, bad.ProbeStatus, "Probe evidence must be preserved.");

        AssertTrue(result.Entries.All(e => e.Platform == "Unknown"), "Platform must remain explicitly unknown.");
        AssertEqual(result.Entries.Sum(e => e.SizeBytes ?? 0L), result.TotalSizeBytes, "Total size must equal the sum of entries.");

        var after = SnapshotTree(root);
        AssertEqual(before.Count, after.Count, "Indexing must not add or remove entries.");
        AssertTrue(before.All(pair => after.TryGetValue(pair.Key, out string? value) && value == pair.Value), "Indexing must not modify any file or timestamp.");
    }

    private static void TestLibraryIndexEmptyAndRejectedRoots(string workDirectory)
    {
        string empty = Path.Combine(workDirectory, "lib-index-empty");
        Directory.CreateDirectory(empty);
        LibraryIndexResult emptyResult = IndexLibrary(empty);
        AssertEqual(LibraryIndexOutcome.Completed, emptyResult.Outcome, "Empty library is a complete, empty index.");
        AssertEqual(0, emptyResult.Entries.Count, "Empty library has no entries.");

        LibraryIndexResult missing = IndexLibrary(Path.Combine(workDirectory, "lib-index-missing"));
        AssertEqual(LibraryIndexOutcome.Failed, missing.Outcome, "Missing root must fail.");
        AssertEqual(LibraryIndexIssueKind.RootRejected, missing.Issues.Single().Kind, "Missing root must be reported.");

        string file = WriteMediaFile(workDirectory, "lib-index-root-file.iso", [1]);
        AssertEqual(LibraryIndexOutcome.Failed, IndexLibrary(file).Outcome, "A file is not a library root.");

        AssertEqual(LibraryIndexOutcome.Failed, IndexLibrary(Path.GetPathRoot(workDirectory)!).Outcome, "A volume root must be rejected.");
        AssertEqual(LibraryIndexOutcome.Failed, IndexLibrary("   ").Outcome, "Blank root must fail.");
    }

    private static void TestLibraryIndexExcludesReservedWorkspaces(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "lib-index-reserved");
        string[] reserved = ["Hakamiq Work", ".HakamiqCHDTool", ".hakamiq-pending", "HAKAMIQCHDTOOL"];
        string[] regular = ["Hakamiq Works", "HakamiqCHDTool2", "games"];

        Directory.CreateDirectory(root);
        WriteMediaFile(root, "top.iso", [1]);
        foreach (string name in reserved.Concat(regular))
        {
            Directory.CreateDirectory(Path.Combine(root, name, "nested"));
            WriteMediaFile(Path.Combine(root, name), "in.iso", [1]);
            WriteMediaFile(Path.Combine(root, name, "nested"), "deep.iso", [1]);
        }

        Dictionary<string, string> before = SnapshotTree(root);
        LibraryIndexResult result = IndexLibrary(root);

        AssertEqual(LibraryIndexOutcome.Completed, result.Outcome, "Skipping workspace folders is policy, not an issue.");
        string[] paths = result.Entries.Select(e => e.RelativePath).ToArray();
        foreach (string name in reserved)
        {
            AssertFalse(paths.Any(p => p.StartsWith(name + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)),
                "Reserved workspace '" + name + "' must not be indexed.");
        }

        foreach (string name in regular)
        {
            AssertTrue(paths.Contains(Path.Combine(name, "in.iso")), "Regular folder '" + name + "' must be indexed.");
            AssertTrue(paths.Contains(Path.Combine(name, "nested", "deep.iso")), "Regular nested folder under '" + name + "' must be indexed.");
        }

        AssertEqual(1 + regular.Length * 2, result.Entries.Count, "Only top-level and regular folder files expected.");

        foreach (string name in reserved)
        {
            LibraryIndexResult rejected = IndexLibrary(Path.Combine(root, name));
            AssertEqual(LibraryIndexOutcome.Failed, rejected.Outcome, "Reserved workspace root '" + name + "' must be rejected.");
            AssertEqual(LibraryIndexIssueKind.RootRejected, rejected.Issues.Single().Kind, "Rejection must be RootRejected.");
            AssertEqual(0, rejected.Entries.Count, "Rejected root must index nothing.");
        }

        AssertEqual(LibraryIndexOutcome.Completed, IndexLibrary(Path.Combine(root, "games")).Outcome, "Regular root must still index.");

        var after = SnapshotTree(root);
        AssertTrue(before.Count == after.Count && before.All(p => after.TryGetValue(p.Key, out string? v) && v == p.Value), "Indexing must not modify the tree.");
    }

    private static void TestLibraryIndexJunctionIsNotFollowed(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "lib-index-junction");
        string library = Path.Combine(root, "library");
        string outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(library);
        Directory.CreateDirectory(outside);
        WriteMediaFile(library, "inside.iso", [1]);
        WriteMediaFile(outside, "outside.iso", [2]);

        string junction = Path.Combine(library, "linked");
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Arguments = $"/c mklink /J \"{junction}\" \"{outside}\""
        };
        using (Process process = Process.Start(start) ?? throw new InvalidOperationException("Junction fixture could not start."))
        {
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            AssertEqual(0, process.ExitCode, "Junction fixture failed: " + output);
        }

        LibraryIndexResult result = IndexLibrary(library);
        AssertEqual(LibraryIndexOutcome.CompletedWithIssues, result.Outcome, "A skipped junction makes the index incomplete.");
        AssertFalse(result.IsComplete, "Skipped junction must not read as complete.");
        AssertEqual("inside.iso", result.Entries.Single().RelativePath, "Only the in-library file may be indexed.");
        AssertEqual(LibraryIndexIssueKind.ReparsePointSkipped, result.Issues.Single().Kind, "Junction skip must be reported.");
        AssertEqual("linked", result.Issues.Single().RelativePath, "Junction skip must name the entry.");

        // A junction used as the root itself is rejected outright.
        AssertEqual(LibraryIndexOutcome.Failed, IndexLibrary(junction).Outcome, "Junction root must be rejected.");
    }

    private static void TestLibraryIndexFileSymlinkIsNotFollowed(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "lib-index-file-symlink");
        string library = Path.Combine(root, "library");
        Directory.CreateDirectory(library);
        string target = WriteMediaFile(root, "outside.iso", [1, 2, 3, 4]);
        string link = Path.Combine(library, "linked.iso");

        TryCreateSymlinkOrSkip(() => File.CreateSymbolicLink(link, target), "file");

        LibraryIndexResult result = IndexLibrary(library);
        AssertEqual(0, result.Entries.Count, "A file symlink must not be indexed.");
        AssertEqual(LibraryIndexIssueKind.ReparsePointSkipped, result.Issues.Single().Kind, "File symlink skip must be reported.");
        AssertEqual(LibraryIndexOutcome.CompletedWithIssues, result.Outcome, "Skipped symlink makes the index incomplete.");
    }

    private static void TestLibraryIndexDirectorySymlinkIsNotFollowed(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "lib-index-dir-symlink");
        string library = Path.Combine(root, "library");
        string outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(library);
        Directory.CreateDirectory(outside);
        WriteMediaFile(outside, "outside.iso", [1, 2, 3, 4]);
        string link = Path.Combine(library, "linked-dir");

        TryCreateSymlinkOrSkip(() => Directory.CreateSymbolicLink(link, outside), "directory");

        LibraryIndexResult result = IndexLibrary(library);
        AssertEqual(0, result.Entries.Count, "A directory symlink must not be traversed.");
        AssertEqual(LibraryIndexIssueKind.ReparsePointSkipped, result.Issues.Single().Kind, "Directory symlink skip must be reported.");
        AssertEqual(LibraryIndexOutcome.CompletedWithIssues, result.Outcome, "Skipped symlink makes the index incomplete.");
    }

    private static void TryCreateSymlinkOrSkip(Action create, string kind)
    {
        try
        {
            create();
        }
        // Skip only for ERROR_PRIVILEGE_NOT_HELD (1314). Any other failure is a real
        // fixture error and must fail the test instead of hiding behind a skip.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            && (ex.HResult & 0xFFFF) == 1314)
        {
            throw new TestSkippedException("Windows denied " + kind + " symbolic-link creation for this account: " + ex.Message);
        }
    }

    /// <summary>Fails the fixture, not the index, when the deny entry does not actually block listing.</summary>
    private static void AssertDeniedByAcl(string directory)
    {
        try
        {
            _ = Directory.GetFileSystemEntries(directory);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        throw new InvalidOperationException(
            "Fixture precondition failed: the deny ACE did not block listing for this process (backup/restore privilege or another ACL bypass is active).");
    }

    private static IDisposable SuspendAclBypassPrivileges()
    {
        List<string> disabled = [];

        foreach (string privilege in new[] { "SeBackupPrivilege", "SeRestorePrivilege" })
        {
            if (SetPrivilege(privilege, enable: false, out bool wasEnabled) && wasEnabled)
            {
                disabled.Add(privilege);
            }
        }

        return new PrivilegeRestore(disabled);
    }

    private sealed class PrivilegeRestore(List<string> privileges) : IDisposable
    {
        public void Dispose()
        {
            foreach (string privilege in privileges)
            {
                SetPrivilege(privilege, enable: true, out _);
            }
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivilege
    {
        public int Count;
        public long Luid;
        public int Attributes;
    }

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool LookupPrivilegeValueW(string? system, string name, out long luid);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivilege state, int length, out TokenPrivilege previous, out int returned);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>Adjusts a privilege on the current process token. Returns false when the token does not hold it.</summary>
    private static bool SetPrivilege(string name, bool enable, out bool wasEnabled)
    {
        const int TokenAdjustPrivileges = 0x20;
        const int TokenQuery = 0x8;
        const int Enabled = 2;
        wasEnabled = false;

        if (!LookupPrivilegeValueW(null, name, out long luid)
            || !OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out IntPtr token))
        {
            return false;
        }

        try
        {
            TokenPrivilege state = new() { Count = 1, Luid = luid, Attributes = enable ? Enabled : 0 };
            bool ok = AdjustTokenPrivileges(token, false, ref state, System.Runtime.InteropServices.Marshal.SizeOf<TokenPrivilege>(), out TokenPrivilege previous, out _);
            int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            wasEnabled = ok && previous.Count == 1 && (previous.Attributes & Enabled) != 0;
            return ok && error == 0;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static void TestLibraryIndexReportsAccessDenied(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "lib-index-denied");
        string locked = Path.Combine(root, "locked");
        Directory.CreateDirectory(locked);
        WriteMediaFile(root, "open.iso", [1]);
        WriteMediaFile(locked, "hidden.iso", [2]);

        SecurityIdentifier me = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current user SID unavailable.");
        var deny = new FileSystemAccessRule(
            me,
            FileSystemRights.ListDirectory,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Deny);

        var info = new DirectoryInfo(locked);
        DirectorySecurity security = info.GetAccessControl();
        security.AddAccessRule(deny);
        try
        {
            info.SetAccessControl(security);
        }
        catch (UnauthorizedAccessException ex)
        {
            Directory.Delete(root, recursive: true);
            throw new TestSkippedException("This account cannot set the ACL needed to create an inaccessible directory: " + ex.Message);
        }

        // An elevated process normally holds SeBackup/SeRestore enabled, and those privileges
        // bypass every ACL, so the deny entry would have no effect. Disabling them affects only
        // this process token; no workstation setting changes.
        using IDisposable backupBypassOff = SuspendAclBypassPrivileges();

        try
        {
            AssertDeniedByAcl(locked);
            LibraryIndexResult result = IndexLibrary(root);
            AssertEqual("open.iso", result.Entries.Single().RelativePath, "Readable files must still be indexed.");
            AssertEqual(LibraryIndexIssueKind.DirectoryEnumerationFailed, result.Issues.Single().Kind, "Access denial must be reported.");
            AssertEqual("locked", result.Issues.Single().RelativePath, "Denied directory must be named.");
            AssertEqual(LibraryIndexOutcome.CompletedWithIssues, result.Outcome, "Denied directory makes the index incomplete.");
        }
        finally
        {
            security = info.GetAccessControl();
            security.RemoveAccessRule(deny);
            info.SetAccessControl(security);
        }
    }

    private static void TestLibraryIndexCancellation(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "lib-index-cancel");
        Directory.CreateDirectory(root);
        for (int i = 0; i < 5; i++)
        {
            WriteMediaFile(root, $"g{i}.iso", [1]);
        }

        using (var pre = new CancellationTokenSource())
        {
            pre.Cancel();
            LibraryIndexResult early = IndexLibrary(root, pre.Token);
            AssertEqual(LibraryIndexOutcome.Cancelled, early.Outcome, "Pre-cancelled index must report Cancelled.");
            AssertEqual(0, early.Entries.Count, "Pre-cancelled index must do no work.");
            AssertFalse(early.IsComplete, "Cancelled index is never complete.");
        }

        using var mid = new CancellationTokenSource();
        var classifier = new HookClassifier((_, count) =>
        {
            if (count == 2)
            {
                mid.Cancel();
            }
        });
        LibraryIndexResult partial = IndexLibrary(root, mid.Token, classifier);
        AssertEqual(LibraryIndexOutcome.Cancelled, partial.Outcome, "Mid-run cancellation must report Cancelled.");
        AssertEqual(1, partial.Entries.Count, "Entries completed before cancellation must be kept.");
        AssertFalse(partial.IsComplete, "Partial index must not read as complete.");
    }

    private static void TestLibraryIndexFileDisappearsDuringEnumeration(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "lib-index-vanish");
        Directory.CreateDirectory(root);
        string first = WriteMediaFile(root, "a.iso", [1]);
        string second = WriteMediaFile(root, "b.iso", [2]);

        var classifier = new HookClassifier((path, _) =>
        {
            if (string.Equals(path, first, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(second);
            }
        });
        LibraryIndexResult result = IndexLibrary(root, classifier: classifier);

        AssertEqual("a.iso", result.Entries.Single().RelativePath, "Surviving file must be indexed.");
        AssertEqual(LibraryIndexIssueKind.FileUnavailable, result.Issues.Single().Kind, "Vanished file must be reported.");
        AssertEqual("b.iso", result.Issues.Single().RelativePath, "Vanished file must be named.");
        AssertEqual(LibraryIndexOutcome.CompletedWithIssues, result.Outcome, "Vanished file makes the index incomplete.");
    }

    private static void TestLibraryIndexLargeFileUsesBoundedProbe(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "lib-index-large");
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "huge.chd");
        const long size = 6L * 1024 * 1024 * 1024;

        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(BuildChdHeader(version: 5));
            stream.SetLength(size);
        }

        try
        {
            // Process-wide read-byte counter: a full read of 6 GiB cannot hide inside this bound.
            // It proves this process read little data during the call, not which API did so.
            GetProcessIoCounters(Process.GetCurrentProcess().Handle, out IoCounters beforeIo);
            LibraryIndexResult result = IndexLibrary(root);
            GetProcessIoCounters(Process.GetCurrentProcess().Handle, out IoCounters afterIo);
            AssertTrue(afterIo.ReadTransferCount - beforeIo.ReadTransferCount < 16UL * 1024 * 1024,
                "Indexing a 6 GiB file read " + (afterIo.ReadTransferCount - beforeIo.ReadTransferCount) + " bytes.");

            LibraryIndexEntry entry = result.Entries.Single();
            AssertEqual(size, entry.SizeBytes, "Size must come from metadata.");
            AssertEqual(LibraryIndexEntryStatus.Indexed, entry.Status, "Header probe must confirm the large CHD.");
            AssertEqual(MediaInputProbeStatus.HeaderEnvelopeValid, entry.ProbeStatus, "Only the header envelope is evidence.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);

    private sealed class HookClassifier(Action<string, int> beforeClassify) : IMediaInputClassifier
    {
        private int count;

        public ValueTask<MediaInputDescriptor> ClassifyAsync(string path, CancellationToken cancellationToken)
        {
            beforeClassify(path, ++count);
            return MediaInputClassifier.Shared.ClassifyAsync(path, cancellationToken);
        }
    }
}
