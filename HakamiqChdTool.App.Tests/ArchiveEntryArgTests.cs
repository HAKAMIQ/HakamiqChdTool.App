using HakamiqChdTool.App.Services;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // 7-Zip expands * and ? in entry arguments, so a hostile entry name could select unrelated entries.
    private static void TestArchiveEntryArgumentsRejectWildcardsAndTraversal(string workDirectory)
    {
        AssertTrue(SevenZipArchiveExtractionService.IsSafeArchiveEntryArgument("disc/game.cue"), "A plain nested entry is accepted.");
        AssertFalse(SevenZipArchiveExtractionService.IsSafeArchiveEntryArgument("disc/*.bin"), "A star wildcard is refused.");
        AssertFalse(SevenZipArchiveExtractionService.IsSafeArchiveEntryArgument("disc/track?.bin"), "A question-mark wildcard is refused.");
        AssertFalse(SevenZipArchiveExtractionService.IsSafeArchiveEntryArgument("../escape.bin"), "Parent traversal is refused.");
        AssertFalse(SevenZipArchiveExtractionService.IsSafeArchiveEntryArgument("disc\\..\\escape.bin"), "Backslash traversal is refused.");
        AssertFalse(SevenZipArchiveExtractionService.IsSafeArchiveEntryArgument("@list.txt"), "A list-file reference is refused.");
        AssertFalse(SevenZipArchiveExtractionService.IsSafeArchiveEntryArgument("C:/abs.bin"), "A rooted entry is refused.");
    }
}
