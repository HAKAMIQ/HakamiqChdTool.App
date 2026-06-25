using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Core.Chd.Profiles;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Models.PlayStation.BluRayAnalysis;
using HakamiqChdTool.App.Services.PlayStation.BluRayAnalysis;
using HakamiqChdTool.App.Services.MediaInputPolicy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.Services;

public enum ChdMediaContainerKind
{
    DirectFile = 0,
    Archive = 1
}

public enum ChdMediaFormatKind
{
    Unknown = 0,
    Iso = 1,
    Cue = 2,
    Gdi = 3,
    Chd = 4,
    CdChd = 5,
    DvdChd = 6,
    HdChd = 7,
    RawChd = 8,
    Toc = 9,
    Nrg = 10,
    Cso = 11
}

public enum ChdWorkflowProfileKind
{
    Unsupported = 0,
    CreateCd = 1,
    CreateDvd = 2,
    Verify = 3,
    ExtractCd = 4,
    ExtractDvd = 5,
    ExtractHd = 6,
    ExtractRaw = 7
}

public sealed class ChdWorkflowProfilePlan
{
    public bool IsSupported { get; init; }
    public ChdMediaContainerKind ContainerKind { get; init; }
    public ChdMediaFormatKind MediaKind { get; init; }
    public ChdWorkflowProfileKind ProfileKind { get; init; }
    public string Command { get; init; } = string.Empty;
    public ChdmanExtractionKind ExtractionKind { get; init; }
    public string OutputExtension { get; init; } = string.Empty;
    public string StatusLine { get; init; } = string.Empty;
    public string FailureMessage { get; init; } = string.Empty;
    public string ContainerLabel => ContainerKind == ChdMediaContainerKind.Archive ? "Archive" : "DirectFile";
    public bool RequiresDescriptorDependencies => MediaKind is ChdMediaFormatKind.Cue or ChdMediaFormatKind.Gdi or ChdMediaFormatKind.Toc;
    public IsoChdmanCreateDiagnostics? IsoDiagnostics { get; init; }
    public ChdPlatformProfile? PlatformProfile { get; init; }

    public static ChdWorkflowProfilePlan Unsupported(
        string messageKey,
        ChdMediaContainerKind containerKind = ChdMediaContainerKind.DirectFile) => new()
        {
            IsSupported = false,
            ContainerKind = containerKind,
            MediaKind = ChdMediaFormatKind.Unknown,
            ProfileKind = ChdWorkflowProfileKind.Unsupported,
            FailureMessage = string.IsNullOrWhiteSpace(messageKey)
                ? ChdWorkflowProfilePlanner.UnsupportedMessageKey
                : messageKey
        };
}

public static class ChdWorkflowProfilePlanner
{
    public const string UnsupportedMessageKey = "LocWorkflow_UnsupportedFileType";
    public const string MissingDescriptorDependenciesMessageKey = "LocArchive_DescriptorMissingDependencies";
    public const string UnknownChdExtractionMessageKey = "LocWorkflow_UnknownChdExtraction";
    public const string RawDiscConsoleAmbiguousMessageKey = "LocWorkflow_RawDiscConsoleAmbiguous";
    public const string RawDiscConsoleConflictMessageKey = "LocWorkflow_RawDiscConsoleConflict";
    public const string RawExtractionBlockedMessageKey = "LocWorkflow_RawExtractionBlocked";
    public const string InvalidInputPathMessageKey = "LocWorkflow_InvalidInputPath";
    public const string InputFileNotFoundMessageKey = "LocWorkflow_InputFileNotFound";
    public const string ChdAlreadyChdMessageKey = "LocWorkflow_ChdAlreadyChd";
    public const string ArchiveRequiresExtractionMessageKey = "LocWorkflow_ArchiveRequiresExtraction";
    public const string RawStandaloneBlockedMessageKey = "LocWorkflow_RawStandaloneBlocked";
    public const string BinStandaloneBlockedMessageKey = "LocWorkflow_BinStandaloneBlocked";
    public const string VerifyChdOnlyMessageKey = "LocWorkflow_VerifyChdOnly";
    public const string DescriptorReadFailedMessageKey = "LocWorkflow_DescriptorReadFailed";
    public const string UnknownIsoAsCdBlockedMessageKey = "LocWorkflow_UnknownIsoAsCdBlocked";
    public const string IsoTooSmallMessageKey = "LocWorkflow_IsoTooSmall";
    public const string IsoReadFailedMessageKey = "LocWorkflow_IsoReadFailed";
    public const string NonChdRecommendedCompressionMessageKey = "LocWorkflow_NonChdRecommendedCompression";
    public const string CsoPreparationDisabledMessageKey = "LocWorkflow_CsoPreparationDisabled";

    private const string StatusCreateCueKey = "LocWorkflow_StatusCreateCue";
    private const string StatusCreateGdiKey = "LocWorkflow_StatusCreateGdi";
    private const string StatusCreateTocKey = "LocWorkflow_StatusCreateToc";
    private const string StatusCreateNrgKey = "LocWorkflow_StatusCreateNrg";
    private const string StatusPrepareCsoToIsoKey = "LocWorkflow_PreparingCsoToIso";
    private const string StatusVerifyChdKey = "LocWorkflow_StatusVerifyChd";
    private const string StatusExtractCdKey = "LocWorkflow_StatusExtractCd";
    private const string StatusExtractDvdKey = "LocWorkflow_StatusExtractDvd";
    private const string StatusExtractHdKey = "LocWorkflow_StatusExtractHd";
    private const string StatusExtractRawKey = "LocWorkflow_StatusExtractRaw";
    private const string StatusCreateIsoCdKey = "LocWorkflow_StatusCreateIsoCd";
    private const string StatusCreateIsoDvdKey = "LocWorkflow_StatusCreateIsoDvd";

    private const string RawMediaImageNameKey = "LocWorkflow_RawMediaImageName";
    private const string RawMetadataConflictTitleKey = "LocWorkflow_RawMetadataConflictTitle";
    private const string RawMetadataConflictReasonKey = "LocWorkflow_RawMetadataConflictReason";
    private const string RawMetadataPlayStationConflictReasonKey = "LocWorkflow_RawMetadataPlayStationConflictReason";
    private const string RawMetadataSafetyHintReasonKey = "LocWorkflow_RawMetadataSafetyHintReason";

    private const long MaxDescriptorTextBytes = 4L * 1024L * 1024L;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    public static ChdWorkflowProfilePlan PlanCreateFromSource(
        string inputPath,
        IsoCreateCommandOverride isoCreateCommandOverride = IsoCreateCommandOverride.Auto,
        ChdMediaContainerKind containerKind = ChdMediaContainerKind.DirectFile,
        string? platformProfileId = null)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return ChdWorkflowProfilePlan.Unsupported(InvalidInputPathMessageKey, containerKind);
        }

        if (!File.Exists(inputPath))
        {
            return ChdWorkflowProfilePlan.Unsupported(InputFileNotFoundMessageKey, containerKind);
        }

        string extension = Path.GetExtension(inputPath).ToLowerInvariant();
        ChdPlatformProfile? requestedProfile = ChdPlatformProfiles.FindById(platformProfileId);
        if (requestedProfile is not null)
        {
            return PlanRequestedPlatformProfile(inputPath, extension, requestedProfile, containerKind);
        }

        return extension switch
        {
            ".cue" => PlanDescriptorCreateCd(inputPath, ChdMediaFormatKind.Cue, StatusCreateCueKey, containerKind),
            ".gdi" => PlanDescriptorCreateCd(inputPath, ChdMediaFormatKind.Gdi, StatusCreateGdiKey, containerKind),
            ".toc" => PlanDescriptorCreateCd(inputPath, ChdMediaFormatKind.Toc, StatusCreateTocKey, containerKind),
            ".nrg" => PlanSingleFileCreateCd(inputPath, ChdMediaFormatKind.Nrg, StatusCreateNrgKey, containerKind),
            ".iso" => PlanIsoCreate(inputPath, isoCreateCommandOverride, containerKind),
            ".cso" => PlanCsoCreateDvd(inputPath, containerKind),
            ".chd" => ChdWorkflowProfilePlan.Unsupported(ChdAlreadyChdMessageKey, containerKind),
            ".zip" or ".rar" or ".7z" => ChdWorkflowProfilePlan.Unsupported(ArchiveRequiresExtractionMessageKey, ChdMediaContainerKind.Archive),
            ".bin" => PlanStandaloneBinCreate(inputPath, containerKind),
            ".cdi" => ChdWorkflowProfilePlan.Unsupported(UnsupportedMessageKey, containerKind),
            ".raw" => ChdWorkflowProfilePlan.Unsupported(RawStandaloneBlockedMessageKey, containerKind),
            _ => ChdWorkflowProfilePlan.Unsupported(UnsupportedMessageKey, containerKind)
        };
    }

    private static ChdWorkflowProfilePlan PlanStandaloneBinCreate(
        string inputPath,
        ChdMediaContainerKind containerKind)
    {
        MediaInputDecision mediaDecision = global::HakamiqChdTool.App.Services.MediaInputPolicy.MediaInputPolicy.Evaluate(inputPath);
        if (mediaDecision.IsRedirectedToCue)
        {
            return PlanDescriptorCreateCd(mediaDecision.EffectivePath, ChdMediaFormatKind.Cue, StatusCreateCueKey, containerKind);
        }

        if (mediaDecision.RequiresTemporaryCue)
        {
            return new ChdWorkflowProfilePlan
            {
                IsSupported = true,
                ContainerKind = containerKind,
                MediaKind = ChdMediaFormatKind.Cue,
                ProfileKind = ChdWorkflowProfileKind.CreateCd,
                Command = "createcd",
                OutputExtension = ".chd",
                StatusLine = StatusCreateCueKey
            };
        }

        return ChdWorkflowProfilePlan.Unsupported(
            string.IsNullOrWhiteSpace(mediaDecision.MessageKey)
                ? BinStandaloneBlockedMessageKey
                : mediaDecision.MessageKey,
            containerKind);
    }

    private static ChdWorkflowProfilePlan PlanRequestedPlatformProfile(
        string inputPath,
        string extension,
        ChdPlatformProfile profile,
        ChdMediaContainerKind containerKind)
    {
        if (!ChdPlatformProfiles.SupportsExtension(profile, extension))
        {
            return ChdWorkflowProfilePlan.Unsupported(UnsupportedMessageKey, containerKind);
        }

        if (profile.RequiresToc)
        {
            if (!TryValidateExistingDescriptorDependencies(inputPath, out string failureMessage))
            {
                return ChdWorkflowProfilePlan.Unsupported(failureMessage, containerKind);
            }
        }

        if (profile.CommandKind == ChdCommandKind.CreateDvd
            && profile.PreparationKind == ChdInputPreparationKind.None
            && !TryValidateIsoPlausible(inputPath, out string isoFailureMessage))
        {
            return ChdWorkflowProfilePlan.Unsupported(isoFailureMessage, containerKind);
        }

        bool createDvd = profile.CommandKind == ChdCommandKind.CreateDvd;
        return new ChdWorkflowProfilePlan
        {
            IsSupported = true,
            ContainerKind = containerKind,
            MediaKind = extension switch
            {
                ".iso" => ChdMediaFormatKind.Iso,
                ".gdi" => ChdMediaFormatKind.Gdi,
                ".cue" => ChdMediaFormatKind.Cue,
                ".cso" => ChdMediaFormatKind.Cso,
                _ => ChdMediaFormatKind.Unknown
            },
            ProfileKind = createDvd ? ChdWorkflowProfileKind.CreateDvd : ChdWorkflowProfileKind.CreateCd,
            Command = createDvd ? "createdvd" : "createcd",
            StatusLine = ResolveProfileStatusLine(profile, extension),
            PlatformProfile = profile
        };
    }

    private static string ResolveProfileStatusLine(ChdPlatformProfile profile, string extension)
    {
        if (profile.PreparationKind == ChdInputPreparationKind.ExpandCsoToIso
            || string.Equals(extension, ".cso", StringComparison.OrdinalIgnoreCase))
        {
            return StatusPrepareCsoToIsoKey;
        }

        if (profile == ChdPlatformProfiles.DreamcastGdi || string.Equals(extension, ".gdi", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCreateGdiKey;
        }

        if (profile.CommandKind == ChdCommandKind.CreateDvd)
        {
            return StatusCreateIsoDvdKey;
        }

        return StatusCreateCueKey;
    }

    private static ChdWorkflowProfilePlan PlanCsoCreateDvd(
        string inputPath,
        ChdMediaContainerKind containerKind)
    {
        ValidateInputReadableOrThrow(inputPath);

        return new ChdWorkflowProfilePlan
        {
            IsSupported = true,
            ContainerKind = containerKind,
            MediaKind = ChdMediaFormatKind.Cso,
            ProfileKind = ChdWorkflowProfileKind.CreateDvd,
            Command = "createdvd",
            StatusLine = StatusPrepareCsoToIsoKey,
            PlatformProfile = ChdPlatformProfiles.PspCso
        };
    }

    public static ChdWorkflowProfilePlan PlanVerifyChd(string inputPath)
    {
        if (!string.Equals(Path.GetExtension(inputPath), ".chd", StringComparison.OrdinalIgnoreCase))
        {
            return ChdWorkflowProfilePlan.Unsupported(VerifyChdOnlyMessageKey);
        }

        return new ChdWorkflowProfilePlan
        {
            IsSupported = true,
            ContainerKind = ChdMediaContainerKind.DirectFile,
            MediaKind = ChdMediaFormatKind.Chd,
            ProfileKind = ChdWorkflowProfileKind.Verify,
            Command = "verify",
            StatusLine = StatusVerifyChdKey
        };
    }

    public static ChdWorkflowProfilePlan PlanExtractionByKind(ChdmanExtractionKind extractionKind) => extractionKind switch
    {
        ChdmanExtractionKind.ExtractCd => BuildExtractionPlan(ChdMediaFormatKind.CdChd, ChdWorkflowProfileKind.ExtractCd, "extractcd", ChdmanExtractionKind.ExtractCd, ".cue", StatusExtractCdKey),
        ChdmanExtractionKind.ExtractDvd => BuildExtractionPlan(ChdMediaFormatKind.DvdChd, ChdWorkflowProfileKind.ExtractDvd, "extractdvd", ChdmanExtractionKind.ExtractDvd, ".iso", StatusExtractDvdKey),
        ChdmanExtractionKind.ExtractHd => BuildExtractionPlan(ChdMediaFormatKind.HdChd, ChdWorkflowProfileKind.ExtractHd, "extracthd", ChdmanExtractionKind.ExtractHd, ".img", StatusExtractHdKey),
        ChdmanExtractionKind.ExtractRaw => BuildExtractionPlan(ChdMediaFormatKind.RawChd, ChdWorkflowProfileKind.ExtractRaw, "extractraw", ChdmanExtractionKind.ExtractRaw, ".raw", StatusExtractRawKey),
        _ => ChdWorkflowProfilePlan.Unsupported(UnknownChdExtractionMessageKey)
    };

    public static ChdWorkflowProfilePlan PlanExtractionFromChdMediaType(
        string? mediaType,
        string? chdPath = null,
        PlatformDetectionResult? platformDetection = null)
    {
        if (string.IsNullOrWhiteSpace(mediaType) || string.Equals(mediaType, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return ChdWorkflowProfilePlan.Unsupported(UnknownChdExtractionMessageKey);
        }

        string normalizedMediaType = mediaType.Trim();
        if (IsRawChdMediaType(normalizedMediaType))
        {
            return TryGetRawExtractionBlockReason(chdPath, platformDetection, out string blockReason)
                ? ChdWorkflowProfilePlan.Unsupported(blockReason)
                : PlanExtractionByKind(ChdmanExtractionKind.ExtractRaw);
        }

        return normalizedMediaType switch
        {
            "GD-ROM" => PlanExtractionByKind(ChdmanExtractionKind.ExtractCd),
            "CD-ROM" => PlanExtractionByKind(ChdmanExtractionKind.ExtractCd),
            "DVD-ROM" => PlanExtractionByKind(ChdmanExtractionKind.ExtractDvd),
            "HD-ROM" or "Hard Disk" => PlanExtractionByKind(ChdmanExtractionKind.ExtractHd),
            _ => ChdWorkflowProfilePlan.Unsupported(UnknownChdExtractionMessageKey)
        };
    }

    public static bool TryGetRawExtractionBlockReason(
        string? chdPath,
        PlatformDetectionResult? platformDetection,
        out string reason)
    {
        reason = string.Empty;

        if (platformDetection is not null
            && IsStrongDiscConsoleEvidence(platformDetection, out bool isPlayStation))
        {
            reason = isPlayStation ? RawDiscConsoleConflictMessageKey : RawExtractionBlockedMessageKey;
            return true;
        }

        string candidateText = BuildChdIdentityText(chdPath);
        if (candidateText.Length == 0)
        {
            return false;
        }

        if (ContainsPlayStationSerial(candidateText))
        {
            reason = RawDiscConsoleConflictMessageKey;
            return true;
        }

        if (ContainsDiscConsoleKeyword(candidateText))
        {
            reason = RawExtractionBlockedMessageKey;
            return true;
        }

        return false;
    }

    public static bool TryBuildRawMetadataConflictDetection(
        string? chdPath,
        PlatformDetectionResult? keywordDetection,
        out PlatformDetectionResult conflictDetection)
    {
        conflictDetection = PlatformDetectionResult.Create(
            L(RawMediaImageNameKey),
            string.Empty,
            68,
            L(RawMetadataConflictReasonKey));

        if (!TryGetRawExtractionBlockReason(chdPath, keywordDetection, out _))
        {
            return false;
        }

        string identityText = BuildChdIdentityText(chdPath);
        if (ContainsPlayStationSerial(identityText))
        {
            conflictDetection = PlatformDetectionResult.Create(
                "Sony PlayStation",
                L(RawMetadataConflictTitleKey),
                80,
                L(RawMetadataPlayStationConflictReasonKey));
            return true;
        }

        if (keywordDetection is not null
            && IsStrongDiscConsoleEvidence(keywordDetection, out bool isPlayStation)
            && isPlayStation)
        {
            conflictDetection = PlatformDetectionResult.Create(
                "Sony PlayStation",
                L(RawMetadataConflictTitleKey),
                82,
                L(RawMetadataPlayStationConflictReasonKey));
            return true;
        }

        conflictDetection = PlatformDetectionResult.Create(
            L(RawMediaImageNameKey),
            L(RawMetadataConflictTitleKey),
            60,
            L(RawMetadataSafetyHintReasonKey));
        return true;
    }

    public static void ValidateInputReadableOrThrow(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException(InvalidInputPathMessageKey, nameof(inputPath));
        }

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException(InputFileNotFoundMessageKey, inputPath);
        }

        try
        {
            using FileStream stream = File.Open(inputPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            _ = stream.CanRead;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException(IsoReadFailedMessageKey, ex);
        }
    }

    public static void ValidateConversionInputOrThrow(string inputPath, string command)
    {
        ValidateInputReadableOrThrow(inputPath);

        string extension = Path.GetExtension(inputPath).ToLowerInvariant();
        if (string.Equals(command, "createdvd", StringComparison.OrdinalIgnoreCase) && extension == ".iso")
        {
            ValidateIsoPlausibleOrThrow(inputPath);
            return;
        }

        if (string.Equals(command, "createcd", StringComparison.OrdinalIgnoreCase) && extension is ".cue" or ".gdi" or ".toc")
        {
            ValidateDescriptorDependenciesOrThrow(inputPath);
        }
    }

    public static void ValidateDescriptorDependenciesOrThrow(string descriptorPath)
    {
        string extension = Path.GetExtension(descriptorPath).ToLowerInvariant();
        if (extension is not (".cue" or ".gdi" or ".toc"))
        {
            return;
        }

        if (!TryValidateExistingDescriptorDependencies(descriptorPath, out string failureMessage))
        {
            throw new InvalidDataException(failureMessage);
        }
    }

    public static bool TryValidateExistingDescriptorDependencies(string descriptorPath, out string failureMessage)
    {
        failureMessage = string.Empty;
        string extension = Path.GetExtension(descriptorPath).ToLowerInvariant();
        if (extension is not (".cue" or ".gdi" or ".toc"))
        {
            return true;
        }

        string? directory = Path.GetDirectoryName(descriptorPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            failureMessage = MissingDescriptorDependenciesMessageKey;
            return false;
        }

        string descriptorText;
        try
        {
            descriptorText = ReadSmallDescriptorText(descriptorPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            failureMessage = DescriptorReadFailedMessageKey;
            return false;
        }

        IEnumerable<string> availableFiles = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories);
        if (!ArchiveCandidateDiscovery.TryValidateDescriptorDependencies(descriptorPath, descriptorText, availableFiles, out failureMessage))
        {
            failureMessage = string.IsNullOrWhiteSpace(failureMessage)
                ? MissingDescriptorDependenciesMessageKey
                : failureMessage;
            return false;
        }

        return true;
    }

    public static bool IsRawChdMediaType(string mediaType) =>
        string.Equals(mediaType, "Raw", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "RAW", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "Raw Disk", StringComparison.OrdinalIgnoreCase);

    private static string BuildChdIdentityText(string? chdPath)
    {
        if (string.IsNullOrWhiteSpace(chdPath))
        {
            return string.Empty;
        }

        string fileName = Path.GetFileNameWithoutExtension(chdPath);
        string directory = Path.GetDirectoryName(chdPath) ?? string.Empty;
        return NormalizeIdentityText(fileName + " " + directory);
    }

    private static string NormalizeIdentityText(string value) =>
        value.ToLowerInvariant()
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Replace('.', ' ');

    private static bool ContainsPlayStationSerial(string identityText)
    {
        if (string.IsNullOrWhiteSpace(identityText))
        {
            return false;
        }

        return Regex.IsMatch(
            identityText,
            @"\b(slus|scus|sles|sces|slpm|slps|slka|papx|pcpx|espm)[\s._-]*\d{3,5}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout);
    }

    private static bool ContainsDiscConsoleKeyword(string identityText)
    {
        if (string.IsNullOrWhiteSpace(identityText))
        {
            return false;
        }

        return ContainsAny(
            identityText,
            "playstation",
            "ps1",
            "psx",
            "ps one",
            "sega saturn",
            "saturn",
            "sega cd",
            "mega cd",
            "neo geo cd",
            "pc engine cd",
            "turbografx cd",
            "turbo grafx cd",
            "dreamcast",
            "3do");
    }

    private static bool IsStrongDiscConsoleEvidence(PlatformDetectionResult detection, out bool isPlayStation)
    {
        isPlayStation = false;
        if (!IsDiscConsolePlatformName(detection.PlatformName, out bool detectedPlayStation))
        {
            return false;
        }

        if (!IsStrongPlatformEvidence(detection))
        {
            return false;
        }

        isPlayStation = detectedPlayStation;
        return true;
    }

    private static bool IsStrongPlatformEvidence(PlatformDetectionResult detection)
    {
        if (detection.ConfidenceScore < 80)
        {
            return false;
        }

        string reason = detection.Reason ?? string.Empty;
        if (IsWeakPlatformDetectionReason(reason))
        {
            return false;
        }

        string platform = detection.PlatformName ?? string.Empty;
        return !platform.Contains('/', StringComparison.Ordinal);
    }

    private static bool IsWeakPlatformDetectionReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return true;
        }

        return string.Equals(reason, "LocPlatformDetect_PathHint", StringComparison.Ordinal)
            || reason.Contains("PathHint", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("FilenameHint", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("FileNameHint", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDiscConsolePlatformName(string? platformName, out bool isPlayStation)
    {
        isPlayStation = false;
        if (string.IsNullOrWhiteSpace(platformName))
        {
            return false;
        }

        isPlayStation = platformName.Contains("PlayStation 1", StringComparison.OrdinalIgnoreCase)
            || platformName.Contains("PlayStation 2", StringComparison.OrdinalIgnoreCase)
            || platformName.Contains("PlayStation 3", StringComparison.OrdinalIgnoreCase)
            || platformName.Contains("Sony PlayStation", StringComparison.OrdinalIgnoreCase);

        return isPlayStation
            || platformName.Contains("SEGA Saturn", StringComparison.OrdinalIgnoreCase)
            || platformName.Contains("SEGA CD", StringComparison.OrdinalIgnoreCase)
            || platformName.Contains("Mega CD", StringComparison.OrdinalIgnoreCase)
            || platformName.Contains("Neo Geo CD", StringComparison.OrdinalIgnoreCase)
            || platformName.Contains("PC Engine", StringComparison.OrdinalIgnoreCase)
            || platformName.Contains("TurboGrafx", StringComparison.OrdinalIgnoreCase)
            || platformName.Contains("Dreamcast", StringComparison.OrdinalIgnoreCase)
            || platformName.Contains("3DO", StringComparison.OrdinalIgnoreCase)
            || platformName.Contains("CD-Based", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldBlockIsoChdConversion(
        string inputPath,
        IsoChdmanCreateDiagnostics diagnostics,
        out string messageKey)
    {
        messageKey = string.Empty;

        if (diagnostics.FileLengthBytes > DiscMediaKindResolver.SafeDvdIsoUpperBoundBytes
            && !HasBluRayOrPs3IsoEvidence(inputPath))
        {
            messageKey = NonChdRecommendedCompressionMessageKey;
            return true;
        }

        return false;
    }

    private static bool HasBluRayOrPs3IsoEvidence(string inputPath)
    {
        try
        {
            var analyzer = new BluRayIsoAnalysisService();
            return analyzer.TryAnalyze(inputPath, out BluRayIsoAnalysisResult? result, BluRayAnalysisProfile.Quick)
                && result is not null
                && result.LooksLikeBluRayStyleIso;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException or InvalidDataException or OperationCanceledException or OverflowException)
        {
            return false;
        }
    }

    private static bool ContainsAny(string source, params string[] values)
    {
        foreach (string value in values)
        {
            if (source.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static ChdWorkflowProfilePlan PlanDescriptorCreateCd(
        string inputPath,
        ChdMediaFormatKind mediaKind,
        string statusLine,
        ChdMediaContainerKind containerKind)
    {
        if (!TryValidateExistingDescriptorDependencies(inputPath, out string failureMessage))
        {
            return ChdWorkflowProfilePlan.Unsupported(failureMessage, containerKind);
        }

        return new ChdWorkflowProfilePlan
        {
            IsSupported = true,
            ContainerKind = containerKind,
            MediaKind = mediaKind,
            ProfileKind = ChdWorkflowProfileKind.CreateCd,
            Command = "createcd",
            StatusLine = statusLine
        };
    }

    private static ChdWorkflowProfilePlan PlanSingleFileCreateCd(
        string inputPath,
        ChdMediaFormatKind mediaKind,
        string statusLine,
        ChdMediaContainerKind containerKind)
    {
        return new ChdWorkflowProfilePlan
        {
            IsSupported = true,
            ContainerKind = containerKind,
            MediaKind = mediaKind,
            ProfileKind = ChdWorkflowProfileKind.CreateCd,
            Command = "createcd",
            StatusLine = statusLine
        };
    }

    private static ChdWorkflowProfilePlan PlanIsoCreate(
        string inputPath,
        IsoCreateCommandOverride isoCreateCommandOverride,
        ChdMediaContainerKind containerKind)
    {
        if (!TryValidateIsoPlausible(inputPath, out string failureMessage))
        {
            return ChdWorkflowProfilePlan.Unsupported(failureMessage, containerKind);
        }
        IsoChdmanCreateDiagnostics diagnostics = IsoChdmanCreateCommandResolver.ResolveCreateCompressionCommandWithDiagnostics(inputPath, isoCreateCommandOverride);
        if (ShouldBlockIsoChdConversion(inputPath, diagnostics, out string blockMessageKey))
        {
            return ChdWorkflowProfilePlan.Unsupported(blockMessageKey, containerKind);
        }

        if (string.Equals(diagnostics.Command, "createcd", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(diagnostics.AutoSuggestedCommand, "createcd", StringComparison.OrdinalIgnoreCase))
        {
            return ChdWorkflowProfilePlan.Unsupported(UnknownIsoAsCdBlockedMessageKey, containerKind);
        }

        ChdWorkflowProfileKind profileKind = string.Equals(diagnostics.Command, "createcd", StringComparison.OrdinalIgnoreCase)
            ? ChdWorkflowProfileKind.CreateCd
            : ChdWorkflowProfileKind.CreateDvd;

        string statusLine = profileKind == ChdWorkflowProfileKind.CreateCd
            ? StatusCreateIsoCdKey
            : StatusCreateIsoDvdKey;

        return new ChdWorkflowProfilePlan
        {
            IsSupported = true,
            ContainerKind = containerKind,
            MediaKind = ChdMediaFormatKind.Iso,
            ProfileKind = profileKind,
            Command = profileKind == ChdWorkflowProfileKind.CreateCd ? "createcd" : "createdvd",
            StatusLine = statusLine,
            IsoDiagnostics = diagnostics
        };
    }


    private static ChdWorkflowProfilePlan BuildExtractionPlan(
        ChdMediaFormatKind mediaKind,
        ChdWorkflowProfileKind profileKind,
        string command,
        ChdmanExtractionKind extractionKind,
        string outputExtension,
        string statusLine) => new()
        {
            IsSupported = true,
            ContainerKind = ChdMediaContainerKind.DirectFile,
            MediaKind = mediaKind,
            ProfileKind = profileKind,
            Command = command,
            ExtractionKind = extractionKind,
            OutputExtension = outputExtension,
            StatusLine = statusLine
        };

    private static void ValidateIsoPlausibleOrThrow(string isoPath)
    {
        if (!TryValidateIsoPlausible(isoPath, out string failureMessage))
        {
            throw new InvalidDataException(failureMessage);
        }
    }

    private static bool TryValidateIsoPlausible(string isoPath, out string failureMessage)
    {
        failureMessage = string.Empty;
        try
        {
            FileInfo info = new(isoPath);
            const long minimumIsoBytes = 32L * 1024;
            if (info.Length < minimumIsoBytes)
            {
                failureMessage = IsoTooSmallMessageKey;
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            failureMessage = IsoReadFailedMessageKey;
            return false;
        }
    }

    private static string ReadSmallDescriptorText(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        FileInfo fileInfo = new(path);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException(InputFileNotFoundMessageKey, path);
        }

        if (fileInfo.Length > MaxDescriptorTextBytes)
        {
            throw new InvalidOperationException(DescriptorReadFailedMessageKey);
        }

        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using StreamReader reader = new(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: false);
        return reader.ReadToEnd();
    }

    private static string L(string key) => ArabicUi.ResolveDisplayString(key);
}
