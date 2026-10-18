using HakamiqChdTool.App.Core.Input;
using System;
using System.IO;
using System.Linq;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // Directory junctions need no privilege (unlike symbolic links), so this runs everywhere and
    // exercises the per-directory chain check that replaced the per-file ancestor walk.
    private static void TestInputResolverRejectsJunctions(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "resolver-junction");
        string library = Path.Combine(root, "library");
        string outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(library);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(library, "inside.iso"), "inside");
        File.WriteAllText(Path.Combine(outside, "outside.iso"), "outside");

        string linkedChild = Path.Combine(library, "linked");
        string linkedRoot = Path.Combine(root, "linked-root");
        CreateJunction(linkedChild, outside);
        CreateJunction(linkedRoot, library);

        var resolver = new InputResolver();

        string[] found = [.. resolver.Resolve(library, SearchOption.AllDirectories)];
        AssertEqual(1, found.Length, "Only the real file is resolved; the junction subtree is skipped.");
        AssertTrue(found[0].EndsWith("inside.iso", StringComparison.OrdinalIgnoreCase), "The resolved file is the one inside the library.");

        AssertEqual(0, resolver.Resolve(linkedRoot, SearchOption.AllDirectories).Count(), "A root reached through a junction resolves nothing.");
        AssertEqual(0, resolver.Resolve(Path.Combine(linkedChild, "outside.iso"), SearchOption.TopDirectoryOnly).Count(), "A file reached through a junction resolves nothing.");
        AssertEqual(0, resolver.Resolve(Path.Combine(linkedRoot, "inside.iso"), SearchOption.TopDirectoryOnly).Count(), "A file below a junction root resolves nothing.");
    }
}
