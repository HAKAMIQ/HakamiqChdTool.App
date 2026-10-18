using HakamiqChdTool.App.Core.Workflow;
using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // An unreadable subtree must make the reparse scan report "unsafe" (fail closed), because the caller
    // deletes the whole job tree when the scan reports clean.
    private static void TestPendingCleanerReparseScanFailsClosedOnUnreadableSubtree(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "pending-failclosed-" + Guid.NewGuid().ToString("N"));
        string locked = Path.Combine(root, "locked");
        Directory.CreateDirectory(locked);
        File.WriteAllBytes(Path.Combine(locked, "inner.bin"), [1]);
        File.WriteAllBytes(Path.Combine(root, "plain.bin"), [1]);

        AssertFalse(WorkflowPendingOutputCleaner.ContainsReparsePoint(root), "A readable plain tree reports no reparse point.");

        SecurityIdentifier me = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("No current user SID.");
        var denyRule = new FileSystemAccessRule(
            me,
            FileSystemRights.ListDirectory | FileSystemRights.ReadData,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Deny);

        var lockedInfo = new DirectoryInfo(locked);
        DirectorySecurity security = lockedInfo.GetAccessControl();
        security.AddAccessRule(denyRule);
        try
        {
            lockedInfo.SetAccessControl(security);
        }
        catch (UnauthorizedAccessException ex)
        {
            Directory.Delete(root, recursive: true);
            throw new TestSkippedException("This account cannot set the ACL needed to create an unreadable subtree: " + ex.Message);
        }

        try
        {
            AssertTrue(
                WorkflowPendingOutputCleaner.ContainsReparsePoint(root),
                "An unreadable subtree must be treated as unsafe, not skipped.");
        }
        finally
        {
            security = lockedInfo.GetAccessControl();
            security.RemoveAccessRule(denyRule);
            lockedInfo.SetAccessControl(security);
            Directory.Delete(root, recursive: true);
        }
    }
}
