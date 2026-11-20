using System.IO;
using HakamiqChdTool.App.Services.Watch;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // When the watched folder is deleted, watching stops and the app is told once, instead of only a log line.
    private static void TestWatchFolderReportsALostFolderOnce()
    {
        string folder = Path.Combine(Path.GetTempPath(), "HakamiqChdTool.Tests", "watch-lost-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        int lost = 0;

        using (WatchFolderService? watch = WatchFolderService.TryStart(
                   folder,
                   _ => { },
                   TimeSpan.FromMilliseconds(100),
                   TimeSpan.FromMilliseconds(100),
                   onLost: () => Interlocked.Increment(ref lost)))
        {
            AssertTrue(watch is not null, "The folder is watched.");
            Thread.Sleep(300);
            AssertEqual(0, Volatile.Read(ref lost), "A folder that is still there is not reported as lost.");

            Directory.Delete(folder, recursive: true);
            DateTime end = DateTime.UtcNow.AddSeconds(5);
            while (Volatile.Read(ref lost) == 0 && DateTime.UtcNow < end)
            {
                Thread.Sleep(50);
            }

            Thread.Sleep(400);
            AssertEqual(1, Volatile.Read(ref lost), "A deleted watch folder is reported exactly once.");
        }
    }
}
