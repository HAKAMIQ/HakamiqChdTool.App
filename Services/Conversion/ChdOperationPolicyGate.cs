using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public sealed record ChdOperationPolicyRequest(
    string ChdmanPath,
    string InputPath,
    string Command,
    ChdmanExtractionKind ExtractionKind,
    IsoChdmanCreateDiagnostics? IsoDiagnostics,
    string? ResolvedCompression,
    int ResolvedHunkSizeBytes,
    bool ExtractionMetadataDecisionConfirmed,
    ChdmanCapabilitySnapshot? PrecomputedCapabilities = null,
    ChdProfileMediaKind MediaKind = ChdProfileMediaKind.Unknown,
    ChdMediaFormatKind InputFormat = ChdMediaFormatKind.Unknown,
    PlatformAwareChdProfileDecision? ProfileDecision = null);

public sealed record ChdOperationPolicyDecision(
    bool IsAllowed,
    string MessageKey,
    ChdmanCapabilitySnapshot? Capabilities)
{
    public static ChdOperationPolicyDecision Allow(ChdmanCapabilitySnapshot capabilities) => new(true, string.Empty, capabilities);

    public static ChdOperationPolicyDecision Block(string messageKey, ChdmanCapabilitySnapshot? capabilities = null) => new(
        false,
        string.IsNullOrWhiteSpace(messageKey) ? "LocChdPolicy_CapabilityProbeFailed" : messageKey,
        capabilities);
}

public interface IChdOperationPolicyGate
{
    Task<ChdOperationPolicyDecision> EvaluateAsync(
        ChdOperationPolicyRequest request,
        CancellationToken cancellationToken);
}

public sealed class ChdOperationPolicyGate : IChdOperationPolicyGate
{
    public const string CapabilityProbeFailedMessageKey = "LocChdPolicy_CapabilityProbeFailed";
    public const string CreateDvdUnsupportedMessageKey = "LocChdPolicy_CreateDvdUnsupported";
    public const string ExtractDvdUnsupportedMessageKey = "LocChdPolicy_ExtractDvdUnsupported";
    public const string PspIsoCreateCdBlockedMessageKey = "LocChdPolicy_PspIsoCreateCdBlocked";
    public const string Ps2DvdIsoCreateCdBlockedMessageKey = "LocChdPolicy_Ps2DvdIsoCreateCdBlocked";
    public const string RequestedCompressionUnsupportedMessageKey = "LocChdPolicy_RequestedCompressionUnsupported";
    public const string RequestedHunkSizeUnsupportedMessageKey = "LocChdPolicy_RequestedHunkSizeUnsupported";
    public const string ExtractionMetadataRequiredMessageKey = "LocChdPolicy_ExtractionMetadataRequired";
    public const string UnknownIsoMediaKindRequiredMessageKey = "LocChdPolicy_UnknownIsoMediaKindRequired";

    private readonly IChdmanCapabilityService _capabilityService;

    public ChdOperationPolicyGate(IChdmanCapabilityService capabilityService)
    {
        _capabilityService = capabilityService ?? throw new ArgumentNullException(nameof(capabilityService));
    }

    public async Task<ChdOperationPolicyDecision> EvaluateAsync(
        ChdOperationPolicyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (RequiresMetadataDecisionForExtraction(request) && !request.ExtractionMetadataDecisionConfirmed)
        {
            Log.Warning(
                "CHD extraction blocked because no metadata-based extraction decision was confirmed. Input={InputPath}; Command={Command}; ExtractionKind={ExtractionKind}",
                request.InputPath,
                request.Command,
                request.ExtractionKind);

            return ChdOperationPolicyDecision.Block(ExtractionMetadataRequiredMessageKey);
        }

        ChdOperationPolicyDecision formatDecision = EvaluateFormatPolicy(request);
        if (!formatDecision.IsAllowed)
        {
            return formatDecision;
        }

        ChdmanCapabilitySnapshot capabilities = request.PrecomputedCapabilities
            ?? await _capabilityService
                .InspectAsync(request.ChdmanPath, cancellationToken)
                .ConfigureAwait(false);

        if (!capabilities.IsAvailable)
        {
            return ChdOperationPolicyDecision.Block(
                string.IsNullOrWhiteSpace(capabilities.MessageKey)
                    ? CapabilityProbeFailedMessageKey
                    : capabilities.MessageKey,
                capabilities);
        }

        if (string.Equals(request.Command, "createdvd", StringComparison.OrdinalIgnoreCase)
            && !capabilities.SupportsCreateDvd)
        {
            Log.Warning(
                "CHD operation blocked because the selected chdman does not advertise createdvd. Chdman={ChdmanPath}; Version={Version}; Input={InputPath}",
                request.ChdmanPath,
                capabilities.Version,
                request.InputPath);

            return ChdOperationPolicyDecision.Block(CreateDvdUnsupportedMessageKey, capabilities);
        }

        if (string.Equals(request.Command, "extractdvd", StringComparison.OrdinalIgnoreCase)
            && !capabilities.SupportsExtractDvd)
        {
            Log.Warning(
                "CHD operation blocked because the selected chdman does not advertise extractdvd. Chdman={ChdmanPath}; Version={Version}; Input={InputPath}",
                request.ChdmanPath,
                capabilities.Version,
                request.InputPath);

            return ChdOperationPolicyDecision.Block(ExtractDvdUnsupportedMessageKey, capabilities);
        }

        if (!_capabilityService.SupportsRequestedCompression(capabilities, request.Command, request.ResolvedCompression))
        {
            Log.Warning(
                "CHD operation blocked because requested compression is unsupported by the selected chdman. Chdman={ChdmanPath}; Version={Version}; Command={Command}; Compression={Compression}",
                request.ChdmanPath,
                capabilities.Version,
                request.Command,
                request.ResolvedCompression);

            return ChdOperationPolicyDecision.Block(RequestedCompressionUnsupportedMessageKey, capabilities);
        }

        if (!_capabilityService.SupportsRequestedHunkSize(capabilities, request.Command, request.ResolvedHunkSizeBytes))
        {
            Log.Warning(
                "CHD operation blocked because requested hunk size is unsupported by the selected chdman. Chdman={ChdmanPath}; Version={Version}; Command={Command}; HunkSize={HunkSize}",
                request.ChdmanPath,
                capabilities.Version,
                request.Command,
                request.ResolvedHunkSizeBytes);

            return ChdOperationPolicyDecision.Block(RequestedHunkSizeUnsupportedMessageKey, capabilities);
        }

        return ChdOperationPolicyDecision.Allow(capabilities);
    }

    private static ChdOperationPolicyDecision EvaluateFormatPolicy(ChdOperationPolicyRequest request)
    {
        if (request.InputFormat == ChdMediaFormatKind.Iso && request.MediaKind == ChdProfileMediaKind.Unknown)
        {
            Log.Warning(
                "CHD operation blocked: ISO requires a media-kind decision before chdman command selection. Input={InputPath}; Platform={Platform}; ReasonCode={ReasonCode}",
                request.InputPath,
                request.IsoDiagnostics?.PlatformName,
                request.ProfileDecision?.ReasonCode);

            return ChdOperationPolicyDecision.Block(UnknownIsoMediaKindRequiredMessageKey);
        }

        if (request.InputFormat != ChdMediaFormatKind.Iso
            || !string.Equals(request.Command, "createcd", StringComparison.OrdinalIgnoreCase))
        {
            return ChdOperationPolicyDecision.Allow(ChdmanCapabilitySnapshot.Unavailable(request.ChdmanPath, string.Empty));
        }

        if (IsPlayStationPortable(request.IsoDiagnostics?.PlatformName))
        {
            Log.Warning(
                "CHD operation blocked: PSP ISO must not be routed through createcd. Input={InputPath}; Platform={Platform}; Reason={Reason}",
                request.InputPath,
                request.IsoDiagnostics?.PlatformName,
                request.IsoDiagnostics?.DetectionReason);

            return ChdOperationPolicyDecision.Block(PspIsoCreateCdBlockedMessageKey);
        }

        if (IsPlayStation2(request.IsoDiagnostics?.PlatformName) && request.MediaKind == ChdProfileMediaKind.DvdRom)
        {
            Log.Warning(
                "CHD operation blocked: PS2 DVD ISO must not be routed through createcd. Input={InputPath}; Platform={Platform}; MediaKind={MediaKind}; AutoSuggestedCommand={AutoSuggestedCommand}; SizeBytes={SizeBytes}",
                request.InputPath,
                request.IsoDiagnostics?.PlatformName,
                request.MediaKind,
                request.IsoDiagnostics?.AutoSuggestedCommand,
                request.IsoDiagnostics?.FileLengthBytes);

            return ChdOperationPolicyDecision.Block(Ps2DvdIsoCreateCdBlockedMessageKey);
        }

        return ChdOperationPolicyDecision.Allow(ChdmanCapabilitySnapshot.Unavailable(request.ChdmanPath, string.Empty));
    }

    private static bool RequiresMetadataDecisionForExtraction(ChdOperationPolicyRequest request) =>
        string.Equals(Path.GetExtension(request.InputPath), ".chd", StringComparison.OrdinalIgnoreCase)
        && request.ExtractionKind != ChdmanExtractionKind.None
        && request.Command.StartsWith("extract", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlayStationPortable(string? platformName)
    {
        if (string.IsNullOrWhiteSpace(platformName))
        {
            return false;
        }

        return platformName.Contains("PlayStation Portable", StringComparison.OrdinalIgnoreCase)
            || platformName.Contains("Sony PSP", StringComparison.OrdinalIgnoreCase)
            || platformName.Contains("PSP", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlayStation2(string? platformName)
    {
        if (string.IsNullOrWhiteSpace(platformName))
        {
            return false;
        }

        return platformName.Contains("PlayStation 2", StringComparison.OrdinalIgnoreCase)
            || platformName.Contains("Sony PlayStation 2", StringComparison.OrdinalIgnoreCase)
            || platformName.Contains("PS2", StringComparison.OrdinalIgnoreCase);
    }
}
