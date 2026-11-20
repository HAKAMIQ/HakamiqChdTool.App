using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Startup;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // The quick window opens only for an Explorer launch with a mode and paths while the setting is on; the setting
    // defaults to on and survives Clone and CopyFrom.
    private static void TestQuickWindowFollowsTheSetting()
    {
        AssertTrue(new AppSettings().ExplorerOpensQuickWindow, "The Explorer menu opens the quick window by default.");
        AssertFalse(new AppSettings { ExplorerOpensQuickWindow = false }.Clone().ExplorerOpensQuickWindow, "Clone keeps the setting.");
        var copy = new AppSettings();
        copy.CopyFrom(new AppSettings { ExplorerOpensQuickWindow = false });
        AssertFalse(copy.ExplorerOpensQuickWindow, "CopyFrom keeps the setting.");

        var on = new AppSettings { ExplorerOpensQuickWindow = true };
        var off = new AppSettings { ExplorerOpensQuickWindow = false };
        var quick = new LaunchRequest(LaunchMode.Convert, [@"C:\Games\a.iso"], Quick: true);
        AssertTrue(QuickLaunch.OpensQuickWindow(quick, on), "An Explorer launch opens the quick window.");
        AssertFalse(QuickLaunch.OpensQuickWindow(quick, off), "With the setting off, the main window opens.");
        AssertFalse(QuickLaunch.OpensQuickWindow(quick with { Quick = false }, on), "A command-line launch opens the main window.");
        AssertFalse(QuickLaunch.OpensQuickWindow(quick with { Mode = LaunchMode.None }, on), "Without a mode the main window opens.");
        AssertFalse(QuickLaunch.OpensQuickWindow(quick with { Paths = [] }, on), "Without paths the main window opens.");
        AssertFalse(QuickLaunch.OpensQuickWindow(LaunchRequest.Empty, on), "A plain start opens the main window.");
    }

    // The quick window waits for Start only when the run may delete the source.
    private static void TestQuickWindowWaitsWhenTheSourceMayBeDeleted()
    {
        var none = new AppSettings { DeleteSourceAfterVerifiedConversion = false, DeleteSourceAfterVerifiedExtraction = false, VerifyAfterConversion = true };
        AssertFalse(QuickLaunch.MayDeleteSource(LaunchMode.Convert, none), "Nothing is deleted: convert starts on its own.");
        AssertFalse(QuickLaunch.MayDeleteSource(LaunchMode.Extract, none), "Nothing is deleted: extract starts on its own.");

        var convertDelete = new AppSettings { DeleteSourceAfterVerifiedConversion = true, VerifyAfterConversion = true };
        AssertTrue(QuickLaunch.MayDeleteSource(LaunchMode.Convert, convertDelete), "Convert waits when the source is deleted after a verified conversion.");
        var unverified = new AppSettings { DeleteSourceAfterVerifiedConversion = true, VerifyAfterConversion = false };
        AssertFalse(QuickLaunch.MayDeleteSource(LaunchMode.Convert, unverified), "Without verification the conversion never deletes, so it starts on its own.");
        AssertFalse(QuickLaunch.MayDeleteSource(LaunchMode.Verify, convertDelete), "Verify never deletes.");

        var extractDelete = new AppSettings { DeleteSourceAfterVerifiedExtraction = true };
        AssertTrue(QuickLaunch.MayDeleteSource(LaunchMode.Extract, extractDelete), "Extract waits when the CHD is deleted after a proven extraction.");
        AssertFalse(QuickLaunch.MayDeleteSource(LaunchMode.Convert, extractDelete), "The extraction setting does not affect conversion.");
        AssertFalse(QuickLaunch.MayDeleteSource(LaunchMode.Verify, extractDelete), "Verify never deletes.");
    }
}
