using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Services;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Tests;

internal static class ConversionDecisionPreviewTests
{
    public static void PreviewUsesPlannerAndCapabilitySnapshot(string workDirectory)
    {
        string input = CreateIso(workDirectory, "preview-planner.iso");
        FakeCapabilities capability = new(AvailableCapabilities());
        ChdConversionService service = CreateService(capability);
        AppSettings settings = new();
        ChdWorkflowProfilePlan plan = ChdWorkflowProfilePlanner.PlanCreateFromSource(
            input, settings.IsoCreateCommandOverride, ChdMediaContainerKind.DirectFile, settings.ChdPlatformProfileId);

        ChdConversionDecisionPreview preview = service.PreviewDecisionAsync("fake-chdman.exe", input,
            Path.Combine(workDirectory, "planner.chd"), settings).GetAwaiter().GetResult();

        Assert(plan.IsSupported && plan.Command == preview.Command, "Preview command must match the source planner.");
        Assert(preview.Capabilities == capability.Snapshot, "Preview must return the capability snapshot used by policy.");
        Assert(capability.InspectCount >= 1, "Preview must inspect chdman capability.");
    }

    public static void OutputAndVerifyReflectSettings(string workDirectory)
    {
        string input = CreateIso(workDirectory, "preview-output.iso");
        AppSettings settings = new()
        {
            VerifyAfterConversion = false,
            CompressionCodecs = "preset:fast",
            HunkSizeBytes = -2,
            SkipExistingOutput = false
        };
        ChdConversionDecisionPreview preview = CreateService(new FakeCapabilities(AvailableCapabilities()))
            .PreviewDecisionAsync("fake-chdman.exe", input, Path.Combine(workDirectory, "chosen-output.chd"), settings)
            .GetAwaiter().GetResult();

        int outputArgument = Array.IndexOf(preview.Arguments.ToArray(), "-o");
        Assert(outputArgument >= 0 && preview.Arguments[outputArgument + 1] == Path.Combine(workDirectory, "chosen-output.chd"),
            "Preview arguments must use the selected output path.");
        Assert(!preview.VerifyAfterConversion, "Preview verification must reflect the supplied settings.");
        Assert(preview.Arguments.Contains("-c"), "Preview must expose resolved codec arguments when present.");
        Assert(preview.Arguments.Contains("-f"), "Preview overwrite argument must follow the execution setting.");
    }

    public static void CsoPreviewDoesNotPrepareInput(string workDirectory)
    {
        string cso = Path.Combine(workDirectory, "preview-input.cso");
        File.WriteAllBytes(cso, new byte[] { 1, 2, 3, 4 });
        CountingCsoPreprocessor preprocessor = new();
        ChdConversionService service = CreateService(new FakeCapabilities(AvailableCapabilities()), preprocessor);
        service.PreviewDecisionAsync("fake-chdman.exe", cso, Path.Combine(workDirectory, "cso.chd"), new AppSettings())
            .GetAwaiter().GetResult();

        Assert(preprocessor.CallCount == 0, "CSO preview must not run input preparation.");
        Assert(!File.Exists(Path.Combine(workDirectory, "preview-input.iso")), "CSO preview must not create a temporary ISO.");
    }

    public static void ArchivePreviewIsDeferredAndDoesNotExtract(string workDirectory)
    {
        string archive = Path.Combine(workDirectory, "preview-input.zip");
        File.WriteAllBytes(archive, new byte[] { 5, 6, 7, 8 });
        ChdWorkflowProfilePlan plan = ChdWorkflowProfilePlanner.PlanCreateFromSource(archive);
        Assert(!plan.IsSupported && plan.ContainerKind == ChdMediaContainerKind.Archive,
            "Archive planning must remain explicitly deferred to workflow preparation.");
        Assert(File.ReadAllBytes(archive).Length == 4, "Archive inspection must leave source bytes unchanged.");
    }

    public static void CapabilityFailureDoesNotBlockPreview(string workDirectory)
    {
        string input = CreateIso(workDirectory, "preview-no-capability.iso");
        FakeCapabilities capability = new(ChdmanCapabilitySnapshot.Unavailable("fake-chdman.exe", "probe-failed"));
        ChdConversionDecisionPreview preview = CreateService(capability)
            .PreviewDecisionAsync("fake-chdman.exe", input, Path.Combine(workDirectory, "no-capability.chd"), new AppSettings())
            .GetAwaiter().GetResult();

        Assert(preview.IsSupported, "Capability inspection failure must be informational and not block conversion.");
        Assert(!preview.Capabilities.IsAvailable && preview.CapabilityWarning == "probe-failed",
            "Capability failure must remain visible in the preview.");
    }

    public static void UnsupportedInputReturnsInformationalResult(string workDirectory)
    {
        string input = Path.Combine(workDirectory, "preview-unsupported.txt");
        File.WriteAllText(input, "unchanged");
        ChdConversionDecisionPreview preview = CreateService(new FakeCapabilities(AvailableCapabilities()))
            .PreviewDecisionAsync("fake-chdman.exe", input, Path.Combine(workDirectory, "unsupported.chd"), new AppSettings())
            .GetAwaiter().GetResult();
        Assert(!preview.IsSupported && preview.Arguments.Count == 0 && !string.IsNullOrWhiteSpace(preview.Message),
            "Unsupported input must produce an advisory result without attempting conversion.");
        Assert(File.ReadAllText(input) == "unchanged", "Unsupported preview must leave its input unchanged.");
    }

    public static void PreviewPanelIsOptionalAndCollapsedByDefault()
    {
        string xamlPath = Path.Combine(Environment.CurrentDirectory, "Views", "Main", "QueueView.xaml");
        string xaml = File.ReadAllText(xamlPath);
        string mainWindowXaml = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Views", "MainWindow", "MainWindow.xaml"));
        Assert(xaml.Contains("x:Name=\"ConversionDecisionPreviewPanel\"", StringComparison.Ordinal)
            && xaml.Contains("IsExpanded=\"False\"", StringComparison.Ordinal),
            "Decision preview panel must exist and start collapsed.");
        Assert(mainWindowXaml.Contains("ConversionDecisionPreviewRequested", StringComparison.Ordinal),
            "Preview must be separately invoked from the existing run control.");
    }

    private static string CreateIso(string workDirectory, string name)
    {
        string path = Path.Combine(workDirectory, name);
        File.WriteAllBytes(path, new byte[64 * 1024]);
        return path;
    }

    private static ChdConversionService CreateService(FakeCapabilities capability, ICsoPreprocessor? cso = null) =>
        new(new ChdCommandPreparationService(), new ChdProcessExecutionService(), new ChdResultMappingService(),
            new ChdVerificationBridge(), capability, null, new PlatformAwareChdProfilePolicy(), cso);

    private static ChdmanCapabilitySnapshot AvailableCapabilities() => new(
        "fake-chdman.exe", "test-version", true, true, true, true, true, true, string.Empty, "read-only help probe");

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeCapabilities(ChdmanCapabilitySnapshot snapshot) : IChdmanCapabilityService
    {
        public ChdmanCapabilitySnapshot Snapshot { get; } = snapshot;
        public int InspectCount { get; private set; }
        public Task<ChdmanCapabilitySnapshot> InspectAsync(string chdmanPath, CancellationToken cancellationToken)
        {
            InspectCount++;
            return Task.FromResult(Snapshot with { ExecutablePath = chdmanPath });
        }
        public bool SupportsRequestedCompression(ChdmanCapabilitySnapshot capabilities, string command, string? resolvedCompression) =>
            capabilities.SupportsRequestedCompression(command, resolvedCompression);
        public bool SupportsRequestedHunkSize(ChdmanCapabilitySnapshot capabilities, string command, int hunkSizeBytes) =>
            capabilities.SupportsRequestedHunkSize(command, hunkSizeBytes);
    }

    private sealed class CountingCsoPreprocessor : ICsoPreprocessor
    {
        public int CallCount { get; private set; }
        public Task<CsoToolProbeResult> InspectAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("Preview must not invoke the CSO preprocessor.");
        }

        public Task<CsoPreprocessResult> PreprocessAsync(string inputCsoPath, string temporaryIsoPath,
            CancellationToken cancellationToken, Func<string, Task>? progress = null)
        {
            CallCount++;
            throw new InvalidOperationException("Preview must not invoke the CSO preprocessor.");
        }
    }
}
