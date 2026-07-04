using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HakamiqChdTool.App.Core.Chd.Profiles;

public static class ChdPlatformProfiles
{
    public const string AutoDetectPlatformId = "auto";

    public static readonly ChdPlatformProfile GenericCd = new(
        PlatformId: "generic-cd",
        DisplayName: "Generic CD",
        InputExtensions: [".cue", ".gdi", ".toc", ".nrg"],
        CommandKind: ChdCommandKind.CreateCd,
        PreparationKind: ChdInputPreparationKind.None,
        RequiresToc: true,
        RequiresDvdSectorAlignment: false,
        HunkSize: null,
        Note: "Generic CD-style image. Use createcd.");

    public static readonly ChdPlatformProfile GenericDvd = new(
        PlatformId: "generic-dvd",
        DisplayName: "Generic DVD",
        InputExtensions: [".iso"],
        CommandKind: ChdCommandKind.CreateDvd,
        PreparationKind: ChdInputPreparationKind.None,
        RequiresToc: false,
        RequiresDvdSectorAlignment: true,
        HunkSize: null,
        Note: "Generic DVD-style 2048-byte sector image. Use createdvd.");

    public static readonly ChdPlatformProfile Ps1 = new(
        PlatformId: "sony-ps1",
        DisplayName: "Sony PlayStation",
        InputExtensions: [".cue"],
        CommandKind: ChdCommandKind.CreateCd,
        PreparationKind: ChdInputPreparationKind.None,
        RequiresToc: true,
        RequiresDvdSectorAlignment: false,
        HunkSize: null,
        Note: "CD-based image. Use createcd from CUE.");

    public static readonly ChdPlatformProfile SegaSaturn = new(
        PlatformId: "sega-saturn",
        DisplayName: "Sega Saturn",
        InputExtensions: [".cue"],
        CommandKind: ChdCommandKind.CreateCd,
        PreparationKind: ChdInputPreparationKind.None,
        RequiresToc: true,
        RequiresDvdSectorAlignment: false,
        HunkSize: null,
        Note: "CD-based image. Use createcd from CUE.");

    public static readonly ChdPlatformProfile DreamcastGdi = new(
        PlatformId: "sega-dreamcast-gdi",
        DisplayName: "Sega Dreamcast",
        InputExtensions: [".gdi"],
        CommandKind: ChdCommandKind.CreateCd,
        PreparationKind: ChdInputPreparationKind.None,
        RequiresToc: true,
        RequiresDvdSectorAlignment: false,
        HunkSize: null,
        Note: "GD-ROM layout. Prefer GDI input.");

    public static readonly ChdPlatformProfile PspIso = new(
        PlatformId: "sony-psp-iso",
        DisplayName: "Sony PlayStation Portable ISO",
        InputExtensions: [".iso"],
        CommandKind: ChdCommandKind.CreateDvd,
        PreparationKind: ChdInputPreparationKind.None,
        RequiresToc: false,
        RequiresDvdSectorAlignment: true,
        HunkSize: null,
        Note: "PSP ISO uses createdvd. Default chdman DVD hunk behavior is preserved.");

    public static readonly ChdPlatformProfile PspCso = new(
        PlatformId: "sony-psp-cso",
        DisplayName: "Sony PlayStation Portable CSO",
        InputExtensions: [".cso"],
        CommandKind: ChdCommandKind.CreateDvd,
        PreparationKind: ChdInputPreparationKind.ExpandCsoToIso,
        RequiresToc: false,
        RequiresDvdSectorAlignment: true,
        HunkSize: null,
        Note: "CSO must be expanded to temporary ISO before CHD conversion.");

    public static readonly ChdPlatformProfile Ps2Dvd = new(
        PlatformId: "sony-ps2-dvd",
        DisplayName: "Sony PlayStation 2 DVD",
        InputExtensions: [".iso"],
        CommandKind: ChdCommandKind.CreateDvd,
        PreparationKind: ChdInputPreparationKind.None,
        RequiresToc: false,
        RequiresDvdSectorAlignment: true,
        HunkSize: null,
        Note: "DVD-based PS2 image. Use createdvd.");

    public static readonly ChdPlatformProfile Ps2Cd = new(
        PlatformId: "sony-ps2-cd",
        DisplayName: "Sony PlayStation 2 CD",
        InputExtensions: [".cue"],
        CommandKind: ChdCommandKind.CreateCd,
        PreparationKind: ChdInputPreparationKind.None,
        RequiresToc: true,
        RequiresDvdSectorAlignment: false,
        HunkSize: null,
        Note: "CD-based PS2 image. Use createcd.");

    public static IReadOnlyList<ChdPlatformProfile> All { get; } =
    [
        GenericCd,
        GenericDvd,
        Ps1,
        SegaSaturn,
        DreamcastGdi,
        PspIso,
        PspCso,
        Ps2Dvd,
        Ps2Cd
    ];

    public static ChdPlatformProfile? FindById(string? platformId)
    {
        if (string.IsNullOrWhiteSpace(platformId)
            || string.Equals(platformId, AutoDetectPlatformId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return All.FirstOrDefault(profile =>
            string.Equals(profile.PlatformId, platformId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static ChdPlatformProfile? ResolveForInput(
        string inputPath,
        string? detectedPlatform,
        string? requestedPlatformId = null)
    {
        ChdPlatformProfile? requested = FindById(requestedPlatformId);
        string extension = Path.GetExtension(inputPath).ToLowerInvariant();

        if (requested is not null && SupportsExtension(requested, extension))
        {
            return requested;
        }

        return extension switch
        {
            ".gdi" => DreamcastGdi,
            ".cso" => PspCso,
            ".iso" => ResolveIsoProfile(detectedPlatform),
            ".cue" => ResolveCueProfile(detectedPlatform),
            ".toc" => GenericCd,
            ".nrg" => GenericCd,
            _ => null
        };
    }

    public static ChdPlatformProfile? ResolveForCommand(
        string command,
        string inputPath,
        string? detectedPlatform,
        string? requestedPlatformId = null)
    {
        ChdPlatformProfile? profile = ResolveForInput(inputPath, detectedPlatform, requestedPlatformId);
        if (profile is not null
            && string.Equals(ToCommandName(profile.CommandKind), command, StringComparison.OrdinalIgnoreCase))
        {
            return profile;
        }

        if (string.Equals(command, "createcd", StringComparison.OrdinalIgnoreCase))
        {
            return GenericCd;
        }

        if (string.Equals(command, "createdvd", StringComparison.OrdinalIgnoreCase))
        {
            return GenericDvd;
        }

        return null;
    }

    public static bool SupportsExtension(ChdPlatformProfile profile, string extension) =>
        profile.InputExtensions.Any(candidate =>
            string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase));

    public static string ToCommandName(ChdCommandKind commandKind) =>
        commandKind == ChdCommandKind.CreateDvd ? "createdvd" : "createcd";

    private static ChdPlatformProfile ResolveIsoProfile(string? detectedPlatform)
    {
        string platform = detectedPlatform ?? string.Empty;

        if (ContainsAny(platform, "PlayStation Portable", "Sony PSP", "PPSSPP", "PSP"))
        {
            return PspIso;
        }

        if (ContainsAny(platform, "PlayStation 2", "Sony PlayStation 2", "PCSX2", "PS2"))
        {
            return Ps2Dvd;
        }

        return GenericDvd;
    }

    private static ChdPlatformProfile ResolveCueProfile(string? detectedPlatform)
    {
        string platform = detectedPlatform ?? string.Empty;

        if (ContainsAny(platform, "Saturn", "SEGA Saturn", "Sega Saturn"))
        {
            return SegaSaturn;
        }

        if (ContainsAny(platform, "PlayStation 2", "Sony PlayStation 2", "PCSX2", "PS2"))
        {
            return Ps2Cd;
        }

        if (ContainsAny(platform, "PlayStation", "Sony PlayStation", "PS1", "PSX", "DuckStation"))
        {
            return Ps1;
        }

        return GenericCd;
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
}