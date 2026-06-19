using System;
using HakamiqChdTool.App.Core.Chd.Profiles;
using HakamiqChdTool.App.Models;

namespace HakamiqChdTool.App.Services;

public enum ChdProfileMediaKind
{
    Unknown = 0,
    CdRom = 1,
    DvdRom = 2,
    HardDisk = 3
}

public enum TargetEmulatorProfile
{
    Auto = 0,
    GenericChd = 1,
    DuckStation = 2,
    PCSX2 = 3,
    PPSSPP = 4
}

public enum ChdProfileUserGoal
{
    Auto = 0,
    Compatibility = 1,
    Balanced = 2,
    SmallestSize = 3,
    FastestConversion = 4,
    Advanced = 5
}

public sealed record PlatformAwareChdProfileRequest(
    ChdProfileMediaKind MediaKind,
    string Platform,
    ChdMediaFormatKind InputFormat,
    TargetEmulatorProfile TargetEmulatorProfile,
    ChdProfileUserGoal UserGoal,
    ChdmanCapabilitySnapshot ChdmanCapabilities,
    string CurrentCommand,
    string InputPath,
    string? RequestedCompression,
    int RequestedHunkSizeBytes);

public sealed record PlatformAwareChdProfileDecision(
    string Command,
    string Compression,
    int HunkSize,
    string EffectiveProfileName,
    string CompatibilityWarningCode,
    string ReasonCode,
    string CompressionPolicyName = "",
    string HunkPolicyName = "")
{
    public bool HasCommand => !string.IsNullOrWhiteSpace(Command);
}

public interface IPlatformAwareChdProfilePolicy
{
    PlatformAwareChdProfileDecision Resolve(PlatformAwareChdProfileRequest request);
}

public sealed class PlatformAwareChdProfilePolicy : IPlatformAwareChdProfilePolicy
{
    public const string GenericCdProfileName = "Generic CD createcd";
    public const string GenericDvdProfileName = "Generic DVD createdvd";
    public const string PspPpssppProfileName = "PPSSPP PSP ISO createdvd 2048";
    public const string PspCsoPreparedProfileName = "PSP CSO prepared ISO createdvd";
    public const string Ps2DvdProfileName = "PCSX2 PS2 DVD ISO createdvd";
    public const string HardDiskProfileName = "Generic HD createhd";

    public const string PspPpssppIsoReasonCode = "ChdPolicyReason_PspPpssppIsoCreatedvd2048";
    public const string Ps2DvdIsoReasonCode = "ChdPolicyReason_Ps2DvdIsoCreatedvd";
    public const string CdDescriptorReasonCode = "ChdPolicyReason_CdDescriptorCreateCd";
    public const string GdiReasonCode = "ChdPolicyReason_GdiCreateCd";
    public const string IsoCdReasonCode = "ChdPolicyReason_IsoMediaKindCdCreateCd";
    public const string IsoDvdReasonCode = "ChdPolicyReason_IsoMediaKindDvdCreateDvd";
    public const string UnknownIsoReasonCode = "ChdPolicyReason_UnknownIsoMediaKindBlocked";
    public const string ExistingCommandReasonCode = "ChdPolicyReason_ExistingCommandPreserved";
    public const string CanonicalProfileReasonCode = "ChdPolicyReason_CanonicalProfileResolved";
    public const string CsoPreparationRequiredReasonCode = "ChdPolicyReason_CsoPreparationRequired";

    public const string UnknownIsoMediaKindRequiredMessageKey = "LocChdPolicy_UnknownIsoMediaKindRequired";
    public const string CreateDvdUnsupportedMessageKey = "LocChdPolicy_CreateDvdUnsupported";

    private readonly ChdCdCompressionPolicy _cdCompressionPolicy = new();
    private readonly ChdDvdCompressionPolicy _dvdCompressionPolicy = new();
    private readonly ChdCdHunkPolicy _cdHunkPolicy = new();
    private readonly ChdDvdHunkPolicy _dvdHunkPolicy = new();

    public PlatformAwareChdProfileDecision Resolve(PlatformAwareChdProfileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (TryResolveCanonicalCreateProfile(request, out ChdPlatformProfile? canonicalProfile)
            && canonicalProfile is not null
            && !ShouldPreserveIsoCdCommand(request, canonicalProfile))
        {
            return BuildCanonicalProfileDecision(request, canonicalProfile);
        }

        return ResolveLegacyNonProfileDecision(request);
    }

    private PlatformAwareChdProfileDecision BuildCanonicalProfileDecision(
        PlatformAwareChdProfileRequest request,
        ChdPlatformProfile profile)
    {
        string command = ChdPlatformProfiles.ToCommandName(profile.CommandKind);
        ChdDiscProfileSettings settings = profile.CommandKind == ChdCommandKind.CreateDvd
            ? ResolveDvdProfileSettings(request, ResolveDvdHunkIntent(profile))
            : ResolveCdProfileSettings(request, ResolveCdHunkIntent(profile));

        return Build(
            command,
            settings,
            BuildProfileName(profile),
            profile.CommandKind == ChdCommandKind.CreateDvd
                ? BuildCreateDvdCapabilityWarning(request.ChdmanCapabilities)
                : string.Empty,
            ResolveCanonicalReasonCode(profile));
    }

    private PlatformAwareChdProfileDecision ResolveLegacyNonProfileDecision(PlatformAwareChdProfileRequest request)
    {
        bool isIso = request.InputFormat == ChdMediaFormatKind.Iso;

        if (isIso)
        {
            return request.MediaKind switch
            {
                ChdProfileMediaKind.CdRom => Build(
                    "createcd",
                    ResolveCdProfileSettings(request, ChdDiscHunkIntent.IsoCd),
                    GenericCdProfileName,
                    string.Empty,
                    IsoCdReasonCode),

                ChdProfileMediaKind.DvdRom => Build(
                    "createdvd",
                    ResolveDvdProfileSettings(request, ChdDiscHunkIntent.GenericDvd),
                    GenericDvdProfileName,
                    BuildCreateDvdCapabilityWarning(request.ChdmanCapabilities),
                    IsoDvdReasonCode),

                ChdProfileMediaKind.HardDisk => Build(
                    "createhd",
                    ChdDiscProfileSettings.Empty,
                    HardDiskProfileName,
                    string.Empty,
                    ExistingCommandReasonCode),

                _ => Build(
                    string.Empty,
                    ChdDiscProfileSettings.Empty,
                    string.Empty,
                    UnknownIsoMediaKindRequiredMessageKey,
                    UnknownIsoReasonCode)
            };
        }

        return BuildExistingCommandDecision(request);
    }

    private static bool TryResolveCanonicalCreateProfile(
        PlatformAwareChdProfileRequest request,
        out ChdPlatformProfile? profile)
    {
        profile = null;

        string inputPath = ResolveCanonicalInputPath(request);
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return false;
        }

        profile = ChdPlatformProfiles.ResolveForInput(
            inputPath,
            BuildCanonicalPlatformHint(request));

        return profile is not null;
    }

    private static string ResolveCanonicalInputPath(PlatformAwareChdProfileRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.InputPath))
        {
            return request.InputPath;
        }

        return request.InputFormat switch
        {
            ChdMediaFormatKind.Iso => "input.iso",
            ChdMediaFormatKind.Cue => "input.cue",
            ChdMediaFormatKind.Gdi => "input.gdi",
            ChdMediaFormatKind.Toc => "input.toc",
            ChdMediaFormatKind.Nrg => "input.nrg",
            _ => string.Empty
        };
    }

    private static string BuildCanonicalPlatformHint(PlatformAwareChdProfileRequest request)
    {
        string platform = request.Platform?.Trim() ?? string.Empty;
        TargetEmulatorProfile targetProfile = ResolveTargetProfile(platform, request.TargetEmulatorProfile);

        string targetHint = targetProfile switch
        {
            TargetEmulatorProfile.PPSSPP => "PPSSPP PSP",
            TargetEmulatorProfile.PCSX2 => "PCSX2 PS2",
            TargetEmulatorProfile.DuckStation => "DuckStation PlayStation",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(targetHint))
        {
            return platform;
        }

        if (string.IsNullOrWhiteSpace(platform))
        {
            return targetHint;
        }

        return $"{platform} {targetHint}";
    }

    private static bool ShouldPreserveIsoCdCommand(
        PlatformAwareChdProfileRequest request,
        ChdPlatformProfile profile) =>
        profile == ChdPlatformProfiles.GenericDvd
        && request.InputFormat == ChdMediaFormatKind.Iso
        && request.MediaKind == ChdProfileMediaKind.CdRom
        && string.Equals(request.CurrentCommand, "createcd", StringComparison.OrdinalIgnoreCase);

    private static ChdDiscHunkIntent ResolveCdHunkIntent(ChdPlatformProfile profile)
    {
        if (profile == ChdPlatformProfiles.DreamcastGdi)
        {
            return ChdDiscHunkIntent.Gdi;
        }

        return ChdDiscHunkIntent.CdDescriptor;
    }

    private static ChdDiscHunkIntent ResolveDvdHunkIntent(ChdPlatformProfile profile)
    {
        if (profile == ChdPlatformProfiles.PspIso)
        {
            return ChdDiscHunkIntent.PspPpssppDvd;
        }

        if (profile == ChdPlatformProfiles.PspCso)
        {
            return ChdDiscHunkIntent.GenericDvd;
        }

        if (profile == ChdPlatformProfiles.Ps2Dvd)
        {
            return ChdDiscHunkIntent.Ps2Dvd;
        }

        return ChdDiscHunkIntent.GenericDvd;
    }

    private static string BuildProfileName(ChdPlatformProfile profile)
    {
        if (profile == ChdPlatformProfiles.PspIso)
        {
            return PspPpssppProfileName;
        }

        if (profile == ChdPlatformProfiles.PspCso)
        {
            return PspCsoPreparedProfileName;
        }

        if (profile == ChdPlatformProfiles.Ps2Dvd)
        {
            return Ps2DvdProfileName;
        }

        if (profile.CommandKind == ChdCommandKind.CreateCd)
        {
            return GenericCdProfileName;
        }

        if (profile.CommandKind == ChdCommandKind.CreateDvd)
        {
            return GenericDvdProfileName;
        }

        return profile.DisplayName;
    }

    private static string ResolveCanonicalReasonCode(ChdPlatformProfile profile)
    {
        if (profile == ChdPlatformProfiles.PspIso)
        {
            return PspPpssppIsoReasonCode;
        }

        if (profile == ChdPlatformProfiles.PspCso)
        {
            return CsoPreparationRequiredReasonCode;
        }

        if (profile == ChdPlatformProfiles.Ps2Dvd)
        {
            return Ps2DvdIsoReasonCode;
        }

        if (profile == ChdPlatformProfiles.DreamcastGdi)
        {
            return GdiReasonCode;
        }

        if (profile.CommandKind == ChdCommandKind.CreateCd)
        {
            return CdDescriptorReasonCode;
        }

        if (profile.CommandKind == ChdCommandKind.CreateDvd)
        {
            return IsoDvdReasonCode;
        }

        return CanonicalProfileReasonCode;
    }

    private ChdDiscProfileSettings ResolveCdProfileSettings(
        PlatformAwareChdProfileRequest request,
        ChdDiscHunkIntent hunkIntent) => new(
            _cdCompressionPolicy.Resolve(request.RequestedCompression, request.UserGoal),
            _cdHunkPolicy.Resolve(request.RequestedHunkSizeBytes, request.UserGoal, hunkIntent),
            _cdCompressionPolicy.PolicyName,
            _cdHunkPolicy.PolicyName);

    private ChdDiscProfileSettings ResolveDvdProfileSettings(
        PlatformAwareChdProfileRequest request,
        ChdDiscHunkIntent hunkIntent) => new(
            _dvdCompressionPolicy.Resolve(request.RequestedCompression, request.UserGoal),
            _dvdHunkPolicy.Resolve(request.RequestedHunkSizeBytes, request.UserGoal, hunkIntent),
            _dvdCompressionPolicy.PolicyName,
            _dvdHunkPolicy.PolicyName);

    private PlatformAwareChdProfileDecision BuildExistingCommandDecision(PlatformAwareChdProfileRequest request)
    {
        if (string.Equals(request.CurrentCommand, "createcd", StringComparison.OrdinalIgnoreCase))
        {
            return Build(
                request.CurrentCommand,
                ResolveCdProfileSettings(request, ChdDiscHunkIntent.ExistingCommand),
                GenericCdProfileName,
                string.Empty,
                ExistingCommandReasonCode);
        }

        if (string.Equals(request.CurrentCommand, "createdvd", StringComparison.OrdinalIgnoreCase))
        {
            return Build(
                request.CurrentCommand,
                ResolveDvdProfileSettings(request, ChdDiscHunkIntent.ExistingCommand),
                GenericDvdProfileName,
                BuildCreateDvdCapabilityWarning(request.ChdmanCapabilities),
                ExistingCommandReasonCode);
        }

        return Build(
            request.CurrentCommand,
            ChdDiscProfileSettings.Empty,
            ResolveExistingProfileName(request.CurrentCommand),
            BuildExistingCapabilityWarning(request.CurrentCommand, request.ChdmanCapabilities),
            ExistingCommandReasonCode);
    }

    private static PlatformAwareChdProfileDecision Build(
        string command,
        ChdDiscProfileSettings settings,
        string effectiveProfileName,
        string compatibilityWarningCode,
        string reasonCode) => new(
            command?.Trim() ?? string.Empty,
            settings.Compression?.Trim() ?? string.Empty,
            settings.HunkSize,
            effectiveProfileName?.Trim() ?? string.Empty,
            compatibilityWarningCode?.Trim() ?? string.Empty,
            reasonCode?.Trim() ?? string.Empty,
            settings.CompressionPolicyName?.Trim() ?? string.Empty,
            settings.HunkPolicyName?.Trim() ?? string.Empty);

    private static TargetEmulatorProfile ResolveTargetProfile(string platform, TargetEmulatorProfile requested)
    {
        if (requested != TargetEmulatorProfile.Auto)
        {
            return requested;
        }

        if (IsPspPlatform(platform))
        {
            return TargetEmulatorProfile.PPSSPP;
        }

        if (IsPlayStation2(platform))
        {
            return TargetEmulatorProfile.PCSX2;
        }

        if (platform.Contains("PlayStation", StringComparison.OrdinalIgnoreCase)
            || platform.Contains("PS1", StringComparison.OrdinalIgnoreCase)
            || platform.Contains("PSX", StringComparison.OrdinalIgnoreCase))
        {
            return TargetEmulatorProfile.DuckStation;
        }

        return TargetEmulatorProfile.GenericChd;
    }

    private static bool IsPspPlatform(string platform) =>
        platform.Contains("PlayStation Portable", StringComparison.OrdinalIgnoreCase)
        || platform.Contains("Sony PSP", StringComparison.OrdinalIgnoreCase)
        || platform.Contains("PPSSPP", StringComparison.OrdinalIgnoreCase)
        || platform.Contains("PSP", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlayStation2(string platform) =>
        platform.Contains("PlayStation 2", StringComparison.OrdinalIgnoreCase)
        || platform.Contains("Sony PlayStation 2", StringComparison.OrdinalIgnoreCase)
        || platform.Contains("PCSX2", StringComparison.OrdinalIgnoreCase)
        || platform.Contains("PS2", StringComparison.OrdinalIgnoreCase);

    private static string ResolveExistingProfileName(string command)
    {
        if (string.Equals(command, "createcd", StringComparison.OrdinalIgnoreCase))
        {
            return GenericCdProfileName;
        }

        if (string.Equals(command, "createdvd", StringComparison.OrdinalIgnoreCase))
        {
            return GenericDvdProfileName;
        }

        if (string.Equals(command, "createhd", StringComparison.OrdinalIgnoreCase))
        {
            return HardDiskProfileName;
        }

        return string.IsNullOrWhiteSpace(command) ? string.Empty : command.Trim();
    }

    private static string BuildExistingCapabilityWarning(string command, ChdmanCapabilitySnapshot capabilities) =>
        string.Equals(command, "createdvd", StringComparison.OrdinalIgnoreCase)
            ? BuildCreateDvdCapabilityWarning(capabilities)
            : string.Empty;

    private static string BuildCreateDvdCapabilityWarning(ChdmanCapabilitySnapshot capabilities) =>
        capabilities.IsAvailable && !capabilities.SupportsCreateDvd
            ? CreateDvdUnsupportedMessageKey
            : string.Empty;
}
