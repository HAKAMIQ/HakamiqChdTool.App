using System.Collections.Generic;

namespace HakamiqChdTool.App.Models.Chd;

public sealed record ChdConversionDecisionPreview(
    bool IsSupported,
    string MediaKind,
    string MediaEvidence,
    string Command,
    string InputPath,
    string OutputPath,
    string PlatformProfile,
    ChdmanCapabilitySnapshot Capabilities,
    string ResolvedCompression,
    int ResolvedHunkSizeBytes,
    IReadOnlyList<string> Arguments,
    string DisplayCommandLine,
    bool VerifyAfterConversion,
    string Message,
    string CapabilityWarning)
{
    public DiscMediaDecision? IsoMediaDecision { get; init; }
    public DescriptorCreateCdEvidence DescriptorEvidence { get; init; }
}
