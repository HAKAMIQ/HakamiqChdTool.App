using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.IO;

namespace HakamiqChdTool.App.Services;

public enum ChdExtractionMetadataKind
{
    Unknown = 0,
    CdRom = 1,
    DvdRom = 2,
    HardDisk = 3,
    Raw = 4
}

public enum ChdRestoreTargetMode
{
    Standard = 0,
    LegacyCdProfileToIso = 1
}

public sealed record MetadataAwareChdExtractionRequest(
    string? MediaType,
    string ChdPath,
    string OriginalPath,
    PlatformDetectionResult PlatformDetection,
    long? LogicalBytes);

public sealed record MetadataAwareChdExtractionDecision(
    bool IsSupported,
    ChdmanExtractionKind ExtractionKind,
    string OutputExtension,
    string StatusLine,
    string FailureMessageKey,
    string WarningMessageKey,
    string SuccessMessageKey,
    string EffectiveProfileName,
    string ReasonCode,
    ChdExtractionMetadataKind MetadataKind,
    ChdRestoreTargetMode RestoreTargetMode)
{
    public bool IsLegacyWrongProfile => RestoreTargetMode == ChdRestoreTargetMode.LegacyCdProfileToIso;
}

public interface IMetadataAwareChdExtractionPolicy
{
    MetadataAwareChdExtractionDecision Resolve(MetadataAwareChdExtractionRequest request);
}

public sealed class MetadataAwareChdExtractionPolicy : IMetadataAwareChdExtractionPolicy
{
    public const string UnknownChdExtractionMessageKey = "LocWorkflow_UnknownChdExtraction";
    public const string WrongProfileLegacyWarningKey = "LocChdExtraction_WrongProfileLegacyChdWarning";
    public const string LegacyCdRestoreSuccessKey = "LocChdExtraction_LegacyCdRestoreToIso";

    public const string DvdMetadataReasonCode = "ChdExtractionReason_DvdMetadataExtractDvd";
    public const string CdMetadataReasonCode = "ChdExtractionReason_CdMetadataExtractCd";
    public const string HdMetadataReasonCode = "ChdExtractionReason_HdMetadataExtractHd";
    public const string UnknownMetadataReasonCode = "ChdExtractionReason_UnknownMetadataBlocked";
    public const string LegacyCdMetadataReasonCode = "ChdExtractionReason_LegacyCdMetadataToIsoRestore";
    public const string RawMetadataReasonCode = "ChdExtractionReason_RawMetadataExtractRaw";

    private const long LikelyDvdLogicalBytesThreshold = 800L * 1024L * 1024L;

    public MetadataAwareChdExtractionDecision Resolve(MetadataAwareChdExtractionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ChdPath);

        ChdExtractionMetadataKind metadataKind = ResolveMetadataKind(request.MediaType);
        return metadataKind switch
        {
            ChdExtractionMetadataKind.DvdRom => BuildSupported(
                ChdmanExtractionKind.ExtractDvd,
                ".iso",
                "LocWorkflow_StatusExtractDvd",
                "DVD CHD metadata extractdvd",
                DvdMetadataReasonCode,
                metadataKind),

            ChdExtractionMetadataKind.CdRom when IsLegacyWrongProfileContext(request) => BuildSupported(
                ChdmanExtractionKind.ExtractCd,
                ".iso",
                WrongProfileLegacyWarningKey,
                "Wrong-profile / Legacy CHD restore target",
                LegacyCdMetadataReasonCode,
                metadataKind,
                warningMessageKey: WrongProfileLegacyWarningKey,
                successMessageKey: LegacyCdRestoreSuccessKey,
                restoreTargetMode: ChdRestoreTargetMode.LegacyCdProfileToIso),

            ChdExtractionMetadataKind.CdRom => BuildSupported(
                ChdmanExtractionKind.ExtractCd,
                ".cue",
                "LocWorkflow_StatusExtractCd",
                "CD CHD metadata extractcd",
                CdMetadataReasonCode,
                metadataKind),

            ChdExtractionMetadataKind.HardDisk => BuildSupported(
                ChdmanExtractionKind.ExtractHd,
                ".img",
                "LocWorkflow_StatusExtractHd",
                "HD CHD metadata extracthd",
                HdMetadataReasonCode,
                metadataKind),

            ChdExtractionMetadataKind.Raw => ResolveRawMetadata(request, metadataKind),

            _ => BuildUnsupported(metadataKind, UnknownMetadataReasonCode)
        };
    }

    private static MetadataAwareChdExtractionDecision ResolveRawMetadata(
        MetadataAwareChdExtractionRequest request,
        ChdExtractionMetadataKind metadataKind)
    {
        if (ChdWorkflowProfilePlanner.TryGetRawExtractionBlockReason(
                request.ChdPath,
                request.PlatformDetection,
                out string blockReason))
        {
            return BuildUnsupported(metadataKind, string.IsNullOrWhiteSpace(blockReason) ? UnknownMetadataReasonCode : blockReason);
        }

        return BuildSupported(
            ChdmanExtractionKind.ExtractRaw,
            ".raw",
            "LocWorkflow_StatusExtractRaw",
            "RAW CHD metadata extractraw",
            RawMetadataReasonCode,
            metadataKind);
    }

    private static MetadataAwareChdExtractionDecision BuildSupported(
        ChdmanExtractionKind extractionKind,
        string outputExtension,
        string statusLine,
        string effectiveProfileName,
        string reasonCode,
        ChdExtractionMetadataKind metadataKind,
        string warningMessageKey = "",
        string successMessageKey = "",
        ChdRestoreTargetMode restoreTargetMode = ChdRestoreTargetMode.Standard) => new(
            true,
            extractionKind,
            outputExtension,
            statusLine,
            string.Empty,
            warningMessageKey,
            successMessageKey,
            effectiveProfileName,
            reasonCode,
            metadataKind,
            restoreTargetMode);

    private static MetadataAwareChdExtractionDecision BuildUnsupported(
        ChdExtractionMetadataKind metadataKind,
        string reasonCode) => new(
            false,
            ChdmanExtractionKind.None,
            string.Empty,
            string.Empty,
            UnknownChdExtractionMessageKey,
            string.Empty,
            string.Empty,
            string.Empty,
            reasonCode,
            metadataKind,
            ChdRestoreTargetMode.Standard);

    private static ChdExtractionMetadataKind ResolveMetadataKind(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return ChdExtractionMetadataKind.Unknown;
        }

        string normalized = mediaType.Trim();
        if (string.Equals(normalized, "CD-ROM", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "GD-ROM", StringComparison.OrdinalIgnoreCase))
        {
            return ChdExtractionMetadataKind.CdRom;
        }

        if (string.Equals(normalized, "DVD-ROM", StringComparison.OrdinalIgnoreCase))
        {
            return ChdExtractionMetadataKind.DvdRom;
        }

        if (string.Equals(normalized, "HD-ROM", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Hard Disk", StringComparison.OrdinalIgnoreCase))
        {
            return ChdExtractionMetadataKind.HardDisk;
        }

        if (ChdWorkflowProfilePlanner.IsRawChdMediaType(normalized))
        {
            return ChdExtractionMetadataKind.Raw;
        }

        return ChdExtractionMetadataKind.Unknown;
    }

    private static bool IsLegacyWrongProfileContext(MetadataAwareChdExtractionRequest request)
    {
        string platformText = BuildContextText(request);
        if (IsPlayStationPortable(platformText))
        {
            return true;
        }

        if (!IsPlayStation2(platformText))
        {
            return false;
        }

        if (request.LogicalBytes.GetValueOrDefault() >= LikelyDvdLogicalBytesThreshold)
        {
            return true;
        }

        return platformText.Contains("DVD", StringComparison.OrdinalIgnoreCase)
            || platformText.Contains("ISO", StringComparison.OrdinalIgnoreCase)
            || platformText.Contains("createdvd", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildContextText(MetadataAwareChdExtractionRequest request)
    {
        string detectionPlatform = request.PlatformDetection.PlatformName ?? string.Empty;
        string detectionReason = request.PlatformDetection.Reason ?? string.Empty;
        string chdName = SafeFileNameWithoutExtension(request.ChdPath);
        string originalName = SafeFileNameWithoutExtension(request.OriginalPath);

        return string.Join(
            ' ',
            detectionPlatform,
            detectionReason,
            request.ChdPath,
            request.OriginalPath,
            chdName,
            originalName);
    }

    private static string SafeFileNameWithoutExtension(string path)
    {
        try
        {
            return Path.GetFileNameWithoutExtension(path) ?? string.Empty;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Log.Debug(ex, "Could not read path name while classifying legacy CHD context. Path={Path}", path);
            return string.Empty;
        }
    }

    private static bool IsPlayStationPortable(string text) =>
        text.Contains("PlayStation Portable", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Sony PSP", StringComparison.OrdinalIgnoreCase)
        || text.Contains("PPSSPP", StringComparison.OrdinalIgnoreCase)
        || text.Contains("PSP", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlayStation2(string text) =>
        text.Contains("PlayStation 2", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Sony PS2", StringComparison.OrdinalIgnoreCase)
        || text.Contains("PCSX2", StringComparison.OrdinalIgnoreCase)
        || text.Contains("PS2", StringComparison.OrdinalIgnoreCase);
}
