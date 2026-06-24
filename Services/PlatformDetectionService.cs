using HakamiqChdTool.App.Core.Disc;
using HakamiqChdTool.App.Models;
using DiscUtils.Iso9660;
using HakamiqChdTool.App.Models.PlayStation.BluRayAnalysis;
using HakamiqChdTool.App.Services.PlayStation.BluRayAnalysis;
using HakamiqChdTool.App.Services.ConsoleMedia;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HakamiqChdTool.App.Services;

public static class PlatformDetectionService
{
    private const long GameCubeDiscSize = 1_459_978_240L;
    private const long WiiSingleLayerSize = 4_699_979_776L;
    private const long WiiDualLayerSize = 8_511_160_320L;
    private const int MaxIsoTextProbeBytes = 256 * 1024;

    private const string InvalidPathOrFileReasonKey = "LocPlatformDetect_InvalidPathOrFile";
    private const string GdiExtensionReasonKey = "LocPlatformDetect_GdiExtension";
    private const string TocExtensionReasonKey = "LocPlatformDetect_TocExtension";
    private const string NrgExtensionReasonKey = "LocPlatformDetect_NrgExtension";
    private const string CdiExtensionReasonKey = "LocPlatformDetect_CdiExtension";
    private const string CsoExtensionReasonKey = "LocPlatformDetect_CsoExtension";
    private const string ChdContainerOnlyReasonKey = "LocPlatformDetect_ChdContainerOnly";
    private const string CueSbiReasonKey = "LocPlatformDetect_CueSbi";
    private const string CueMultiTrackPathHintReasonKey = "LocPlatformDetect_CueMultiTrackPathHint";
    private const string CueMultiTrackAmbiguousReasonKey = "LocPlatformDetect_CueMultiTrackAmbiguous";
    private const string XboxDefaultXbeReasonKey = "LocPlatformDetect_XboxDefaultXbe";
    private const string GameCubeSizeReasonKey = "LocPlatformDetect_GameCubeSize";
    private const string WiiSizeReasonKey = "LocPlatformDetect_WiiSize";
    private const string NonIsoGameCubeSizeReasonKey = "LocPlatformDetect_NonIsoGameCubeSize";
    private const string NonIsoWiiSizeReasonKey = "LocPlatformDetect_NonIsoWiiSize";
    private const string PathHintReasonKey = "LocPlatformDetect_PathHint";
    private const string WeakGuessReasonKey = "LocPlatformDetect_WeakGuess";
    private const string PspStructureReasonKey = "LocDiscProbe_PspStructure";
    private const string Ps2SystemCnfReasonKey = "LocDiscProbe_SystemCnfPs2Boot2";
    private const string Ps1SystemCnfReasonKey = "LocDiscProbe_SystemCnfPs1Hint";
    private const string Ps3BluRayStructureReasonKey = "LocDiscProbe_Ps3BluRayStructure";

    private static readonly ILogger Logger = global::Serilog.Log.ForContext(typeof(PlatformDetectionService));

    public static string DetectPlatform(string path) => Detect(path).PlatformName;


    public static bool IsActionablePlatformName(string? platformName)
    {
        if (string.IsNullOrWhiteSpace(platformName))
        {
            return false;
        }

        string platform = platformName.Trim();

        if (platform.Contains("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !IsMediaOnlyPlatformName(platform)
            && !IsSourceFormatPlatformName(platform);
    }

    public static bool IsOrganizablePlatformName(string? platformName)
    {
        if (!IsActionablePlatformName(platformName))
        {
            return false;
        }

        string platform = platformName!.Trim();
        return !platform.Contains('/', StringComparison.Ordinal)
            && !platform.Contains(" or ", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMediaOnlyPlatformName(string? platformName)
    {
        if (string.IsNullOrWhiteSpace(platformName))
        {
            return true;
        }

        string platform = platformName.Trim();

        return string.Equals(platform, "CD", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "DVD", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "HD", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "CHD", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "CD-ROM", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "DVD-ROM", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "GD-ROM", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "HD-ROM", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "Hard Disk", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "Raw Media Image", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSourceFormatPlatformName(string platform)
    {
        return string.Equals(platform, "CUE/BIN", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "CUE - BIN", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "ISO", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "GDI", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "TOC", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "NRG", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "BIN", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "IMG", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "RAW", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "ZIP/7Z/RAR", StringComparison.OrdinalIgnoreCase);
    }

    public static PlatformDetectionResult Detect(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PlatformDetectionResult.Create(string.Empty, string.Empty, 10, InvalidPathOrFileReasonKey);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            Logger.Debug(ex, "Platform detection rejected an invalid path. Path={Path}", path);
            return PlatformDetectionResult.Create(string.Empty, string.Empty, 10, InvalidPathOrFileReasonKey);
        }
        if (Directory.Exists(fullPath))
        {
            return PlatformDetectionResult.Create(string.Empty, string.Empty, 10, InvalidPathOrFileReasonKey);
        }

        if (!File.Exists(fullPath))
        {
            return PlatformDetectionResult.Create(string.Empty, string.Empty, 10, InvalidPathOrFileReasonKey);
        }

        string extension = Path.GetExtension(fullPath).ToLowerInvariant();

        if (extension == ".iso")
        {
            return PlatformCandidateFilter.Apply(fullPath, DetectFromIso(fullPath));
        }

        if (extension == ".bin")
        {
            ConsoleDiscIdentityResult consoleIdentity = ConsoleDiscIdentityService.Shared.Detect(fullPath);
            if (consoleIdentity.IsIdentified)
            {
                return PlatformCandidateFilter.Apply(
                    fullPath,
                    PlatformDetectionResult.Create(
                        consoleIdentity.PlatformName,
                        string.Empty,
                        consoleIdentity.Confidence,
                        consoleIdentity.ReasonKey));
            }
        }

        if (DiscRawSerialProbe.TryDetectPlatform(fullPath, out PlatformDetectionResult rawSerialDetection))
        {
            return PlatformCandidateFilter.Apply(fullPath, rawSerialDetection);
        }

        PlatformDetectionResult result = extension switch
        {
            ".gdi" => PlatformDetectionResult.Create("SEGA Dreamcast", string.Empty, 95, GdiExtensionReasonKey),
            ".toc" => DetectFromPathKeywords(fullPath, string.Empty),
            ".nrg" => DetectFromPathKeywords(fullPath, string.Empty),
            ".cdi" => PlatformDetectionResult.Create("SEGA Dreamcast", string.Empty, 70, CdiExtensionReasonKey),
            ".cso" => PlatformDetectionResult.Create("PlayStation Portable", string.Empty, 95, CsoExtensionReasonKey),
            ".cue" => DetectFromCue(fullPath),
            ".chd" => DetectFromPathKeywords(fullPath, string.Empty),
            ".zip" or ".rar" or ".7z" => DetectFromPathKeywords(fullPath, string.Empty),
            _ => DetectFromPathKeywords(fullPath, string.Empty)
        };

        return PlatformCandidateFilter.Apply(fullPath, result);
    }

    private static PlatformDetectionResult DetectFromCue(string cuePath)
    {
        PlatformDetectionResult pathHint = DetectFromPathKeywords(cuePath, string.Empty);
        if (IsActionablePlatformName(pathHint.PlatformName)
            && pathHint.ConfidenceScore >= 60)
        {
            return pathHint;
        }

        if (DiscMetadataProbe.TryDetectPlatform(cuePath, out PlatformDetectionResult metadataDetection))
        {
            return metadataDetection;
        }

        try
        {
            string cueDirectory = Path.GetDirectoryName(cuePath) ?? string.Empty;
            string cueBaseName = Path.GetFileNameWithoutExtension(cuePath);
            bool hasSbi = File.Exists(Path.Combine(cueDirectory, $"{cueBaseName}.sbi"));

            int trackCount = 0;
            var referencedFiles = new List<string>();

            foreach (string line in ReadTextLines(cuePath))
            {
                if (line.AsSpan().TrimStart().StartsWith("TRACK ".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    trackCount++;
                }

                string? reference = ParseCueFileReference(line);
                if (!string.IsNullOrWhiteSpace(reference))
                {
                    referencedFiles.Add(reference);
                }
            }

            int binCount = referencedFiles.Count(file =>
                string.Equals(Path.GetExtension(file), ".bin", StringComparison.OrdinalIgnoreCase));

            if (hasSbi)
            {
                return PlatformDetectionResult.Create("PlayStation 1", string.Empty, 96, CueSbiReasonKey);
            }

            if (binCount > 1 || trackCount >= 20)
            {
                if (IsActionablePlatformName(pathHint.PlatformName))
                {
                    return PlatformDetectionResult.Create(
                        pathHint.PlatformName,
                        string.Empty,
                        76,
                        CueMultiTrackPathHintReasonKey);
                }

                return PlatformDetectionResult.Create(
                    "PlayStation 1 / SEGA Saturn",
                    string.Empty,
                    72,
                    CueMultiTrackAmbiguousReasonKey);
            }

            return pathHint;
        }
        catch (Exception ex) when (IsExpectedReadException(ex) || IsExpectedPathException(ex))
        {
            Logger.Debug(ex, "Platform detection could not parse CUE; using path keywords. Path={Path}", cuePath);
            return pathHint;
        }
    }

    private static PlatformDetectionResult DetectFromIso(string isoPath)
    {
        if (DiscMetadataProbe.TryDetectPlatform(isoPath, out PlatformDetectionResult metadataDetection))
        {
            return metadataDetection;
        }

        try
        {
            long fileSize = new FileInfo(isoPath).Length;

            if (!LooksLikeIso9660Volume(isoPath))
            {
                if (TryDetectPs3BluRayIso(isoPath, out PlatformDetectionResult ps3BluRayRawDetection))
                {
                    return ps3BluRayRawDetection;
                }

                Logger.Information(
                    "Platform detection found no ISO9660 descriptor; using non-ISO fallback. Path={Path}",
                    isoPath);

                return DetectFromIsoFallback(isoPath, fileSize, string.Empty);
            }

            using FileStream stream = new(
                isoPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);

            using var cd = new CDReader(stream, true);

            if (TryDetectDefaultXbe(cd))
            {
                return PlatformDetectionResult.Create("Xbox", string.Empty, 96, XboxDefaultXbeReasonKey);
            }

            if (IsoFileExists(cd, @"\UMD_DATA.BIN") || IsoDirectoryExists(cd, @"\PSP_GAME"))
            {
                return PlatformDetectionResult.Create("PlayStation Portable", string.Empty, 96, PspStructureReasonKey);
            }

            if (IsoFileExists(cd, @"\SYSTEM.CNF"))
            {
                string systemCnf = ReadTextFileFromIso(cd, @"\SYSTEM.CNF");

                if (systemCnf.IndexOf("BOOT2", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return PlatformDetectionResult.Create("PlayStation 2", string.Empty, 96, Ps2SystemCnfReasonKey);
                }

                if (systemCnf.IndexOf("PSX.EXE", StringComparison.OrdinalIgnoreCase) >= 0
                    || systemCnf.IndexOf("CDROM:", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return PlatformDetectionResult.Create("PlayStation 1", string.Empty, 75, Ps1SystemCnfReasonKey);
                }
            }

            if (TryDetectPs3BluRayIso(isoPath, out PlatformDetectionResult ps3Detection))
            {
                return ps3Detection;
            }

            if (IsApproximately(fileSize, GameCubeDiscSize, 150L * 1024L * 1024L))
            {
                return PlatformDetectionResult.Create("Nintendo GameCube", string.Empty, 90, GameCubeSizeReasonKey);
            }

            if (IsApproximately(fileSize, WiiSingleLayerSize, 300L * 1024L * 1024L)
                || IsApproximately(fileSize, WiiDualLayerSize, 500L * 1024L * 1024L))
            {
                return PlatformDetectionResult.Create("Nintendo Wii", string.Empty, 90, WiiSizeReasonKey);
            }

            return DetectFromPathKeywords(isoPath, string.Empty);
        }
        catch (Exception ex) when (IsDiscUtilsInvalidFileSystemException(ex))
        {
            Logger.Warning(
                "Platform detection could not open ISO9660 filesystem; using fallback. Path={Path}, Exception={ExceptionType}",
                isoPath,
                ex.GetType().Name);

            return DetectFromIsoFallbackWithSafeSize(isoPath, string.Empty);
        }
        catch (Exception ex) when (IsExpectedReadException(ex) || IsExpectedPathException(ex) || IsDiscUtilsReadException(ex))
        {
            Logger.Debug(ex, "Platform detection could not read ISO; using fallback. Path={Path}", isoPath);
            return DetectFromIsoFallbackWithSafeSize(isoPath, string.Empty);
        }
    }

    private static bool TryDetectPs3BluRayIso(string isoPath, out PlatformDetectionResult detection)
    {
        detection = PlatformDetectionResult.Create(string.Empty, string.Empty, 10, WeakGuessReasonKey);

        try
        {
            var analyzer = new BluRayIsoAnalysisService();
            if (!analyzer.TryAnalyze(isoPath, out BluRayIsoAnalysisResult? result, BluRayAnalysisProfile.Quick)
                || result is null
                || !result.LooksLikePs3Disc)
            {
                return false;
            }

            detection = PlatformDetectionResult.Create(
                "PlayStation 3",
                result.Metadata.Title,
                result.Metadata.HasMinimumRequiredStructure ? 96 : 88,
                Ps3BluRayStructureReasonKey);
            return true;
        }
        catch (Exception ex) when (IsExpectedReadException(ex) || IsExpectedPathException(ex) || ex is InvalidDataException or OperationCanceledException or OverflowException)
        {
            Logger.Debug(ex, "Platform detection could not complete PS3/Blu-ray raw ISO probe. Path={Path}", isoPath);
            return false;
        }
    }

    private static PlatformDetectionResult DetectFromIsoFallbackWithSafeSize(string isoPath, string fallback)
    {
        try
        {
            long fileSize = new FileInfo(isoPath).Length;
            return DetectFromIsoFallback(isoPath, fileSize, fallback);
        }
        catch (Exception ex) when (IsExpectedReadException(ex) || IsExpectedPathException(ex))
        {
            Logger.Debug(ex, "Platform detection could not read file size during fallback. Path={Path}", isoPath);
            return DetectFromPathKeywords(isoPath, fallback);
        }
    }

    private static PlatformDetectionResult DetectFromIsoFallback(string isoPath, long fileSize, string fallback)
    {
        if (IsApproximately(fileSize, GameCubeDiscSize, 150L * 1024L * 1024L))
        {
            return PlatformDetectionResult.Create("Nintendo GameCube", string.Empty, 88, NonIsoGameCubeSizeReasonKey);
        }

        if (IsApproximately(fileSize, WiiSingleLayerSize, 300L * 1024L * 1024L)
            || IsApproximately(fileSize, WiiDualLayerSize, 500L * 1024L * 1024L))
        {
            return PlatformDetectionResult.Create("Nintendo Wii", string.Empty, 88, NonIsoWiiSizeReasonKey);
        }

        return DetectFromPathKeywords(isoPath, fallback);
    }

    private static bool LooksLikeIso9660Volume(string isoPath)
    {
        try
        {
            using FileStream stream = new(
                isoPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);

            return HasIso9660DescriptorAt(stream, 0x8001)
                || HasIso9660DescriptorAt(stream, 0x8801)
                || HasIso9660DescriptorAt(stream, 0x9001);
        }
        catch (Exception ex) when (IsExpectedReadException(ex) || IsExpectedPathException(ex))
        {
            Logger.Debug(ex, "Platform detection ISO9660 signature probe failed. Path={Path}", isoPath);
            return false;
        }
    }

    private static bool HasIso9660DescriptorAt(FileStream stream, long offset)
    {
        if (stream.Length < offset + 5)
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[5];
        stream.Position = offset;
        int read = stream.Read(buffer);

        return read == 5
            && buffer[0] == (byte)'C'
            && buffer[1] == (byte)'D'
            && buffer[2] == (byte)'0'
            && buffer[3] == (byte)'0'
            && buffer[4] == (byte)'1';
    }

    private static bool TryDetectDefaultXbe(CDReader cd)
    {
        if (IsoFileExists(cd, @"\DEFAULT.XBE"))
        {
            return true;
        }

        foreach (string path in cd.GetFiles(@"\", "*", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(Path.GetFileName(path), "DEFAULT.XBE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (string directory in cd.GetDirectories(@"\", "*", SearchOption.TopDirectoryOnly))
        {
            string candidate = CombineIsoPath(directory, "DEFAULT.XBE");
            if (IsoFileExists(cd, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsoFileExists(CDReader cd, string path) =>
        cd.FileExists(NormalizeIsoPath(path));

    private static bool IsoDirectoryExists(CDReader cd, string path) =>
        cd.DirectoryExists(NormalizeIsoPath(path));

    private static string CombineIsoPath(string directory, string fileName)
    {
        string normalizedDirectory = NormalizeIsoPath(directory).TrimEnd('\\');
        return normalizedDirectory + "\\" + fileName;
    }

    private static string NormalizeIsoPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "\\";
        }

        string normalized = path.Replace('/', '\\');
        return normalized.StartsWith('\\')
            ? normalized
            : "\\" + normalized;
    }

    private static PlatformDetectionResult DetectFromPathKeywords(string path, string fallback)
    {
        string normalized = NormalizePathHint(path);

        bool Has(params string[] keys) => ContainsPathHint(normalized, keys);

        if (Has("playstation 5", "ps5"))
        {
            return PlatformDetectionResult.Create("PlayStation 5", string.Empty, 68, PathHintReasonKey);
        }

        if (Has("playstation 4", "ps4"))
        {
            return PlatformDetectionResult.Create("PlayStation 4", string.Empty, 68, PathHintReasonKey);
        }

        if (Has(
            "playstation 3",
            "ps3",
            "sony 3",
            "sony three",
            "سوني 3",
            "سوني ٣",
            "سوني٣",
            "بلايستيشن 3",
            "بلايستيشن ٣",
            "بلايستيشن٣",
            "بلاي ستيشن 3",
            "بلاي ستيشن ٣",
            "بلاي ستيشن٣"))
        {
            return PlatformDetectionResult.Create("PlayStation 3", string.Empty, 68, PathHintReasonKey);
        }

        if (Has("playstation vita", "ps vita", "psvita"))
        {
            return PlatformDetectionResult.Create("PlayStation Vita", string.Empty, 68, PathHintReasonKey);
        }

        if (Has("sony psp minis", "psp minis"))
        {
            return PlatformDetectionResult.Create("Sony PSP Minis", string.Empty, 68, PathHintReasonKey);
        }

        if (Has("playstation portable", "psp"))
        {
            return PlatformDetectionResult.Create("PlayStation Portable", string.Empty, 68, PathHintReasonKey);
        }

        if (Has(
            "playstation 2",
            "ps2",
            "sony 2",
            "sony two",
            "سوني 2",
            "سوني ٢",
            "سوني٢",
            "بلايستيشن 2",
            "بلايستيشن ٢",
            "بلايستيشن٢",
            "بلاي ستيشن 2",
            "بلاي ستيشن ٢",
            "بلاي ستيشن٢",
            "بلايستيشن تو",
            "بلاي ستيشن تو"))
        {
            return PlatformDetectionResult.Create("PlayStation 2", string.Empty, 68, PathHintReasonKey);
        }

        if (Has(
            "playstation 1",
            "ps1",
            "psx",
            "ps one",
            "sony 1",
            "sony one",
            "سوني 1",
            "سوني ١",
            "سوني١",
            "بلايستيشن 1",
            "بلايستيشن ١",
            "بلايستيشن١",
            "بلاي ستيشن 1",
            "بلاي ستيشن ١",
            "بلاي ستيشن١",
            "بلايستيشن ون",
            "بلاي ستيشن ون"))
        {
            return PlatformDetectionResult.Create("PlayStation 1", string.Empty, 68, PathHintReasonKey);
        }

        if (Has("advanced pico beena", "pico beena"))
        {
            return PlatformDetectionResult.Create("SEGA Advanced Pico Beena", string.Empty, 68, PathHintReasonKey);
        }

        if (Has("naomi"))
        {
            return PlatformDetectionResult.Create("SEGA Naomi", string.Empty, 68, PathHintReasonKey);
        }

        if (Has("dreamcast"))
        {
            return PlatformDetectionResult.Create("SEGA Dreamcast", string.Empty, 70, PathHintReasonKey);
        }

        if (Has("saturn", "sega saturn"))
        {
            return PlatformDetectionResult.Create("SEGA Saturn", string.Empty, 70, PathHintReasonKey);
        }

        if (Has("sega 32x", "32x"))
        {
            return PlatformDetectionResult.Create("SEGA 32X", string.Empty, 65, PathHintReasonKey);
        }

        if (Has("sega pico", "pico"))
        {
            return PlatformDetectionResult.Create("SEGA Pico", string.Empty, 65, PathHintReasonKey);
        }

        if (Has("sega cd", "segacd", "mega cd", "megacd"))
        {
            return PlatformDetectionResult.Create("SEGA CD", string.Empty, 70, PathHintReasonKey);
        }

        if (Has("game gear", "gamegear"))
        {
            return PlatformDetectionResult.Create("SEGA Game Gear", string.Empty, 65, PathHintReasonKey);
        }

        if (Has("genesis", "mega drive", "megadrive"))
        {
            return PlatformDetectionResult.Create("SEGA Genesis / Mega Drive", string.Empty, 65, PathHintReasonKey);
        }

        if (Has("master system", "sega master system"))
        {
            return PlatformDetectionResult.Create("SEGA Master System", string.Empty, 65, PathHintReasonKey);
        }

        if (Has("xbox 360", "xbox360"))
        {
            return PlatformDetectionResult.Create("Xbox 360", string.Empty, 68, PathHintReasonKey);
        }

        if (Has("xbox"))
        {
            return PlatformDetectionResult.Create("Xbox", string.Empty, 68, PathHintReasonKey);
        }

        if (Has("wii u", "wiiu"))
        {
            return PlatformDetectionResult.Create("Nintendo Wii U", string.Empty, 68, PathHintReasonKey);
        }

        if (Has("wii"))
        {
            return PlatformDetectionResult.Create("Nintendo Wii", string.Empty, 68, PathHintReasonKey);
        }

        if (Has("gamecube", "game cube", "ngc"))
        {
            return PlatformDetectionResult.Create("Nintendo GameCube", string.Empty, 68, PathHintReasonKey);
        }

        if (Has("nintendo 64dd", "64dd", "n64dd"))
        {
            return PlatformDetectionResult.Create("Nintendo 64DD", string.Empty, 65, PathHintReasonKey);
        }

        if (Has("nintendo 64", "n64"))
        {
            return PlatformDetectionResult.Create("Nintendo 64", string.Empty, 65, PathHintReasonKey);
        }

        if (Has("nintendo entertainment system", "nes"))
        {
            return PlatformDetectionResult.Create("Nintendo Entertainment System", string.Empty, 65, PathHintReasonKey);
        }

        if (Has("super nintendo entertainment system", "super nintendo", "snes"))
        {
            return PlatformDetectionResult.Create("Super Nintendo Entertainment System", string.Empty, 65, PathHintReasonKey);
        }

        if (Has("super famicom", "sfc"))
        {
            return PlatformDetectionResult.Create("Super Famicom", string.Empty, 65, PathHintReasonKey);
        }

        if (Has("satellaview", "bsx", "bs x"))
        {
            return PlatformDetectionResult.Create("SATELLAVIEW", string.Empty, 62, PathHintReasonKey);
        }

        if (Has("neo geo cd", "neogeocd"))
        {
            return PlatformDetectionResult.Create("Neo Geo CD", string.Empty, 68, PathHintReasonKey);
        }

        if (Has("3do"))
        {
            return PlatformDetectionResult.Create("3DO", string.Empty, 62, PathHintReasonKey);
        }

        if (Has("pc engine", "pcenginecd", "pc engine cd", "turbografx", "turbografx cd", "turbo grafx cd"))
        {
            return PlatformDetectionResult.Create("PC Engine", string.Empty, 62, PathHintReasonKey);
        }

        return PlatformDetectionResult.Create(fallback, string.Empty, 25, WeakGuessReasonKey);
    }

    private static string NormalizePathHint(string path)
    {
        return (path ?? string.Empty).ToLowerInvariant()
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Replace('.', ' ')
            .Replace('ـ', ' ')
            .Replace('\\', ' ')
            .Replace('/', ' ');
    }

    private static bool ContainsPathHint(string source, params string[] values)
    {
        foreach (string value in values)
        {
            string normalizedValue = NormalizePathHint(value).Trim();
            if (normalizedValue.Length == 0)
            {
                continue;
            }

            if (ContainsWithTokenBoundaries(source, normalizedValue))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsWithTokenBoundaries(string source, string value)
    {
        int searchIndex = 0;

        while (searchIndex < source.Length)
        {
            int index = source.IndexOf(value, searchIndex, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            int before = index - 1;
            int after = index + value.Length;

            bool boundaryBefore = before < 0 || char.IsWhiteSpace(source[before]);
            bool boundaryAfter = after >= source.Length || char.IsWhiteSpace(source[after]);

            if (boundaryBefore && boundaryAfter)
            {
                return true;
            }

            searchIndex = index + value.Length;
        }

        return false;
    }

    private static string? ParseCueFileReference(string line) =>
        CueSheetFileStatementReader.TryRead(line, out string fileName, out _)
            ? fileName
            : null;

    private static IEnumerable<string> ReadTextLines(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            FileOptions.SequentialScan);

        using StreamReader reader = new(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 16 * 1024,
            leaveOpen: false);

        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static string ReadTextFileFromIso(CDReader cd, string path)
    {
        using Stream stream = cd.OpenFile(NormalizeIsoPath(path), FileMode.Open);
        int length = (int)Math.Min(MaxIsoTextProbeBytes, Math.Max(0, stream.Length));
        byte[] buffer = new byte[length];
        int read = stream.Read(buffer, 0, buffer.Length);
        return Encoding.ASCII.GetString(buffer, 0, read);
    }

    private static bool IsApproximately(long actual, long expected, long tolerance) =>
        Math.Abs(actual - expected) <= tolerance;

    private static bool IsDiscUtilsInvalidFileSystemException(Exception ex) =>
        string.Equals(ex.GetType().FullName, "DiscUtils.InvalidFileSystemException", StringComparison.Ordinal)
        || string.Equals(ex.GetType().Name, "InvalidFileSystemException", StringComparison.Ordinal);

    private static bool IsDiscUtilsReadException(Exception ex) =>
        ex.GetType().FullName?.Contains("DiscUtils", StringComparison.OrdinalIgnoreCase) == true
        || ex.GetType().Name.Contains("FileSystem", StringComparison.OrdinalIgnoreCase);

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException;

    private static bool IsExpectedReadException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException
        or InvalidDataException
        or PathTooLongException;
}
