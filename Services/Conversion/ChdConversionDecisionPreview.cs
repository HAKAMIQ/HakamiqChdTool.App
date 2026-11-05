using HakamiqChdTool.App.Core.Chd.Profiles;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Models.Chd;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

internal static class ChdConversionDecisionPreviewBuilder
{
    /// <summary>
    /// Computes the conversion decision without preparing input or creating output.
    /// Capability inspection only starts chdman help/version probes; it never starts a create command.
    /// </summary>
    public static async Task<ChdConversionDecisionPreview> BuildAsync(
        string chdmanPath,
        string inputPath,
        string outputPath,
        AppSettings settings,
        IChdCommandPreparationService commandPreparation,
        IChdmanCapabilityService capabilityService,
        IPlatformAwareChdProfilePolicy profilePolicy,
        IChdOperationPolicyGate operationPolicyGate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string resolvedInputPath = commandPreparation.NormalizePathForCli(inputPath);
        string resolvedOutputPath = commandPreparation.NormalizePathForCli(outputPath);
        string inputExtension = Path.GetExtension(resolvedInputPath).ToLowerInvariant();
        ChdWorkflowProfilePlan plannerDecision = ChdWorkflowProfilePlanner.PlanCreateFromSource(
            resolvedInputPath,
            settings.IsoCreateCommandOverride,
            ChdMediaContainerKind.DirectFile,
            settings.ChdPlatformProfileId);
        if (!plannerDecision.IsSupported)
        {
            ChdmanCapabilitySnapshot unavailable = ChdmanCapabilitySnapshot.Unavailable(chdmanPath, "LocWorkflow_UnsupportedFileType");
            return new ChdConversionDecisionPreview(
                false, plannerDecision.MediaKind.ToString(), plannerDecision.StatusLine, string.Empty, resolvedInputPath, resolvedOutputPath,
                string.Empty, unavailable, string.Empty, 0, Array.Empty<string>(), string.Empty,
                settings.VerifyAfterConversion, plannerDecision.FailureMessage, plannerDecision.FailureMessage);
        }

        IsoChdmanCreateDiagnostics? isoDiagnostics = plannerDecision.IsoDiagnostics;
        ChdRequestedCreateProfileSelection requested = ChdPlatformProfileExecutionPolicy.ApplyRequestedCreateProfile(
            plannerDecision.Command,
            inputExtension,
            settings.ChdPlatformProfileId,
            ChdmanExtractionKind.None,
            resolvedInputPath,
            resolvedOutputPath);
        if (requested.FailureResult is not null)
        {
            ChdmanCapabilitySnapshot unavailable = ChdmanCapabilitySnapshot.Unavailable(chdmanPath, "LocWorkflow_UnsupportedFileType");
            return new ChdConversionDecisionPreview(
                false, plannerDecision.MediaKind.ToString(), plannerDecision.StatusLine, plannerDecision.Command, resolvedInputPath, resolvedOutputPath,
                requested.Profile?.DisplayName ?? string.Empty, unavailable, string.Empty, 0, Array.Empty<string>(),
                string.Empty, settings.VerifyAfterConversion, requested.FailureResult.Message,
                requested.FailureResult.Message);
        }

        ChdConversionServiceSupport.ChdPreparedPolicyContext policy = await ChdConversionServiceSupport.PreparePolicyContextAsync(
            chdmanPath,
            inputPath,
            outputPath,
            resolvedInputPath,
            resolvedOutputPath,
            requested.Command,
            ChdmanExtractionKind.None,
            isoDiagnostics,
            settings.CompressionCodecs,
            settings.HunkSizeBytes,
            extractionMetadataDecisionConfirmed: false,
            commandPreparation,
            capabilityService,
            profilePolicy,
            operationPolicyGate,
            cancellationToken).ConfigureAwait(false);

        ChdPlatformProfile? profile = ChdPlatformProfileExecutionPolicy.ResolveCreateProfile(
            isExtractCommand: false,
            requested.Profile,
            policy.Command,
            resolvedInputPath,
            ChdConversionServiceSupport.ResolveProfilePlatform(resolvedInputPath, isoDiagnostics),
            settings.ChdPlatformProfileId);
        ChdConversionExecutionSetup.ArgumentContext args = ChdConversionExecutionSetup.BuildArgumentContext(
            profile,
            isExtractCommand: false,
            policy.Command,
            resolvedInputPath,
            resolvedOutputPath,
            ChdmanExtractionKind.None,
            string.Empty,
            string.Empty,
            settings.MaxProcessorCount,
            settings.EnableAutoResourceLimiter,
            settings.ReservedLogicalCores,
            settings.PerformanceMode,
            policy.ResolvedCompression,
            policy.ResolvedHunkSizeBytes,
            allowOverwriteOutput: !settings.SkipExistingOutput,
            commandPreparation);

        string capabilityWarning = !policy.Capabilities.IsAvailable
            ? policy.Capabilities.MessageKey
            : !capabilityService.SupportsRequestedCompression(policy.Capabilities, policy.Command, policy.ResolvedCompression)
                ? "LocChdPolicy_RequestedCompressionUnsupported"
                : !capabilityService.SupportsRequestedHunkSize(policy.Capabilities, policy.Command, policy.ResolvedHunkSizeBytes)
                    ? "LocChdPolicy_RequestedHunkSizeUnsupported"
                    : string.Empty;

        return new ChdConversionDecisionPreview(
            IsSupported: true,
            MediaKind: plannerDecision.MediaKind.ToString(),
            MediaEvidence: plannerDecision.IsoDiagnostics?.DetectionReason ?? plannerDecision.StatusLine,
            Command: policy.Command,
            InputPath: resolvedInputPath,
            OutputPath: resolvedOutputPath,
            PlatformProfile: profile?.DisplayName ?? string.Empty,
            Capabilities: policy.Capabilities,
            ResolvedCompression: policy.ResolvedCompression,
            ResolvedHunkSizeBytes: policy.ResolvedHunkSizeBytes,
            Arguments: args.Arguments.AsReadOnly(),
            DisplayCommandLine: ChdmanCliRunner.FormatCommandLineForDisplay(chdmanPath, args.Arguments),
            VerifyAfterConversion: settings.VerifyAfterConversion,
            Message: policy.FailureResult?.Message ?? string.Empty,
            CapabilityWarning: capabilityWarning);
    }
}
