using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Services;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // A PSP CSO is expanded to an ISO and converted with the same 2048-byte hunks as a PSP ISO. A CSO from another
    // platform (CSO is also used for PS2) keeps the default DVD hunk size.
    private static void TestPspCsoUsesThePspIsoHunkSize()
    {
        var policy = new PlatformAwareChdProfilePolicy();
        ChdmanCapabilitySnapshot chdman = ChdmanCapabilitySnapshot.Unavailable(string.Empty, string.Empty);

        PlatformAwareChdProfileDecision Decide(string platform, string inputPath, ChdMediaFormatKind format) => policy.Resolve(new PlatformAwareChdProfileRequest(
            ChdProfileMediaKind.DvdRom, platform, format, TargetEmulatorProfile.Auto, ChdProfileUserGoal.Auto, chdman, "createdvd", inputPath, null, 0));

        PlatformAwareChdProfileDecision pspIso = Decide("PlayStation Portable", @"C:\g\game.iso", ChdMediaFormatKind.Iso);
        PlatformAwareChdProfileDecision pspCso = Decide("PlayStation Portable", @"C:\g\game.cso", ChdMediaFormatKind.Cso);
        PlatformAwareChdProfileDecision ps2Cso = Decide("PlayStation 2", @"C:\g\game.cso", ChdMediaFormatKind.Cso);
        PlatformAwareChdProfileDecision unknownCso = Decide("Unknown Platform", @"C:\g\game.cso", ChdMediaFormatKind.Cso);

        AssertEqual(2048, pspIso.HunkSize, "A PSP ISO uses 2048-byte hunks.");
        AssertEqual(2048, pspCso.HunkSize, "A PSP CSO uses the same 2048-byte hunks as a PSP ISO.");
        AssertEqual("createdvd", pspCso.Command, "A PSP CSO is still converted with createdvd.");
        AssertEqual(0, ps2Cso.HunkSize, "A PS2 CSO keeps the default DVD hunk size.");
        AssertEqual(0, unknownCso.HunkSize, "A CSO from an unknown platform keeps the default DVD hunk size.");
    }
}
