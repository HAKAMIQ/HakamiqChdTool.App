using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Core.Workflow.Paths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Library;

public enum LibraryIndexEntryStatus
{
    /// <summary>Classified; any required header probe confirmed the format.</summary>
    Indexed,

    /// <summary>Classified by extension, but the bounded header probe did not confirm it.</summary>
    HeaderNotConfirmed
}

public enum LibraryIndexIssueKind
{
    RootRejected,
    DirectoryEnumerationFailed,
    ReparsePointSkipped,
    FileUnavailable
}

public enum LibraryIndexOutcome
{
    /// <summary>Every reachable entry was indexed and no issue was recorded.</summary>
    Completed,

    /// <summary>Traversal finished, but at least one entry or directory was not indexed.</summary>
    CompletedWithIssues,

    Cancelled,

    /// <summary>The root was rejected; nothing was traversed.</summary>
    Failed
}

public sealed record LibraryIndexEntry(
    string RelativePath,
    string FileName,
    string? Extension,
    MediaInputKind Kind,
    long? SizeBytes,
    LibraryIndexEntryStatus Status,
    MediaInputProbeStatus ProbeStatus)
{
    /// <summary>
    /// Platform identity is not established by the intake classifier; an extension is
    /// not proof of a console. It stays explicitly unknown until real evidence exists.
    /// </summary>
    public string Platform => UnknownPlatform;

    public const string UnknownPlatform = "Unknown";
}

public sealed record LibraryIndexIssue(
    string RelativePath,
    LibraryIndexIssueKind Kind,
    string Message);

public sealed class LibraryIndexResult(
    string rootPath,
    LibraryIndexOutcome outcome,
    IReadOnlyList<LibraryIndexEntry> entries,
    IReadOnlyList<LibraryIndexIssue> issues,
    int directoriesVisited)
{
    public string RootPath { get; } = rootPath;

    public LibraryIndexOutcome Outcome { get; } = outcome;

    public IReadOnlyList<LibraryIndexEntry> Entries { get; } = entries;

    public IReadOnlyList<LibraryIndexIssue> Issues { get; } = issues;

    public int DirectoriesVisited { get; } = directoriesVisited;

    public bool IsComplete => Outcome == LibraryIndexOutcome.Completed;

    public long TotalSizeBytes => Entries.Sum(entry => entry.SizeBytes ?? 0L);

    /// <summary>One count per file kind (every kind except Folder), zero when absent.</summary>
    public IReadOnlyDictionary<MediaInputKind, int> CountsByKind { get; } =
        BuildCounts(entries);

    private static Dictionary<MediaInputKind, int> BuildCounts(
        IReadOnlyList<LibraryIndexEntry> entries)
    {
        Dictionary<MediaInputKind, int> counts = Enum
            .GetValues<MediaInputKind>()
            .Where(kind => kind != MediaInputKind.Folder)
            .ToDictionary(kind => kind, _ => 0);

        foreach (LibraryIndexEntry entry in entries)
        {
            counts[entry.Kind]++;
        }

        return counts;
    }
}

/// <summary>
/// Read-only library inventory built on <see cref="IMediaInputClassifier"/>. It never
/// opens a file beyond the classifier's bounded header probe, never hashes content and
/// never writes, moves or deletes anything. Reparse points are skipped, not followed.
/// The reparse-point check and the later header open are not atomic, so a concurrent
/// process can still swap a path component between the two.
/// </summary>
public sealed class LibraryIndexer(IMediaInputClassifier classifier)
{
    public LibraryIndexer()
        : this(MediaInputClassifier.Shared)
    {
    }

    public async Task<LibraryIndexResult> IndexAsync(
        string rootPath,
        CancellationToken cancellationToken)
    {
        List<LibraryIndexEntry> entries = [];
        List<LibraryIndexIssue> issues = [];
        int directoriesVisited = 0;

        string fullRoot;

        try
        {
            fullRoot = Path.GetFullPath(rootPath.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return Rejected(rootPath, ex.Message);
        }

        if (!Directory.Exists(fullRoot)
            || IsReservedWorkspace(fullRoot)
            || !InputResolver.IsSafeExistingPath(fullRoot)
            || string.Equals(
                fullRoot,
                Path.GetPathRoot(fullRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            return Rejected(
                fullRoot,
                "Library root is missing, is a volume root, is an app workspace folder, or is or sits under a reparse point.");
        }

        Stack<string> pending = new();
        pending.Push(fullRoot);

        try
        {
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string directory = pending.Pop();
                directoriesVisited++;

                List<FileSystemInfo> children;

                try
                {
                    children = new DirectoryInfo(directory)
                        .EnumerateFileSystemInfos()
                        .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    issues.Add(new LibraryIndexIssue(
                        Relative(fullRoot, directory),
                        LibraryIndexIssueKind.DirectoryEnumerationFailed,
                        ex.Message));
                    continue;
                }

                List<string> subdirectories = [];

                foreach (FileSystemInfo child in children)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if ((child.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        issues.Add(new LibraryIndexIssue(
                            Relative(fullRoot, child.FullName),
                            LibraryIndexIssueKind.ReparsePointSkipped,
                            "Symbolic link, junction or other reparse point was not followed."));
                        continue;
                    }

                    if ((child.Attributes & FileAttributes.Directory) != 0)
                    {
                        // Same policy as InputResolver: app workspace folders are not library content.
                        if (!IsReservedWorkspace(child.FullName))
                        {
                            subdirectories.Add(child.FullName);
                        }

                        continue;
                    }

                    MediaInputDescriptor descriptor = await classifier
                        .ClassifyAsync(child.FullName, cancellationToken)
                        .ConfigureAwait(false);

                    AddFile(fullRoot, child, descriptor, entries, issues);
                }

                // Reverse so the stack pops directories in name order.
                for (int i = subdirectories.Count - 1; i >= 0; i--)
                {
                    pending.Push(subdirectories[i]);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return new LibraryIndexResult(
                fullRoot,
                LibraryIndexOutcome.Cancelled,
                entries,
                issues,
                directoriesVisited);
        }

        return new LibraryIndexResult(
            fullRoot,
            issues.Count == 0
                ? LibraryIndexOutcome.Completed
                : LibraryIndexOutcome.CompletedWithIssues,
            entries,
            issues,
            directoriesVisited);
    }

    private static void AddFile(
        string fullRoot,
        FileSystemInfo file,
        MediaInputDescriptor descriptor,
        List<LibraryIndexEntry> entries,
        List<LibraryIndexIssue> issues)
    {
        string relative = Relative(fullRoot, file.FullName);

        if (!descriptor.Exists || descriptor.IsDirectory)
        {
            bool unsafePath = descriptor.DetectionReason == "path-unsafe";

            issues.Add(new LibraryIndexIssue(
                relative,
                unsafePath
                    ? LibraryIndexIssueKind.ReparsePointSkipped
                    : LibraryIndexIssueKind.FileUnavailable,
                unsafePath
                    ? "Path traverses a reparse point and was rejected before probing."
                    : "File disappeared or became unreadable during enumeration."));
            return;
        }

        bool probeNotConfirmed = descriptor.ProbeStatus
            is not MediaInputProbeStatus.NotRequired
            and not MediaInputProbeStatus.MagicConfirmed
            and not MediaInputProbeStatus.HeaderEnvelopeValid;

        entries.Add(new LibraryIndexEntry(
            relative,
            file.Name,
            descriptor.Extension,
            descriptor.Kind,
            descriptor.SizeBytes,
            probeNotConfirmed
                ? LibraryIndexEntryStatus.HeaderNotConfirmed
                : LibraryIndexEntryStatus.Indexed,
            descriptor.ProbeStatus));
    }

    private static bool IsReservedWorkspace(string directoryPath) =>
        PendingWorkspacePathPolicy.IsReservedWorkspaceDirectoryName(
            Path.GetFileName(directoryPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)));

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path);

    private static LibraryIndexResult Rejected(string root, string message) =>
        new(
            root,
            LibraryIndexOutcome.Failed,
            [],
            [new LibraryIndexIssue(string.Empty, LibraryIndexIssueKind.RootRejected, message)],
            directoriesVisited: 0);
}
