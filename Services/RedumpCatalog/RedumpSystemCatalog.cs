using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace HakamiqChdTool.App.Services.RedumpCatalog;

internal static class RedumpSystemCatalog
{
    private static readonly Uri PrimaryRoot = new("https://redump.org/", UriKind.Absolute);
    private static readonly Uri MirrorRoot = new("https://old.redump.info/", UriKind.Absolute);

    private static readonly RedumpSystemCatalogEntry[] EntriesValue =
    [
        Disc("psx", "LocRedumpCatalog_System_psx", "Sony PlayStation", "CD-ROM", dumperOnly: false, dat: true, cue: true, sbi: true, bios: true),
        Disc("ps2", "LocRedumpCatalog_System_ps2", "Sony PlayStation 2", "CD-ROM, DVD-5, DVD-9", dumperOnly: false, dat: true, cue: true, bios: true),
        Disc("ps4", "LocRedumpCatalog_System_ps4", "Sony PlayStation 4", "BD-25, BD-50", dumperOnly: true, dat: true),
        Disc("ps5", "LocRedumpCatalog_System_ps5", "Sony PlayStation 5", "(UHD)BD-66, (UHD)BD-100", dumperOnly: true, dat: true),
        Disc("psp", "LocRedumpCatalog_System_psp", "Sony PlayStation Portable", "UMD SL, UMD DL, DVD-5", dumperOnly: false, dat: true),

        Disc("mcd", "LocRedumpCatalog_System_mcd", "Sega Mega CD & Sega CD", "CD-ROM", dumperOnly: false, dat: true, cue: true),
        Disc("ss", "LocRedumpCatalog_System_ss", "Sega Saturn", "CD-ROM", dumperOnly: false, dat: true, cue: true),
        Disc("dc", "LocRedumpCatalog_System_dc", "Sega Dreamcast", "GD-ROM, MIL-CD", dumperOnly: false, dat: true, cue: true, gdi: true),
        Disc("naomi", "LocRedumpCatalog_System_naomi", "Sega Naomi", "GD-ROM", dumperOnly: false, dat: true, cue: true, gdi: true),
        Disc("naomi2", "LocRedumpCatalog_System_naomi2", "Sega Naomi 2", "GD-ROM", dumperOnly: false, dat: true, cue: true, gdi: true),
        Disc("chihiro", "LocRedumpCatalog_System_chihiro", "Sega Chihiro", "GD-ROM", dumperOnly: false, dat: true, cue: true, gdi: true),
        Disc("trf", "LocRedumpCatalog_System_trf", "Namco · Sega · Nintendo Triforce", "GD-ROM", dumperOnly: false, dat: true, cue: true, gdi: true),

        Disc("gc", "LocRedumpCatalog_System_gc", "Nintendo GameCube", "GCN Disc", dumperOnly: false, dat: true, bios: true),
        Disc("wii", "LocRedumpCatalog_System_wii", "Nintendo Wii", "Wii SL, Wii DL", dumperOnly: false, dat: true),
        Disc("wiiu", "LocRedumpCatalog_System_wiiu", "Nintendo Wii U", "Wii U SL", dumperOnly: true, dat: true, discKey: true),

        Disc("xbox", "LocRedumpCatalog_System_xbox", "Microsoft Xbox", "CD-ROM, DVD-5, DVD-9", dumperOnly: false, dat: true, cue: true, bios: true),
        Disc("xbox360", "LocRedumpCatalog_System_xbox360", "Microsoft Xbox 360", "CD-ROM, DVD-5, DVD-9", dumperOnly: false, dat: true, cue: true),
        Disc("xboxone", "LocRedumpCatalog_System_xboxone", "Microsoft Xbox One", "BD-25, BD-50", dumperOnly: true, dat: true),
        Disc("xboxsx", "LocRedumpCatalog_System_xboxsx", "Microsoft Xbox Series X", "BD-25, BD-50", dumperOnly: true, dat: true),

        Disc("pc", "LocRedumpCatalog_System_pc", "IBM PC compatible", "CD-ROM, DVD-5, DVD-9, BD-25, BD-50", dumperOnly: false, dat: true, cue: true, sbi: true),
        Disc("pce", "LocRedumpCatalog_System_pce", "NEC PC Engine CD & TurboGrafx CD", "CD-ROM", dumperOnly: false, dat: true, cue: true),
        Disc("pc-fx", "LocRedumpCatalog_System_pcfx", "NEC PC-FX & PC-FXGA", "CD-ROM", dumperOnly: false, dat: true, cue: true),
        Disc("ngcd", "LocRedumpCatalog_System_ngcd", "Neo Geo CD", "CD-ROM", dumperOnly: false, dat: true, cue: true),
        Disc("3do", "LocRedumpCatalog_System_3do", "Panasonic 3DO Interactive Multiplayer", "CD-ROM", dumperOnly: false, dat: true, cue: true),
        Disc("cdi", "LocRedumpCatalog_System_cdi", "Philips CD-i", "CD-ROM", dumperOnly: false, dat: true, cue: true),
        Disc("fmt", "LocRedumpCatalog_System_fmt", "Fujitsu FM Towns series", "CD-ROM", dumperOnly: false, dat: true, cue: true),
        Disc("x68k", "LocRedumpCatalog_System_x68k", "Sharp X68000", "CD-ROM", dumperOnly: false, dat: true, cue: true),

        Disc("bd-video", "LocRedumpCatalog_System_bdvideo", "BD-Video", "BD-25, BD-50", dumperOnly: true, dat: true),
        Disc("dvd-video", "LocRedumpCatalog_System_dvdvideo", "DVD-Video", "DVD-5, DVD-9", dumperOnly: true, dat: true),
        Disc("audio-cd", "LocRedumpCatalog_System_audiocd", "Audio CD", "CD-ROM", dumperOnly: true, dat: true, cue: true),
        Disc("vcd", "LocRedumpCatalog_System_vcd", "Video CD", "CD-ROM", dumperOnly: true, dat: true, cue: true),

        NonRedump("cps3", "LocRedumpCatalog_System_cps3", "Capcom Play System III", "CD-ROM"),
        NonRedump("kp2", "LocRedumpCatalog_System_kp2", "Konami Python 2", "DVD-5, DVD-9"),
        NonRedump("msxcd", "LocRedumpCatalog_System_msxcd", "Microsoft MSX", "CD-ROM"),
        NonRedump("salls", "LocRedumpCatalog_System_salls", "Sega ALLS", "DVD-5, DVD-9"),
        NonRedump("snu", "LocRedumpCatalog_System_snu", "Sega Nu", "DVD-5, DVD-9"),
        NonRedump("snu2", "LocRedumpCatalog_System_snu2", "Sega Nu 2", "DVD-5, DVD-9"),

        Defunct("cdi-video", "LocRedumpCatalog_System_cdivideo", "Philips CD-i Digital Video", "CD-ROM"),
        Defunct("iktv", "LocRedumpCatalog_System_iktv", "Tao iKTV", "CD-ROM")
    ];

    public static IReadOnlyList<RedumpSystemCatalogEntry> Entries { get; } =
        new ReadOnlyCollection<RedumpSystemCatalogEntry>(EntriesValue);

    public static bool TryGetByKey(string? key, out RedumpSystemCatalogEntry? entry)
    {
        string? normalizedKey = string.IsNullOrWhiteSpace(key)
            ? null
            : key.Trim();

        entry = normalizedKey is null
            ? null
            : Entries.FirstOrDefault(candidate =>
                string.Equals(candidate.Key, normalizedKey, StringComparison.OrdinalIgnoreCase));

        return entry is not null;
    }

    public static IReadOnlyList<RedumpSystemCatalogEntry> CurrentRedumpSystems =>
    [
        .. Entries.Where(static entry => !entry.IsNonRedump && !entry.IsDefunct)
    ];

    public static IReadOnlyList<RedumpSystemCatalogEntry> NonRedumpSystems =>
    [
        .. Entries.Where(static entry => entry.IsNonRedump)
    ];

    public static IReadOnlyList<RedumpSystemCatalogEntry> DefunctSystems =>
    [
        .. Entries.Where(static entry => entry.IsDefunct)
    ];

    public static IReadOnlyList<RedumpCatalogOption> BuildPlatformOptions()
    {
        List<RedumpCatalogOption> result =
        [
            new RedumpCatalogOption(
                "Auto",
                "LocRedumpCatalog_Platform_Auto_Label",
                "LocRedumpCatalog_Platform_Auto_Description"),
            new RedumpCatalogOption(
                "Unknown",
                "LocRedumpCatalog_Platform_Unknown_Label",
                "LocRedumpCatalog_Platform_Unknown_Description")
        ];

        foreach (RedumpSystemCatalogEntry entry in CurrentRedumpSystems.OrderBy(static entry => entry.LabelKey, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(new RedumpCatalogOption(
                entry.Key,
                entry.LabelKey,
                entry.LabelKey,
                technicalDescription: BuildTechnicalDescription(entry)));
        }

        return result;
    }

    public static IReadOnlyList<RedumpCatalogOption> BuildArtifactOptions(string? platformKey)
    {
        List<RedumpCatalogOption> result =
        [
            new RedumpCatalogOption(
                RedumpArtifactKind.Datfile.ToString(),
                ToArtifactLabelKey(RedumpArtifactKind.Datfile),
                "LocRedumpCatalog_Artifact_Datfile_DefaultDescription")
        ];

        if (!TryGetByKey(platformKey, out RedumpSystemCatalogEntry? entry) || entry is null)
        {
            return result;
        }

        result.Clear();

        foreach (RedumpArtifactEndpoint endpoint in entry.Artifacts)
        {
            result.Add(new RedumpCatalogOption(
                endpoint.Kind.ToString(),
                ToArtifactLabelKey(endpoint.Kind),
                ToArtifactDescriptionKey(endpoint.Kind),
                endpoint.PrimaryUri.AbsoluteUri,
                endpoint.Kind is RedumpArtifactKind.DiscKey or RedumpArtifactKind.Subchannel));
        }

        return result.Count == 0
            ? [
                new RedumpCatalogOption(
                    RedumpArtifactKind.Datfile.ToString(),
                    ToArtifactLabelKey(RedumpArtifactKind.Datfile),
                    "LocRedumpCatalog_Artifact_NoCustomLinks")
              ]
            : result;
    }

    public static string ResolveEndpointUrl(string? platformKey, string? artifactKind)
    {
        if (!TryGetByKey(platformKey, out RedumpSystemCatalogEntry? entry) || entry is null)
        {
            return string.Empty;
        }

        if (!Enum.TryParse(artifactKind, ignoreCase: true, out RedumpArtifactKind parsedKind)
            || !Enum.IsDefined(parsedKind))
        {
            return string.Empty;
        }

        RedumpArtifactEndpoint? endpoint = entry.Artifacts.FirstOrDefault(endpoint =>
            endpoint.Kind == parsedKind);

        return endpoint?.PrimaryUri.AbsoluteUri ?? string.Empty;
    }

    private static string ToArtifactLabelKey(RedumpArtifactKind kind)
    {
        return kind switch
        {
            RedumpArtifactKind.Datfile => "LocRedumpCatalog_Artifact_Datfile_Label",
            RedumpArtifactKind.Cuesheet => "LocRedumpCatalog_Artifact_Cuesheet_Label",
            RedumpArtifactKind.Gdi => "LocRedumpCatalog_Artifact_Gdi_Label",
            RedumpArtifactKind.Subchannel => "LocRedumpCatalog_Artifact_Subchannel_Label",
            RedumpArtifactKind.DiscKey => "LocRedumpCatalog_Artifact_DiscKey_Label",
            RedumpArtifactKind.BiosDatfile => "LocRedumpCatalog_Artifact_BiosDatfile_Label",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    private static string ToArtifactDescriptionKey(RedumpArtifactKind kind)
    {
        return kind switch
        {
            RedumpArtifactKind.Datfile => "LocRedumpCatalog_Artifact_Datfile_Description",
            RedumpArtifactKind.Cuesheet => "LocRedumpCatalog_Artifact_Cuesheet_Description",
            RedumpArtifactKind.Gdi => "LocRedumpCatalog_Artifact_Gdi_Description",
            RedumpArtifactKind.Subchannel => "LocRedumpCatalog_Artifact_Subchannel_Description",
            RedumpArtifactKind.DiscKey => "LocRedumpCatalog_Artifact_DiscKey_Description",
            RedumpArtifactKind.BiosDatfile => "LocRedumpCatalog_Artifact_BiosDatfile_Description",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    private static RedumpSystemCatalogEntry Disc(
        string key,
        string labelKey,
        string englishName,
        string mediaTypes,
        bool dumperOnly,
        bool dat = false,
        bool cue = false,
        bool gdi = false,
        bool sbi = false,
        bool discKey = false,
        bool bios = false)
    {
        return new RedumpSystemCatalogEntry(
            key,
            labelKey,
            englishName,
            mediaTypes,
            dumperOnly,
            false,
            false,
            BuildArtifacts(key, dat, cue, gdi, sbi, discKey, bios));
    }

    private static RedumpSystemCatalogEntry NonRedump(
        string key,
        string labelKey,
        string englishName,
        string mediaTypes)
    {
        return new RedumpSystemCatalogEntry(
            key,
            labelKey,
            englishName,
            mediaTypes,
            false,
            true,
            false,
            []);
    }

    private static RedumpSystemCatalogEntry Defunct(
        string key,
        string labelKey,
        string englishName,
        string mediaTypes)
    {
        return new RedumpSystemCatalogEntry(
            key,
            labelKey,
            englishName,
            mediaTypes,
            true,
            false,
            true,
            []);
    }

    private static RedumpArtifactEndpoint[] BuildArtifacts(
        string key,
        bool dat,
        bool cue,
        bool gdi,
        bool sbi,
        bool discKey,
        bool bios)
    {
        List<RedumpArtifactEndpoint> result = [];

        if (dat)
        {
            result.Add(Endpoint(RedumpArtifactKind.Datfile, $"datfile/{key}/"));
        }

        if (cue)
        {
            result.Add(Endpoint(RedumpArtifactKind.Cuesheet, $"cues/{key}/"));
        }

        if (gdi)
        {
            result.Add(Endpoint(RedumpArtifactKind.Gdi, $"gdi/{key}/"));
        }

        if (sbi)
        {
            result.Add(Endpoint(RedumpArtifactKind.Subchannel, $"sbi/{key}/"));
        }

        if (discKey)
        {
            result.Add(Endpoint(RedumpArtifactKind.DiscKey, $"keys/{key}/"));
        }

        if (bios)
        {
            result.Add(Endpoint(RedumpArtifactKind.BiosDatfile, $"datfile/{key}-bios/"));
        }

        return [.. result];
    }

    private static RedumpArtifactEndpoint Endpoint(RedumpArtifactKind kind, string relativePath)
    {
        return new RedumpArtifactEndpoint(
            kind,
            new Uri(PrimaryRoot, relativePath),
            new Uri(MirrorRoot, relativePath));
    }

    private static string BuildTechnicalDescription(RedumpSystemCatalogEntry entry)
    {
        return $"{entry.EnglishName} — {entry.MediaTypes}";
    }
}