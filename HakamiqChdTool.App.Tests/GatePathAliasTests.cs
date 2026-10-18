using HakamiqChdTool.App.Services;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // Two spellings of one file (extended-length prefix, 8.3 short name) must take the same lock.
    private static void TestPathGateFoldsExtendedAndShortAliases(string workDirectory)
    {
        string dir = Path.Combine(workDirectory, "gate-alias-directory-with-long-name");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "a-long-file-name-for-alias.bin");
        File.WriteAllBytes(file, [1]);

        string plain = FilePathExclusiveGate.CanonicalGateKey(Path.GetFullPath(file));
        string extended = FilePathExclusiveGate.CanonicalGateKey(@"\\?\" + Path.GetFullPath(file));
        AssertEqual(plain, extended, "The extended-length prefix folds onto the plain key.");

        string? shortPath = TryGetShortPath(file);
        if (shortPath is not null && shortPath.Contains('~'))
        {
            AssertEqual(plain, FilePathExclusiveGate.CanonicalGateKey(shortPath), "An 8.3 alias folds onto the long key.");

            using IDisposable first = (IDisposable)FilePathExclusiveGate.AcquireAsync(file).GetAwaiter().GetResult();
            Task<System.IAsyncDisposable> second = FilePathExclusiveGate.AcquireAsync(shortPath);
            AssertFalse(second.Wait(300), "The alias waits for the holder of the long path.");
            first.Dispose();
            second.GetAwaiter().GetResult().DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        else
        {
            Console.WriteLine("[NOTE] 8.3 names are disabled on this volume; alias fold covered by the prefix case only.");
        }
    }

    private static string? TryGetShortPath(string path)
    {
        var buffer = new System.Text.StringBuilder(1024);
        uint n = GetShortPathNameW(path, buffer, (uint)buffer.Capacity);
        return n == 0 || n >= buffer.Capacity ? null : buffer.ToString();
    }
}
