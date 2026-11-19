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

    // ZSO (LZ4 compressed ISO) is used for PS2 (OPL) and PSP images. CsoKit 0.6.4+
    // expands it to a temporary ISO; the result is handled like a generic DVD ISO.
    public static readonly ChdPlatformProfile CompressedZso = new(
        PlatformId: "compressed-zso",
        DisplayName: "ZSO compressed ISO",
        InputExtensions: [".zso"],
        CommandKind: ChdCommandKind.CreateDvd,
        PreparationKind: ChdInputPreparationKind.ExpandZsoToIso,
        RequiresToc: false,
        RequiresDvdSectorAlignment: true,
        HunkSize: null,
        Note: "ZSO must be expanded to temporary ISO before CHD conversion.");

    // CloneCD: the .ccd control file describes a raw 2352-byte .img. A temporary CUE is
    // generated for chdman; the .sub subchannel file is not carried over.
    public static readonly ChdPlatformProfile CloneCd = new(
        PlatformId: "clonecd-ccd",
        DisplayName: "CloneCD image",
        InputExtensions: [".ccd"],
        CommandKind: ChdCommandKind.CreateCd,
        PreparationKind: ChdInputPreparationKind.GenerateCueFromCcd,
        RequiresToc: false,
        RequiresDvdSectorAlignment: false,
        HunkSize: null,
        Note: "CloneCD .ccd/.img. A temporary CUE is generated; subchannel data is not kept.");

    // Alcohol 120%: the .mds descriptor describes the .mdf. A CD gets a temporary CUE for
    // createcd; a DVD .mdf is a plain 2048-byte image for createdvd. Subchannel data is not kept.
    public static readonly ChdPlatformProfile AlcoholCd = new(
        PlatformId: "alcohol-mds-cd",
        DisplayName: "Alcohol 120% CD image",
        InputExtensions: [".mds"],
        CommandKind: ChdCommandKind.CreateCd,
        PreparationKind: ChdInputPreparationKind.GenerateCueFromMds,
        RequiresToc: false,
        RequiresDvdSectorAlignment: false,
        HunkSize: null,
        Note: "Alcohol 120% .mds/.mdf CD. A temporary CUE is generated; subchannel data is not kept.");

    public static readonly ChdPlatformProfile AlcoholDvd = new(
        PlatformId: "alcohol-mds-dvd",
        DisplayName: "Alcohol 120% DVD image",
        InputExtensions: [".mds"],
        CommandKind: ChdCommandKind.CreateDvd,
        PreparationKind: ChdInputPreparationKind.StageIsoFromMds,
        RequiresToc: false,
        RequiresDvdSectorAlignment: false,
        HunkSize: null,
        Note: "Alcohol 120% .mds/.mdf DVD. The .mdf is staged as a temporary ISO.");

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

    // Never resolved automatically: raw hard-disk images carry no magic, so the
    // user must select this profile explicitly before createhd is used.
    public static readonly ChdPlatformProfile GenericHardDisk = new(
        PlatformId: "generic-hd",
        DisplayName: "Hard disk image",
        InputExtensions: [".img", ".hdd"],
        CommandKind: ChdCommandKind.CreateHd,
        PreparationKind: ChdInputPreparationKind.None,
        RequiresToc: false,
        RequiresDvdSectorAlignment: false,
        HunkSize: null,
        Note: "Raw hard-disk image. Use createhd; chdman defaults are preserved.");

    public static IReadOnlyList<ChdPlatformProfile> All { get; } =
    [
        GenericCd,
        GenericDvd,
        GenericHardDisk,
        Ps1,
        SegaSaturn,
        DreamcastGdi,
        PspIso,
        PspCso,
        CompressedZso,
        CloneCd,
        AlcoholCd,
        AlcoholDvd,
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
            ".zso" => CompressedZso,
            ".ccd" => CloneCd,
            ".mds" => AlcoholCd,
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

        // An Alcohol .mds is a CD or a DVD; the planner reads the descriptor and picks the command.
        if (string.Equals(Path.GetExtension(inputPath), ".mds", StringComparison.OrdinalIgnoreCase)
            && string.Equals(command, "createdvd", StringComparison.OrdinalIgnoreCase))
        {
            return AlcoholDvd;
        }

        if (string.Equals(command, "createcd", StringComparison.OrdinalIgnoreCase))
        {
            return GenericCd;
        }

        if (string.Equals(command, "createdvd", StringComparison.OrdinalIgnoreCase))
        {
            return GenericDvd;
        }

        if (string.Equals(command, "createhd", StringComparison.OrdinalIgnoreCase))
        {
            return GenericHardDisk;
        }

        return null;
    }

    public static bool SupportsExtension(ChdPlatformProfile profile, string extension) =>
        profile.InputExtensions.Any(candidate =>
            string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase));

    public static string ToCommandName(ChdCommandKind commandKind) =>
        commandKind switch
        {
            ChdCommandKind.CreateDvd => "createdvd",
            ChdCommandKind.CreateHd => "createhd",
            _ => "createcd"
        };

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