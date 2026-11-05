using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Services;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Tests;

internal static class DiscMediaDecisionTests
{
    public static void ResolverBranchesAndBoundaries()
    {
        const long cd = 800L * 1024 * 1024;
        const long dvd = 9L * 1024 * 1024 * 1024;
        Check("LocDiscProbe_SystemCnfPs1Hint", dvd + 1, DiscMediaClassification.CdRom, DiscMediaDecisionSource.Structure, DiscMediaStructure.Ps1);
        Check("LocDiscProbe_PspStructure", 1, DiscMediaClassification.DvdRom, DiscMediaDecisionSource.Structure, DiscMediaStructure.Psp);
        Check("LocDiscProbe_Ps3BluRayStructure", dvd + 1, DiscMediaClassification.DvdRom, DiscMediaDecisionSource.Structure, DiscMediaStructure.Ps3);
        Check("LocDiscProbe_SystemCnfPs1Hint", 0, DiscMediaClassification.Unknown, DiscMediaDecisionSource.Unknown, DiscMediaStructure.None);
        Check("LocDiscProbe_PspStructure", -1, DiscMediaClassification.Unknown, DiscMediaDecisionSource.Unknown, DiscMediaStructure.None);
        Check("LocDiscProbe_SystemCnfPs2Boot2", cd, DiscMediaClassification.CdRom, DiscMediaDecisionSource.SizeFallback, DiscMediaStructure.None);
        Check("LocDiscProbe_SystemCnfPs2Boot2", cd + 1, DiscMediaClassification.DvdRom, DiscMediaDecisionSource.SizeFallback, DiscMediaStructure.None);
        Check("serial-evidence", cd, DiscMediaClassification.CdRom, DiscMediaDecisionSource.SizeFallback, DiscMediaStructure.None);
        Check("", cd + 1, DiscMediaClassification.DvdRom, DiscMediaDecisionSource.SizeFallback, DiscMediaStructure.None);
        Check("", dvd, DiscMediaClassification.DvdRom, DiscMediaDecisionSource.SizeFallback, DiscMediaStructure.None);
        Check("", dvd + 1, DiscMediaClassification.Unknown, DiscMediaDecisionSource.Unknown, DiscMediaStructure.None);
    }

    private static void Check(string reason, long length, DiscMediaClassification kind, DiscMediaDecisionSource source, DiscMediaStructure structure)
    {
        PlatformDetectionResult detection = PlatformDetectionResult.Create("Unknown", "", 0, reason);
        DiscMediaDecision decision = DiscMediaKindResolver.ResolveIsoMediaDecision(detection, length);
        Assert(decision == new DiscMediaDecision(kind, source, structure), $"Unexpected decision for {reason}, {length}: {decision}");
        Assert((int)DiscMediaKindResolver.ResolveIsoMediaKind(detection, length) == (int)kind, "Legacy classification changed.");
    }

    public static void DiagnosticsAndPreviewPropagation(string dir)
    {
        string empty = Path.Combine(dir, "reason-empty.iso");
        File.WriteAllBytes(empty, Array.Empty<byte>());
        var unknown = IsoChdmanCreateCommandResolver.ResolveCreateCompressionCommandWithDiagnostics(empty);
        Assert(unknown.MediaDecision?.Kind == DiscMediaClassification.Unknown && unknown.AutoSuggestedCommand == "createdvd" && unknown.Command == "createdvd", "Unknown must retain createdvd default without becoming DVD.");
        var forced = IsoChdmanCreateCommandResolver.ResolveCreateCompressionCommandWithDiagnostics(empty, IsoCreateCommandOverride.CreateCd);
        Assert(forced.MediaDecision == unknown.MediaDecision && forced.Command == "createcd", "Override must not change the classification.");

        string iso = Path.Combine(dir, "reason.iso");
        File.WriteAllBytes(iso, new byte[64 * 1024]);
        var diag = IsoChdmanCreateCommandResolver.ResolveCreateCompressionCommandWithDiagnostics(iso, IsoCreateCommandOverride.CreateDvd);
        var plan = ChdWorkflowProfilePlanner.PlanCreateFromSource(iso, IsoCreateCommandOverride.CreateDvd);
        var preview = Preview(iso, dir, new AppSettings { IsoCreateCommandOverride = IsoCreateCommandOverride.CreateDvd });
        Assert(diag.MediaDecision?.Kind == DiscMediaClassification.CdRom && diag.MediaDecision?.Source == DiscMediaDecisionSource.SizeFallback, "Small ISO classification must use size.");
        Assert(diag.AutoSuggestedCommand == "createcd" && diag.Command == "createdvd", "Override must only change command.");
        Assert(plan.IsSupported && plan.IsoDiagnostics?.MediaDecision == diag.MediaDecision && preview.IsoMediaDecision == diag.MediaDecision && preview.Command == "createdvd", "ISO decision must pass through planner and preview.");
        Assert(preview.MediaEvidence == diag.DetectionReason && preview.DescriptorEvidence == DescriptorCreateCdEvidence.None, "Existing evidence must remain unchanged.");

        var rejected = Preview(empty, dir, new AppSettings());
        Assert(!rejected.IsSupported && rejected.IsoMediaDecision is null && rejected.DescriptorEvidence == DescriptorCreateCdEvidence.None, "Rejected ISO must not claim evidence.");
        string archive = Path.Combine(dir, "reason.zip");
        File.WriteAllBytes(archive, new byte[] { 1, 2, 3, 4 });
        var archivePreview = Preview(archive, dir, new AppSettings());
        Assert(!archivePreview.IsSupported && archivePreview.IsoMediaDecision is null && archivePreview.DescriptorEvidence == DescriptorCreateCdEvidence.None, "Archive must remain deferred.");
        string cso = Path.Combine(dir, "reason.cso");
        File.WriteAllBytes(cso, new byte[] { 1, 2, 3, 4 });
        var csoPreview = Preview(cso, dir, new AppSettings());
        Assert(csoPreview.IsoMediaDecision is null && csoPreview.DescriptorEvidence == DescriptorCreateCdEvidence.None, "CSO must not claim ISO evidence.");
    }

    public static void DescriptorPropagation(string dir)
    {
        File.WriteAllBytes(Path.Combine(dir, "reason-track.bin"), new byte[2352]);
        foreach (var item in new[] { ("cue", "FILE \"reason-track.bin\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00\n", DescriptorCreateCdEvidence.Cue), ("gdi", "1\n1 0 4 2352 reason-track.bin 0\n", DescriptorCreateCdEvidence.Gdi) })
        {
            string path = Path.Combine(dir, "reason." + item.Item1);
            File.WriteAllText(path, item.Item2);
            var plan = ChdWorkflowProfilePlanner.PlanCreateFromSource(path);
            var preview = Preview(path, dir, new AppSettings());
            Assert(plan.IsSupported && plan.Command == "createcd" && plan.DescriptorEvidence == item.Item3, "Descriptor planner evidence missing.");
            Assert(preview.IsSupported && preview.Command == "createcd" && preview.DescriptorEvidence == item.Item3 && preview.IsoMediaDecision is null, "Descriptor evidence must reach preview without ISO classification.");
        }
        string bad = Path.Combine(dir, "reason-missing.cue");
        File.WriteAllText(bad, "FILE \"missing-track.bin\" BINARY\n TRACK 01 MODE1/2352\n INDEX 01 00:00:00\n");
        Assert(Preview(bad, dir, new AppSettings()).DescriptorEvidence == DescriptorCreateCdEvidence.None, "Rejected descriptor must not claim evidence.");
    }

    private static ChdConversionDecisionPreview Preview(string input, string dir, AppSettings settings) =>
        new ChdConversionService(new ChdCommandPreparationService(), new ChdProcessExecutionService(), new ChdResultMappingService(),
            new ChdVerificationBridge(), new FakeCapabilities(), null, new PlatformAwareChdProfilePolicy(), null)
            .PreviewDecisionAsync("fake-chdman.exe", input, Path.Combine(dir, "reason.chd"), settings).GetAwaiter().GetResult();

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FakeCapabilities : IChdmanCapabilityService
    {
        public Task<ChdmanCapabilitySnapshot> InspectAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(new ChdmanCapabilitySnapshot(path, "test", true, true, true, true, true, true, "", ""));
        public bool SupportsRequestedCompression(ChdmanCapabilitySnapshot capabilities, string command, string? compression) => capabilities.SupportsRequestedCompression(command, compression);
        public bool SupportsRequestedHunkSize(ChdmanCapabilitySnapshot capabilities, string command, int size) => capabilities.SupportsRequestedHunkSize(command, size);
    }
}
