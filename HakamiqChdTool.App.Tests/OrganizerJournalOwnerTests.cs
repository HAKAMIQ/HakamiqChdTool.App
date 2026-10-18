using HakamiqChdTool.App.Services.Library;
using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // An elevated token (GitHub Windows runners) gives new objects the default owner BUILTIN\Administrators,
    // not the current user. The journal must still own what it creates and keep rejecting what it did not.
    private static void TestOrganizerJournalOwnsNewDirectoryUnderDefaultOwner(string workDirectory) =>
        TestOrganizerJournalOwnsNewDirectoryUnderDefaultOwnerAsync(workDirectory).GetAwaiter().GetResult();

    private static async Task TestOrganizerJournalOwnsNewDirectoryUnderDefaultOwnerAsync(string workDirectory)
    {
        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User!;
        bool defaultOwnerDiffers = !DefaultOwnerIsCurrentUser(workDirectory, currentUser);

        string journalDirectory = Path.Combine(workDirectory, "organizer-owner-journal", "nested");
        string journalPath = Path.Combine(journalDirectory, "operations.jsonl");
        LibraryOperationJournal journal = new(journalPath);
        LibraryMoveOperation operation = SyntheticJournalOperation(workDirectory);
        await journal.AppendAsync(operation, Guid.NewGuid(), LibraryOperationState.IntentRecorded, null, default);
        await journal.AppendAsync(operation, Guid.NewGuid(), LibraryOperationState.Completed, null, default);

        AssertEqual(currentUser, OwnerOf(new DirectoryInfo(journalDirectory).GetAccessControl()),
            "A journal directory created by the journal is owned by the current user (default owner differs: " + defaultOwnerDiffers + ").");
        AssertEqual(currentUser, OwnerOf(new FileInfo(journalPath).GetAccessControl()),
            "A journal file created by the journal is owned by the current user (default owner differs: " + defaultOwnerDiffers + ").");
        LibraryOperationJournal.ValidateOwnerAcl(new DirectoryInfo(journalDirectory).GetAccessControl());
        LibraryOperationJournal.ValidateOwnerAcl(new FileInfo(journalPath).GetAccessControl());
        AssertEqual(2, (await journal.ReadAllAsync()).Count, "The journal reads back what it wrote into its own directory.");

        DirectorySecurity created = LibraryOperationJournal.CreateTrustedSecurity(new DirectorySecurity(),
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit);
        AssertEqual(currentUser, OwnerOf(created), "The creation descriptor names the current user as owner explicitly.");
        AssertTrue(created.AreAccessRulesProtected, "The creation descriptor does not inherit parent ACEs.");
    }

    private static void TestOrganizerJournalRejectsForeignOwnedDirectory(string workDirectory) =>
        TestOrganizerJournalRejectsForeignOwnedDirectoryAsync(workDirectory).GetAwaiter().GetResult();

    private static async Task TestOrganizerJournalRejectsForeignOwnedDirectoryAsync(string workDirectory)
    {
        SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User!;
        foreach (WellKnownSidType foreign in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.BuiltinUsersSid })
        {
            DirectorySecurity security = LibraryOperationJournal.CreateTrustedSecurity(new DirectorySecurity(), InheritanceFlags.None);
            security.SetOwner(new SecurityIdentifier(foreign, null));
            AssertThrows<UnauthorizedAccessException>(() => LibraryOperationJournal.ValidateOwnerAcl(security),
                "A journal directory owned by " + foreign + " is rejected.");
        }

        // On a machine whose default owner is not the current user, a directory that already exists
        // (not created by the journal) is foreign-owned on disk and must stay fail-closed.
        if (DefaultOwnerIsCurrentUser(workDirectory, currentUser)) return;
        string existing = Path.Combine(workDirectory, "organizer-foreign-journal");
        Directory.CreateDirectory(existing);
        LibraryOperationJournal journal = new(Path.Combine(existing, "operations.jsonl"));
        try
        {
            await journal.AppendAsync(SyntheticJournalOperation(workDirectory), Guid.NewGuid(),
                LibraryOperationState.IntentRecorded, null, default);
        }
        catch (UnauthorizedAccessException)
        {
            AssertTrue(!File.Exists(Path.Combine(existing, "operations.jsonl")), "A rejected directory receives no journal file.");
            AssertTrue(!currentUser.Equals((object?)OwnerOf(new DirectoryInfo(existing).GetAccessControl())),
                "A rejected pre-existing directory is not re-owned.");
            return;
        }
        throw new InvalidOperationException("A pre-existing journal directory with a foreign default owner must be rejected.");
    }

    private static bool DefaultOwnerIsCurrentUser(string workDirectory, SecurityIdentifier currentUser)
    {
        string probe = Path.Combine(workDirectory, "default-owner-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(probe);
        try
        {
            return currentUser.Equals((object?)OwnerOf(new DirectoryInfo(probe).GetAccessControl()));
        }
        finally
        {
            Directory.Delete(probe);
        }
    }

    private static SecurityIdentifier? OwnerOf(FileSystemSecurity security) =>
        security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;

    private static LibraryMoveOperation SyntheticJournalOperation(string workDirectory) =>
        new(Guid.NewGuid(), Path.Combine(workDirectory, "owner-library", "Game.iso"),
            Path.Combine(workDirectory, "owner-output", "Game.iso"), 4, new string('0', 64), null, true,
            LibraryOperationState.IntentRecorded);
}
