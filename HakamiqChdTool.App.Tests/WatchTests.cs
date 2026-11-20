using HakamiqChdTool.App.Services.Watch;
using System.IO;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // A file is reported once, only after its size stays the same for the quiet period and no one is writing it.
    private static void TestWatchStabilityTracker()
    {
        var tracker = new FileStabilityTracker(TimeSpan.FromSeconds(5));
        DateTime t0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        long size = 100;
        bool locked = false;
        bool exists = true;
        long? SizeOf(string _) => exists ? size : null;
        bool CanOpen(string _) => !locked;

        tracker.Observe(@"C:\w\a.iso", t0);
        AssertEqual(0, tracker.TakeReady(t0, SizeOf, CanOpen).Count, "The first look only records the size.");
        size = 200;
        AssertEqual(0, tracker.TakeReady(t0.AddSeconds(6), SizeOf, CanOpen).Count, "A growing file is not ready.");
        AssertEqual(0, tracker.TakeReady(t0.AddSeconds(9), SizeOf, CanOpen).Count, "The quiet period restarts after a change.");
        locked = true;
        AssertEqual(0, tracker.TakeReady(t0.AddSeconds(12), SizeOf, CanOpen).Count, "A file another program holds is not ready.");
        locked = false;
        IReadOnlyList<string> ready = tracker.TakeReady(t0.AddSeconds(13), SizeOf, CanOpen);
        AssertEqual(1, ready.Count, "A quiet, free file is ready.");
        tracker.Observe(@"C:\w\a.iso", t0.AddSeconds(14));
        AssertEqual(0, tracker.TakeReady(t0.AddSeconds(30), SizeOf, CanOpen).Count, "A reported file is not reported again.");
        tracker.Forget(@"C:\w\a.iso");
        tracker.Observe(@"C:\w\a.iso", t0.AddSeconds(31));
        tracker.TakeReady(t0.AddSeconds(31), SizeOf, CanOpen);
        AssertEqual(1, tracker.TakeReady(t0.AddSeconds(40), SizeOf, CanOpen).Count, "A file deleted and added again is reported again.");

        tracker.Observe(@"C:\w\b.iso", t0);
        exists = false;
        tracker.TakeReady(t0.AddSeconds(1), SizeOf, CanOpen);
        AssertEqual(0, tracker.PendingCount, "A file that disappears is dropped.");
        exists = true;
        size = 0;
        tracker.Observe(@"C:\w\c.iso", t0);
        tracker.TakeReady(t0, SizeOf, CanOpen);
        AssertEqual(0, tracker.TakeReady(t0.AddSeconds(60), SizeOf, CanOpen).Count, "An empty file is never ready.");

        foreach ((string name, bool watched) in new[]
        {
            ("Game.iso", true), ("Game.cue", true), ("Set.zip", true), ("Game.chd", false), ("notes.txt", false),
            ("~Game.iso", false), (".Game.iso", false), ("Game.iso.part", false), ("Game.zip.crdownload", false), ("Game.iso.tmp", false)
        })
        {
            AssertEqual(watched, WatchFolderService.IsWatchedInput(Path.Combine(@"C:\w", name)), $"{name} watched: {watched}.");
        }
    }

    // Real FileSystemWatcher: a file still open for writing is not reported; once closed it is, once; CHD files
    // and files in subfolders are ignored; a missing folder cannot be watched.
    private static void TestWatchFolderWithRealFiles()
    {
        AssertTrue(WatchFolderService.TryStart(Path.Combine(Path.GetTempPath(), "hct-missing-" + Guid.NewGuid().ToString("N")), _ => { }) is null, "A missing folder is not watched.");

        string root = Path.Combine(Path.GetTempPath(), "hct-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        var reported = new List<string>();
        using var arrived = new SemaphoreSlim(0);
        try
        {
            using (WatchFolderService? watch = WatchFolderService.TryStart(root, files =>
            {
                lock (reported)
                {
                    reported.AddRange(files);
                }

                arrived.Release();
            }, TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(200)))
            {
                AssertTrue(watch is not null, "The folder is watched.");
                string game = Path.Combine(root, "Game.iso");
                using (var writer = new FileStream(game, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    writer.Write(new byte[4096]);
                    writer.Flush();
                    AssertFalse(arrived.Wait(TimeSpan.FromSeconds(2)), "A file still open for writing is not reported.");
                }

                File.WriteAllBytes(Path.Combine(root, "Game.chd"), new byte[4096]);
                File.WriteAllBytes(Path.Combine(root, "sub", "Other.iso"), new byte[4096]);
                AssertTrue(arrived.Wait(TimeSpan.FromSeconds(10)), "The finished file is reported.");
                AssertFalse(arrived.Wait(TimeSpan.FromSeconds(2)), "Nothing else is reported.");
                AssertEqual(1, reported.Count, "One file is reported.");
                AssertEqual(game, reported[0], "The finished disc image is the one reported.");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
