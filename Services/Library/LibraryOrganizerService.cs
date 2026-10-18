using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Core.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services.Library;

public enum LibraryOperationState
{
    Planned,
    NeedsReview,
    IntentRecorded,
    Completed,
    Failed,
    Conflict,
    UndoPending,
    Undone,
    RecoveryRequired,
    UnsupportedCrossVolume
}

public sealed record LibraryMoveOperation(
    Guid OperationId,
    string SourcePath,
    string DestinationPath,
    long FileSize,
    string Sha256,
    string? GroupId,
    bool Eligible,
    LibraryOperationState State,
    string? ErrorCode = null,
    string? LibraryRoot = null,
    string? OutputRoot = null);

public sealed record LibraryMovePlan(
    Guid PlanId,
    string LibraryRoot,
    string OutputRoot,
    IReadOnlyList<LibraryMoveOperation> Operations);

public sealed record LibraryOperationResult(
    Guid OperationId,
    LibraryOperationState State,
    string? ErrorCode = null);

/// <summary>
/// Converts the existing read-only organization preview into a content-bound plan.
/// Only planner-approved items are eligible; all other rows remain visible as review rows.
/// </summary>
public sealed class LibraryPlanValidator
{
    public async Task<LibraryMovePlan> CreateAsync(
        OrganizationPlan organizationPlan,
        string libraryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organizationPlan);
        if (organizationPlan.Status != PlanStatus.Ready)
        {
            throw new InvalidOperationException("A rejected organization preview cannot be executed.");
        }

        string sourceRoot = Path.GetFullPath(libraryRoot);
        string outputRoot = Path.GetFullPath(organizationPlan.OutputRoot);
        EnsureSafeDirectory(sourceRoot);
        EnsureSafeExistingAncestor(outputRoot);
        if (LibPathRelation.IsSameOrInside(sourceRoot, outputRoot)
            || LibPathRelation.IsSameOrInside(outputRoot, sourceRoot))
        {
            throw new InvalidOperationException("Library and output roots must be disjoint.");
        }

        List<LibraryMoveOperation> operations = [];
        HashSet<string> destinations = new(StringComparer.OrdinalIgnoreCase);
        foreach (PlanItem item in organizationPlan.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool eligible = item.IsSafeToApply;
            string source = ResolveContainedPath(sourceRoot, item.SourceRelativePath);
            string destination = ResolveContainedPath(outputRoot, item.DestinationRelativePath);
            string? error = null;
            long size = 0;
            string hash = string.Empty;
            LibraryOperationState state = eligible ? LibraryOperationState.Planned : LibraryOperationState.NeedsReview;

            if (!destinations.Add(destination))
            {
                eligible = false;
                state = LibraryOperationState.Conflict;
                error = "destination-collision";
            }
            else if (eligible)
            {
                ValidatePath(sourceRoot, source, expectFile: true);
                ValidatePath(outputRoot, destination, expectFile: false);
                if (File.Exists(destination) || Directory.Exists(destination))
                {
                    eligible = false;
                    state = LibraryOperationState.Conflict;
                    error = "destination-exists";
                }
                else
                {
                    FileInfo info = new(source);
                    size = info.Length;
                    hash = await HashFileAsync(source, cancellationToken).ConfigureAwait(false);
                    ValidatePath(sourceRoot, source, expectFile: true);
                }
            }

            operations.Add(new LibraryMoveOperation(
                Guid.NewGuid(), source, destination, size, hash, item.DiscSetId, eligible, state, error,
                sourceRoot, outputRoot));
        }

        MarkGroupProblems(operations);
        return new LibraryMovePlan(Guid.NewGuid(), sourceRoot, outputRoot, operations);
    }

    internal static string ResolveContainedPath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathFullyQualified(relativePath))
        {
            throw new InvalidOperationException("Plan contains an empty or rooted relative path.");
        }

        string[] components = relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        if (components.Any(component => component is "" or "." or ".."
            || component.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || component.EndsWith(' ') || component.EndsWith('.')))
        {
            throw new InvalidOperationException("Plan contains an unsafe path component.");
        }

        string full = Path.GetFullPath(Path.Combine(root, relativePath));
        string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Plan path escapes its approved root.");
        }

        return full;
    }

    internal static void ValidatePath(string root, string path, bool expectFile)
    {
        string safeRoot = root;
        while (!Directory.Exists(safeRoot))
        {
            safeRoot = Path.GetDirectoryName(safeRoot)
                ?? throw new IOException("Approved root has no existing directory ancestor.");
        }
        if (!InputResolver.IsSafeExistingPath(safeRoot))
        {
            throw new IOException("An approved root is missing or traverses a reparse point.");
        }

        string full = Path.GetFullPath(path);
        string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Path is outside its approved root.");
        }

        string? cursor = full;
        while (cursor is not null && !string.Equals(cursor, root, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(cursor) || Directory.Exists(cursor))
            {
                if (!InputResolver.IsSafeExistingPath(cursor))
                {
                    throw new IOException("Path traverses a reparse point.");
                }
            }
            cursor = Path.GetDirectoryName(cursor);
        }

        if (expectFile && (!File.Exists(full) || Directory.Exists(full)))
        {
            throw new FileNotFoundException("Expected source file is missing or is not a file.", full);
        }
    }

    internal static void EnsureSafeDirectory(string path)
    {
        if (!Directory.Exists(path) || !InputResolver.IsSafeExistingPath(path))
        {
            throw new IOException("Required directory is missing or traverses a reparse point.");
        }
    }

    private static void EnsureSafeExistingAncestor(string path)
    {
        string? cursor = path;
        while (cursor is not null && !Directory.Exists(cursor))
        {
            cursor = Path.GetDirectoryName(cursor);
        }
        if (cursor is null)
        {
            throw new IOException("Output root has no existing directory ancestor.");
        }
        EnsureSafeDirectory(cursor);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static void MarkGroupProblems(List<LibraryMoveOperation> operations)
    {
        foreach (IGrouping<string, LibraryMoveOperation> group in operations
            .Where(operation => operation.GroupId is not null)
            .GroupBy(operation => operation.GroupId!, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Any(operation => !operation.Eligible || operation.State != LibraryOperationState.Planned))
            {
                HashSet<Guid> ids = group.Select(operation => operation.OperationId).ToHashSet();
                for (int i = 0; i < operations.Count; i++)
                {
                    if (ids.Contains(operations[i].OperationId))
                    {
                        operations[i] = operations[i] with
                        {
                            Eligible = false,
                            State = LibraryOperationState.NeedsReview,
                            ErrorCode = operations[i].ErrorCode ?? "disc-set-member-not-eligible"
                        };
                    }
                }
            }
        }
    }
}

/// <summary>Append-only JSON-lines operation journal with a durable flush per state transition.</summary>
public sealed class LibraryOperationJournal
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LibraryOperationJournal(string? path = null)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string defaultRoot = Path.Combine(appData, "HakamiqChdTool", "Organizer");
        _path = Path.GetFullPath(path ?? Path.Combine(defaultRoot, "operations.jsonl"));
        if (!IsUnder(_path, path is null ? defaultRoot : Path.GetDirectoryName(_path)!))
        {
            throw new ArgumentException("Journal path is not contained in its owner directory.", nameof(path));
        }
    }

    public string PathName => _path;

    public async Task AppendAsync(LibraryMoveOperation operation, Guid planId, LibraryOperationState state,
        string? errorCode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        string directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        LibraryPlanValidator.EnsureSafeDirectory(directory);
        SecureOwnerAcl(new DirectoryInfo(directory));
        if (File.Exists(_path) && !InputResolver.IsSafeExistingPath(_path))
        {
            throw new IOException("Organizer journal is a reparse point or is unsafe.");
        }
        if (File.Exists(_path)) ValidateOwnerAcl(new FileInfo(_path).GetAccessControl());

        LibraryJournalEvent entry = new(operation.OperationId, planId, operation.SourcePath,
            operation.DestinationPath, operation.FileSize, operation.Sha256, operation.GroupId,
            DateTimeOffset.UtcNow, state, errorCode, operation.LibraryRoot, operation.OutputRoot);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(entry);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using FileStream stream = new(_path, FileMode.Append, FileAccess.Write, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<LibraryJournalEvent>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return [];
        if (!InputResolver.IsSafeExistingPath(_path)) throw new IOException("Organizer journal is unsafe.");
        ValidateOwnerAcl(new FileInfo(_path).GetAccessControl());
        List<LibraryJournalEvent> events = [];
        await using FileStream stream = new(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using StreamReader reader = new(stream);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.Length > 64 * 1024) throw new InvalidDataException("Organizer journal record exceeds its size limit.");
            events.Add(JsonSerializer.Deserialize<LibraryJournalEvent>(line)
                ?? throw new InvalidDataException("Organizer journal contains an empty record."));
            if (events.Count > 1_000_000) throw new InvalidDataException("Organizer journal exceeds its record limit.");
        }
        return events;
    }

    private static bool IsUnder(string path, string root)
    {
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateOwnerAcl(FileSystemSecurity security)
    {
        SecurityIdentifier owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier
            ?? throw new UnauthorizedAccessException("Organizer journal owner could not be established.");
        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new UnauthorizedAccessException("Current Windows user identity is unavailable.");
        if (!owner.Equals(currentUser))
            throw new UnauthorizedAccessException("Organizer journal directory or file is not owned by the current user.");

        SecurityIdentifier system = new(WellKnownSidType.LocalSystemSid, null);
        SecurityIdentifier administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
        const FileSystemRights writeRights = FileSystemRights.Write | FileSystemRights.Modify
            | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
            | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        AuthorizationRuleCollection rules = security.GetAccessRules(includeExplicit: true, includeInherited: true,
            targetType: typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType != AccessControlType.Allow || (rule.FileSystemRights & writeRights) == 0)
                continue;
            SecurityIdentifier identity = (SecurityIdentifier)rule.IdentityReference;
            if (!identity.Equals(currentUser) && !identity.Equals(system) && !identity.Equals(administrators))
                throw new UnauthorizedAccessException("Organizer journal grants write access to an untrusted Windows identity.");
        }
    }

    private static void SecureOwnerAcl(DirectoryInfo directory)
    {
        DirectorySecurity security = directory.GetAccessControl();
        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new UnauthorizedAccessException("Current Windows user identity is unavailable.");
        SecurityIdentifier owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier
            ?? throw new UnauthorizedAccessException("Organizer journal directory owner could not be established.");
        if (!owner.Equals(currentUser))
            throw new UnauthorizedAccessException("Organizer journal directory is not owned by the current user.");

        foreach (IdentityReference identity in security.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .OfType<FileSystemAccessRule>().Select(rule => rule.IdentityReference).Distinct())
        {
            security.PurgeAccessRules(identity);
        }

        SecurityIdentifier system = new(WellKnownSidType.LocalSystemSid, null);
        SecurityIdentifier administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
        InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(currentUser, FileSystemRights.FullControl, inheritance,
            PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, inheritance,
            PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(administrators, FileSystemRights.FullControl, inheritance,
            PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
        ValidateOwnerAcl(directory.GetAccessControl());
    }
}

public sealed record LibraryJournalEvent(
    Guid OperationId,
    Guid PlanId,
    string SourcePath,
    string DestinationPath,
    long FileSize,
    string Sha256,
    string? GroupId,
    DateTimeOffset TimestampUtc,
    LibraryOperationState State,
    string? ErrorCode,
    string? LibraryRoot,
    string? OutputRoot);

/// <summary>Executes only validated same-volume moves and records intent before changing paths.</summary>
public sealed class LibraryOrganizerService(LibraryOperationJournal journal)
{
    public async Task<IReadOnlyList<LibraryOperationResult>> ExecuteAsync(
        LibraryMovePlan plan,
        IReadOnlySet<Guid> selectedOperationIds,
        bool confirmed,
        IProgress<(int Completed, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(selectedOperationIds);
        if (!confirmed) throw new InvalidOperationException("Explicit user confirmation is required.");
        List<LibraryMoveOperation> selected = plan.Operations
            .Where(operation => selectedOperationIds.Contains(operation.OperationId))
            .ToList();
        if (selected.Count != selectedOperationIds.Count || selected.Any(operation => !operation.Eligible))
        {
            throw new InvalidOperationException("Selection contains an unknown or review-only operation.");
        }

        foreach (IGrouping<string, LibraryMoveOperation> group in selected
            .Where(operation => operation.GroupId is not null)
            .GroupBy(operation => operation.GroupId!, StringComparer.OrdinalIgnoreCase))
        {
            int totalMembers = plan.Operations.Count(operation => string.Equals(operation.GroupId, group.Key, StringComparison.OrdinalIgnoreCase));
            if (group.Count() != totalMembers) throw new InvalidOperationException("A multi-file disc set must be selected as a complete group.");
        }

        List<LibraryOperationResult> results = [];
        HashSet<Guid> completed = [];
        for (int index = 0; index < selected.Count; index++)
        {
            LibraryMoveOperation operation = selected[index];
            if (cancellationToken.IsCancellationRequested)
            {
                _ = await RollBackGroupAsync(plan, operation, selected, completed).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            try
            {
                if (!AreOnSameVolume(operation.SourcePath, operation.DestinationPath))
                {
                    await journal.AppendAsync(operation, plan.PlanId, LibraryOperationState.UnsupportedCrossVolume,
                        "cross-volume", CancellationToken.None).ConfigureAwait(false);
                    results.Add(new(operation.OperationId, LibraryOperationState.UnsupportedCrossVolume, "cross-volume"));
                    HashSet<Guid> rolledBack = await RollBackGroupAsync(plan, operation, selected, completed).ConfigureAwait(false);
                    MarkRolledBackResults(results, rolledBack);
                    continue;
                }

                ValidateForMove(plan, operation);
                string actualHash = await HashFileAsync(operation.SourcePath, cancellationToken).ConfigureAwait(false);
                if (new FileInfo(operation.SourcePath).Length != operation.FileSize
                    || !string.Equals(actualHash, operation.Sha256, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("source-changed-after-preview");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(operation.DestinationPath)!);
                ValidateForMove(plan, operation);
                if (!AreOnSameVolume(operation.SourcePath, operation.DestinationPath))
                    throw new IOException("cross-volume");
                await journal.AppendAsync(operation, plan.PlanId, LibraryOperationState.IntentRecorded,
                    null, CancellationToken.None).ConfigureAwait(false);
                File.Move(operation.SourcePath, operation.DestinationPath, overwrite: false);
                string movedHash = await HashFileAsync(operation.DestinationPath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(movedHash, operation.Sha256, StringComparison.Ordinal)
                    || new FileInfo(operation.DestinationPath).Length != operation.FileSize)
                {
                    await journal.AppendAsync(operation, plan.PlanId, LibraryOperationState.RecoveryRequired,
                        "destination-verification-failed", CancellationToken.None).ConfigureAwait(false);
                    results.Add(new(operation.OperationId, LibraryOperationState.RecoveryRequired, "destination-verification-failed"));
                    HashSet<Guid> rolledBack = await RollBackGroupAsync(plan, operation, selected, completed).ConfigureAwait(false);
                    MarkRolledBackResults(results, rolledBack);
                    continue;
                }

                await journal.AppendAsync(operation, plan.PlanId, LibraryOperationState.Completed,
                    null, CancellationToken.None).ConfigureAwait(false);
                completed.Add(operation.OperationId);
                results.Add(new(operation.OperationId, LibraryOperationState.Completed));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _ = await RollBackGroupAsync(plan, operation, selected, completed).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                LibraryOperationState state = ex is IOException && (File.Exists(operation.DestinationPath) || Directory.Exists(operation.DestinationPath))
                    ? LibraryOperationState.Conflict : LibraryOperationState.Failed;
                string code = ex is InvalidDataException ? "source-changed-after-preview" : ex.GetType().Name;
                await journal.AppendAsync(operation, plan.PlanId, state, code, CancellationToken.None).ConfigureAwait(false);
                results.Add(new(operation.OperationId, state, code));
                HashSet<Guid> rolledBack = await RollBackGroupAsync(plan, operation, selected, completed).ConfigureAwait(false);
                MarkRolledBackResults(results, rolledBack);
            }
            progress?.Report((index + 1, selected.Count));
        }
        return results;
    }

    private static void MarkRolledBackResults(List<LibraryOperationResult> results, HashSet<Guid> rolledBack)
    {
        for (int resultIndex = 0; resultIndex < results.Count; resultIndex++)
        {
            if (rolledBack.Contains(results[resultIndex].OperationId))
                results[resultIndex] = new(results[resultIndex].OperationId, LibraryOperationState.Undone, "group-rollback");
        }
    }

    private static void ValidateForMove(LibraryMovePlan plan, LibraryMoveOperation operation)
    {
        LibraryPlanValidator.ValidatePath(plan.LibraryRoot, operation.SourcePath, expectFile: true);
        LibraryPlanValidator.ValidatePath(plan.OutputRoot, operation.DestinationPath, expectFile: false);
        if (File.Exists(operation.DestinationPath) || Directory.Exists(operation.DestinationPath))
            throw new IOException("destination-exists");
    }

    private async Task<HashSet<Guid>> RollBackGroupAsync(LibraryMovePlan plan, LibraryMoveOperation failed,
        IReadOnlyList<LibraryMoveOperation> selected, HashSet<Guid> completed)
    {
        HashSet<Guid> rolledBack = [];
        IEnumerable<LibraryMoveOperation> rollback = selected
            .Where(operation => (completed.Contains(operation.OperationId)
                    || operation.OperationId == failed.OperationId && File.Exists(operation.DestinationPath)
                        && !File.Exists(operation.SourcePath))
                && (failed.GroupId is null
                    ? operation.OperationId == failed.OperationId
                    : string.Equals(operation.GroupId, failed.GroupId, StringComparison.OrdinalIgnoreCase)))
            .Reverse();
        foreach (LibraryMoveOperation operation in rollback)
        {
            try
            {
                if (!File.Exists(operation.DestinationPath) || File.Exists(operation.SourcePath)
                    || !SameHash(operation.DestinationPath, operation.Sha256))
                    throw new IOException("rollback-precondition-failed");
                LibraryPlanValidator.ValidatePath(plan.OutputRoot, operation.DestinationPath, expectFile: true);
                LibraryPlanValidator.ValidatePath(plan.LibraryRoot, operation.SourcePath, expectFile: false);
                Directory.CreateDirectory(Path.GetDirectoryName(operation.SourcePath)!);
                await journal.AppendAsync(operation, plan.PlanId, LibraryOperationState.UndoPending,
                    "group-rollback", CancellationToken.None).ConfigureAwait(false);
                File.Move(operation.DestinationPath, operation.SourcePath, overwrite: false);
                await journal.AppendAsync(operation, plan.PlanId, LibraryOperationState.Undone,
                    "group-rollback", CancellationToken.None).ConfigureAwait(false);
                completed.Remove(operation.OperationId);
                rolledBack.Add(operation.OperationId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await journal.AppendAsync(operation, plan.PlanId, LibraryOperationState.RecoveryRequired,
                    "group-rollback-failed", CancellationToken.None).ConfigureAwait(false);
            }
        }
        return rolledBack;
    }

    internal static bool AreOnSameVolume(string left, string right) =>
        TryGetVolumeIdentity(left, out string leftVolume)
        && TryGetVolumeIdentity(right, out string rightVolume)
        && string.Equals(leftVolume, rightVolume, StringComparison.OrdinalIgnoreCase);

    private static bool TryGetVolumeIdentity(string path, out string volumeIdentity)
    {
        volumeIdentity = string.Empty;
        StringBuilder mountPath = new(32768);
        string fullPath = Path.GetFullPath(path);
        if (!GetVolumePathNameW(fullPath, mountPath, (uint)mountPath.Capacity)) return false;

        StringBuilder volumeName = new(128);
        string mount = Path.EndsInDirectorySeparator(mountPath.ToString())
            ? mountPath.ToString()
            : mountPath + Path.DirectorySeparatorChar.ToString();
        if (!GetVolumeNameForVolumeMountPointW(mount, volumeName, (uint)volumeName.Capacity)) return false;
        volumeIdentity = volumeName.ToString();
        return volumeIdentity.Length > 0;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNameW(string fileName, StringBuilder volumePathName, uint bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPointW(string volumeMountPoint, StringBuilder volumeName, uint bufferLength);

    internal static bool SameHash(string path, string expected) =>
        string.Equals(HashFileAsync(path, CancellationToken.None).GetAwaiter().GetResult(), expected, StringComparison.Ordinal);

    internal static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }
}

public sealed class LibraryUndoService(LibraryOperationJournal journal)
{
    public async Task<IReadOnlyList<LibraryOperationResult>> RecoverAsync(Guid? planId = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LibraryJournalEvent> events = await journal.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        List<LibraryOperationResult> results = [];
        foreach (LibraryJournalEvent entry in Latest(events).Values.Where(entry =>
                     (entry.State is LibraryOperationState.IntentRecorded or LibraryOperationState.UndoPending)
                     && (planId is null || entry.PlanId == planId)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            LibraryOperationState state;
            string? error = null;
            try
            {
                if (entry.LibraryRoot is null || entry.OutputRoot is null
                    || !LibraryOrganizerService.AreOnSameVolume(entry.SourcePath, entry.DestinationPath))
                    throw new IOException("Journal entry has no safe roots or crosses volumes.");
                LibraryPlanValidator.ValidatePath(entry.LibraryRoot, entry.SourcePath, expectFile: File.Exists(entry.SourcePath));
                LibraryPlanValidator.ValidatePath(entry.OutputRoot, entry.DestinationPath, expectFile: File.Exists(entry.DestinationPath));
                bool sourceMatches = File.Exists(entry.SourcePath)
                    && new FileInfo(entry.SourcePath).Length == entry.FileSize
                    && LibraryOrganizerService.SameHash(entry.SourcePath, entry.Sha256);
                bool destinationMatches = File.Exists(entry.DestinationPath)
                    && new FileInfo(entry.DestinationPath).Length == entry.FileSize
                    && LibraryOrganizerService.SameHash(entry.DestinationPath, entry.Sha256);

                if (entry.State == LibraryOperationState.IntentRecorded && destinationMatches && !File.Exists(entry.SourcePath))
                    state = LibraryOperationState.Completed;
                else if (entry.State == LibraryOperationState.IntentRecorded && sourceMatches && !File.Exists(entry.DestinationPath))
                {
                    state = LibraryOperationState.Failed;
                    error = "recovered-before-move";
                }
                else if (entry.State == LibraryOperationState.UndoPending && sourceMatches && !File.Exists(entry.DestinationPath))
                    state = LibraryOperationState.Undone;
                else if (entry.State == LibraryOperationState.UndoPending && destinationMatches && !File.Exists(entry.SourcePath))
                    state = LibraryOperationState.Completed;
                else
                {
                    state = LibraryOperationState.RecoveryRequired;
                    error = "recovery-state-ambiguous";
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                state = LibraryOperationState.RecoveryRequired;
                error = ex.GetType().Name;
            }

            await journal.AppendAsync(ToOperation(entry), entry.PlanId, state, error, CancellationToken.None).ConfigureAwait(false);
            results.Add(new(entry.OperationId, state, error));
        }
        return results;
    }

    public async Task<IReadOnlyList<LibraryOperationResult>> PreviewUndoAsync(Guid? planId = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LibraryJournalEvent> events = await journal.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return Latest(events).Where(pair => pair.Value.State == LibraryOperationState.Completed
                && (planId is null || pair.Value.PlanId == planId))
            .Select(pair => AssessUndo(pair.Value)).ToList();
    }

    public async Task<IReadOnlyList<LibraryOperationResult>> UndoAsync(Guid? planId = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LibraryJournalEvent> events = await journal.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        List<LibraryJournalEvent> completed = Latest(events).Values
            .Where(entry => entry.State == LibraryOperationState.Completed && (planId is null || entry.PlanId == planId))
            .OrderByDescending(entry => entry.TimestampUtc).ToList();
        List<LibraryOperationResult> results = [];
        foreach (IGrouping<string, LibraryJournalEvent> group in completed.GroupBy(entry =>
                     entry.GroupId is null ? "operation:" + entry.OperationId : "group:" + entry.GroupId,
                     StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            LibraryJournalEvent[] members = group.ToArray();
            LibraryOperationResult[] assessments = members.Select(AssessUndo).ToArray();
            if (assessments.Any(assessment => assessment.State != LibraryOperationState.Completed))
            {
                foreach (LibraryOperationResult assessment in assessments)
                {
                    await journal.AppendAsync(ToOperation(members.Single(entry => entry.OperationId == assessment.OperationId)),
                        members[0].PlanId, LibraryOperationState.Conflict, assessment.ErrorCode, CancellationToken.None).ConfigureAwait(false);
                    results.Add(assessment with { State = LibraryOperationState.Conflict });
                }
                continue;
            }

            List<LibraryJournalEvent> undone = [];
            foreach (LibraryJournalEvent entry in members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LibraryMoveOperation operation = ToOperation(entry);
                try
                {
                    LibraryPlanValidator.ValidatePath(entry.OutputRoot!, entry.DestinationPath, expectFile: true);
                    LibraryPlanValidator.ValidatePath(entry.LibraryRoot!, entry.SourcePath, expectFile: false);
                    if (!LibraryOrganizerService.AreOnSameVolume(entry.SourcePath, entry.DestinationPath))
                        throw new IOException("cross-volume-journal-entry");
                    await journal.AppendAsync(operation, entry.PlanId, LibraryOperationState.UndoPending,
                        null, CancellationToken.None).ConfigureAwait(false);
                    File.Move(entry.DestinationPath, entry.SourcePath, overwrite: false);
                    undone.Add(entry);
                    if (new FileInfo(entry.SourcePath).Length != entry.FileSize
                        || !LibraryOrganizerService.SameHash(entry.SourcePath, entry.Sha256))
                        throw new InvalidDataException("undo-verification-failed");
                    await journal.AppendAsync(operation, entry.PlanId, LibraryOperationState.Undone,
                        null, CancellationToken.None).ConfigureAwait(false);
                    results.Add(new(entry.OperationId, LibraryOperationState.Undone));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _ = await RestoreUndoneGroupAsync(undone, "undo-cancelled").ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
                {
                    HashSet<Guid> restored = await RestoreUndoneGroupAsync(undone, "undo-group-rollback").ConfigureAwait(false);
                    for (int resultIndex = 0; resultIndex < results.Count; resultIndex++)
                    {
                        if (undone.Any(member => member.OperationId == results[resultIndex].OperationId))
                            results[resultIndex] = results[resultIndex] with
                            {
                                State = restored.Contains(results[resultIndex].OperationId)
                                    ? LibraryOperationState.Completed : LibraryOperationState.RecoveryRequired,
                                ErrorCode = "undo-group-rollback"
                            };
                    }
                    await journal.AppendAsync(operation, entry.PlanId, LibraryOperationState.Conflict,
                        ex is InvalidDataException ? "undo-verification-failed" : ex.GetType().Name, CancellationToken.None).ConfigureAwait(false);
                    results.Add(new(entry.OperationId, LibraryOperationState.Conflict,
                        ex is InvalidDataException ? "undo-verification-failed" : ex.GetType().Name));
                    break;
                }
            }
        }
        return results;
    }

    private async Task<HashSet<Guid>> RestoreUndoneGroupAsync(IReadOnlyList<LibraryJournalEvent> undone, string reason)
    {
        HashSet<Guid> restored = [];
        foreach (LibraryJournalEvent entry in undone.Reverse())
        {
            LibraryMoveOperation operation = ToOperation(entry);
            try
            {
                if (!File.Exists(entry.SourcePath) || File.Exists(entry.DestinationPath)
                    || !LibraryOrganizerService.SameHash(entry.SourcePath, entry.Sha256))
                    throw new IOException("undo-rollback-precondition-failed");
                LibraryPlanValidator.ValidatePath(entry.LibraryRoot!, entry.SourcePath, expectFile: true);
                LibraryPlanValidator.ValidatePath(entry.OutputRoot!, entry.DestinationPath, expectFile: false);
                await journal.AppendAsync(operation, entry.PlanId, LibraryOperationState.UndoPending,
                    reason, CancellationToken.None).ConfigureAwait(false);
                File.Move(entry.SourcePath, entry.DestinationPath, overwrite: false);
                await journal.AppendAsync(operation, entry.PlanId, LibraryOperationState.Completed,
                    reason, CancellationToken.None).ConfigureAwait(false);
                restored.Add(entry.OperationId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                await journal.AppendAsync(operation, entry.PlanId, LibraryOperationState.RecoveryRequired,
                    "undo-group-rollback-failed", CancellationToken.None).ConfigureAwait(false);
            }
        }
        return restored;
    }

    private static LibraryOperationResult AssessUndo(LibraryJournalEvent entry)
    {
        if (entry.LibraryRoot is null || entry.OutputRoot is null
            || !LibraryOrganizerService.AreOnSameVolume(entry.SourcePath, entry.DestinationPath))
            return new(entry.OperationId, LibraryOperationState.Conflict, "cross-volume-journal-entry");
        if (!File.Exists(entry.DestinationPath)) return new(entry.OperationId, LibraryOperationState.Conflict, "destination-missing");
        if (File.Exists(entry.SourcePath) || Directory.Exists(entry.SourcePath)) return new(entry.OperationId, LibraryOperationState.Conflict, "source-occupied");
        try
        {
            LibraryPlanValidator.ValidatePath(entry.OutputRoot, entry.DestinationPath, expectFile: true);
            LibraryPlanValidator.ValidatePath(entry.LibraryRoot, entry.SourcePath, expectFile: false);
            if (new FileInfo(entry.DestinationPath).Length != entry.FileSize || !LibraryOrganizerService.SameHash(entry.DestinationPath, entry.Sha256))
                return new(entry.OperationId, LibraryOperationState.Conflict, "destination-changed");
            return new(entry.OperationId, LibraryOperationState.Completed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(entry.OperationId, LibraryOperationState.Conflict, ex.GetType().Name);
        }
    }

    private static Dictionary<Guid, LibraryJournalEvent> Latest(IReadOnlyList<LibraryJournalEvent> events)
    {
        Dictionary<Guid, LibraryJournalEvent> latest = [];
        foreach (LibraryJournalEvent entry in events) latest[entry.OperationId] = entry;
        return latest;
    }

    private static LibraryMoveOperation ToOperation(LibraryJournalEvent entry) => new(
        entry.OperationId, entry.SourcePath, entry.DestinationPath, entry.FileSize, entry.Sha256,
        entry.GroupId, true, entry.State, entry.ErrorCode, entry.LibraryRoot, entry.OutputRoot);
}
