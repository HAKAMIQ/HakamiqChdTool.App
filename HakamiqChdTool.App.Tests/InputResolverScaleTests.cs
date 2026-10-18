using HakamiqChdTool.App.Core.Input;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static void TestInputResolverScalesWithDeepTrees(string workDirectory)
    {
        const int Files = 6000;
        string root = Path.Combine(workDirectory, "resolver-scale");
        string deep = Path.Combine(root, "a", "b", "c", "d", "e", "f", "g", "h");
        Directory.CreateDirectory(deep);

        for (int i = 0; i < Files; i++)
        {
            File.WriteAllBytes(Path.Combine(deep, $"f{i}.bin"), [1]);
        }

        Stopwatch clock = Stopwatch.StartNew();
        string[] resolved = new InputResolver().Resolve(root, SearchOption.AllDirectories).ToArray();
        clock.Stop();
        Console.WriteLine($"[MEASURE] InputResolver enumerated {resolved.Length} files at depth 9 in {clock.ElapsedMilliseconds} ms.");

        AssertEqual(Files, resolved.Length, "Every file in a deep tree is resolved.");
        AssertTrue(clock.Elapsed < TimeSpan.FromSeconds(30), $"Deep-tree enumeration stays within its budget ({clock.ElapsedMilliseconds} ms).");
    }
}
