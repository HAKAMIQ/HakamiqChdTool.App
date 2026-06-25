using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static HakamiqChdTool.App.Services.ChdConversionMessages;

namespace HakamiqChdTool.App.Services;

public sealed class ChdCommandPreparationService : IChdCommandPreparationService
{
    // Compression truth markers: MameCreateCdDefaultCompression EffectiveCompression SameAsMameDefault
    public string BuildExtractOutputPathReplacingChdExtension(string chdPath, string newExtensionWithDot)
    {
        if (string.IsNullOrWhiteSpace(chdPath))
        {
            throw new ArgumentException(InvalidChdPathMessageKey, nameof(chdPath));
        }

        string withoutDot = newExtensionWithDot.StartsWith('.')
            ? newExtensionWithDot[1..]
            : newExtensionWithDot;

        return Path.ChangeExtension(chdPath, withoutDot);
    }

    public string BuildExtractCdBinOutputPath(string cueOutputPath)
    {
        if (string.IsNullOrWhiteSpace(cueOutputPath))
        {
            throw new ArgumentException(InvalidCueOutputPathMessageKey, nameof(cueOutputPath));
        }

        string? directory = Path.GetDirectoryName(cueOutputPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(BinOutputDirectoryMissingMessageKey);
        }

        string stem = Path.GetFileNameWithoutExtension(cueOutputPath);
        stem = string.IsNullOrWhiteSpace(stem) ? "track" : stem;
        return Path.Combine(directory, $"{stem}.bin");
    }


    public string BuildSplitBinExtractCdBinOutputPath(string cueOutputPath)
    {
        if (string.IsNullOrWhiteSpace(cueOutputPath))
        {
            throw new ArgumentException(InvalidCueOutputPathMessageKey, nameof(cueOutputPath));
        }

        string? directory = Path.GetDirectoryName(cueOutputPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(BinOutputDirectoryMissingMessageKey);
        }

        string stem = Path.GetFileNameWithoutExtension(cueOutputPath);
        stem = string.IsNullOrWhiteSpace(stem) ? "track" : stem;
        return Path.Combine(directory, $"{stem} (Track %t).bin");
    }

    public string BuildCommand(
        string inputPath,
        ChdmanExtractionKind extractionKind = ChdmanExtractionKind.None,
        IsoCreateCommandOverride isoCreateCommandOverride = IsoCreateCommandOverride.Auto)
    {
        string ext = Path.GetExtension(inputPath).ToLowerInvariant();
        return ResolveTwoWayCommandWithOptionalIsoDiagnostics(ext, extractionKind, inputPath, isoCreateCommandOverride).Command;
    }

    public (string Command, IsoChdmanCreateDiagnostics? IsoDiagnostics) ResolveTwoWayCommandWithOptionalIsoDiagnostics(
        string inputExtension,
        ChdmanExtractionKind extractionKind,
        string? fullInputPathForIsoClassification,
        IsoCreateCommandOverride isoCreateCommandOverride)
    {
        bool isChdInput = string.Equals(inputExtension, ".chd", StringComparison.OrdinalIgnoreCase);

        if (isChdInput)
        {
            ChdWorkflowProfilePlan extractionPlan = ChdWorkflowProfilePlanner.PlanExtractionByKind(extractionKind);
            if (!extractionPlan.IsSupported || string.IsNullOrWhiteSpace(extractionPlan.Command))
            {
                throw new InvalidOperationException(extractionPlan.FailureMessage);
            }

            return (extractionPlan.Command, null);
        }

        if (extractionKind != ChdmanExtractionKind.None)
        {
            throw new InvalidOperationException(ExtractionKindRequiresChdInputMessageKey);
        }

        ChdWorkflowProfilePlan createPlan = ChdWorkflowProfilePlanner.PlanCreateFromSource(
            fullInputPathForIsoClassification ?? string.Empty,
            isoCreateCommandOverride,
            ChdMediaContainerKind.DirectFile);

        if (!createPlan.IsSupported || string.IsNullOrWhiteSpace(createPlan.Command))
        {
            throw new NotSupportedException(createPlan.FailureMessage);
        }

        return (createPlan.Command, createPlan.IsoDiagnostics);
    }

    public bool IsCreateCommand(string command) =>
        string.Equals(command, "createcd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "createdvd", StringComparison.OrdinalIgnoreCase);

    public bool IsExtractCommand(string command) =>
        string.Equals(command, "extractcd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "extractdvd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "extracthd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "extractraw", StringComparison.OrdinalIgnoreCase);

    public bool IsExtractCdSplitbinPatternRequired(ChdmanCliRunner.Result run)
    {
        string text = string.Concat(run.StandardError, Environment.NewLine, run.StandardOutput);
        return text.Contains("track number variable (%t) must be specified", StringComparison.OrdinalIgnoreCase)
            || text.Contains("--splitbin", StringComparison.OrdinalIgnoreCase)
                && text.Contains("%t", StringComparison.OrdinalIgnoreCase);
    }

    public void ReplaceExtractCdBinOutputArgument(List<string> arguments, string binOutputPath)
    {
        int optionIndex = arguments.FindIndex(static arg => string.Equals(arg, "-ob", StringComparison.OrdinalIgnoreCase));
        if (optionIndex >= 0 && optionIndex + 1 < arguments.Count)
        {
            arguments[optionIndex + 1] = binOutputPath;
            return;
        }

        arguments.Add("-ob");
        arguments.Add(binOutputPath);
    }


    public string ResolveCompressionSetting(string? compressionCodecs, string command) =>
        ResolveCompressionSettingWithTruth(compressionCodecs, command).ResolvedCompression;

    public ChdCompressionResolution ResolveCompressionSettingWithTruth(string? compressionCodecs, string command)
    {
        if (string.Equals(command, "createcd", StringComparison.OrdinalIgnoreCase))
        {
            return new ChdCdCompressionPolicy().ResolveWithTruth(compressionCodecs);
        }

        if (string.Equals(command, "createdvd", StringComparison.OrdinalIgnoreCase))
        {
            return new ChdDvdCompressionPolicy().ResolveWithTruth(compressionCodecs);
        }

        return ChdCompressionResolution.NotApplicable;
    }

    public int ResolveHunkSizeSetting(int hunkSizeBytes, string command, string inputPath, out string policyNote)
    {
        policyNote = string.Empty;
        bool isCd = string.Equals(command, "createcd", StringComparison.OrdinalIgnoreCase);

        if (!isCd)
        {
            const int dvdSectorUnitBytes = 2048;
            int resolved = hunkSizeBytes switch
            {
                0 => 0,
                -1 => 16384,
                -2 => 32768,
                -3 => 65536,
                > 0 => hunkSizeBytes,
                _ => 0
            };

            if (resolved <= 0)
            {
                policyNote = hunkSizeBytes == 0
                    ? "createdvd hunk size left to MAME default."
                    : "createdvd hunk preset omitted because the requested value was not recognized.";
                return 0;
            }

            if (resolved % dvdSectorUnitBytes != 0)
            {
                throw new InvalidOperationException(InvalidDvdHunkSizeMessageKey);
            }

            policyNote = hunkSizeBytes > 0
                ? "createdvd explicit hunk size passed after sector-unit validation."
                : "createdvd hunk preset resolved to a 2048-byte aligned value.";
            return resolved;
        }

        if (hunkSizeBytes == 0)
        {
            policyNote = "createcd hunk size left to chdman default.";
            return 0;
        }

        if (TryResolveCreateCdPresetSectorCount(hunkSizeBytes, out int sectorsPerHunk))
        {
            if (TryResolveCdSectorUnitSize(inputPath, out int sectorSize)
                && TryBuildValidHunkSize(sectorSize, sectorsPerHunk, out int resolved))
            {
                policyNote = $"createcd hunk preset resolved from detected sector size {sectorSize} bytes.";
                return resolved;
            }

            policyNote = "createcd hunk preset omitted because the CD sector size could not be proven before execution; chdman will choose a safe default.";
            return 0;
        }

        if (hunkSizeBytes > 0)
        {
            if (!TryResolveCdSectorUnitSize(inputPath, out int sectorSize))
            {
                policyNote = "createcd explicit hunk size omitted because the CD sector size could not be proven before execution; chdman will choose a safe default.";
                return 0;
            }

            if (hunkSizeBytes % sectorSize != 0)
            {
                throw new InvalidOperationException(InvalidCdHunkSizeMessageKey);
            }

            policyNote = "createcd explicit hunk size passed after sector-size validation.";
            return hunkSizeBytes;
        }

        return 0;
    }

    private static bool TryResolveCreateCdPresetSectorCount(int hunkSizeBytes, out int sectorsPerHunk)
    {
        sectorsPerHunk = hunkSizeBytes switch
        {
            -1 => 8,
            -2 => 16,
            -3 => 32,
            _ => 0
        };

        return sectorsPerHunk > 0;
    }

    public bool TryBuildCreateCdHunkRetrySize(
        int requestedHunkSetting,
        int requiredSectorSize,
        int previousHunkSizeBytes,
        out int retryHunkSizeBytes)
    {
        retryHunkSizeBytes = 0;
        if (!TryResolveCreateCdPresetSectorCount(requestedHunkSetting, out int sectorsPerHunk))
        {
            return false;
        }

        if (!TryBuildValidHunkSize(requiredSectorSize, sectorsPerHunk, out int candidate))
        {
            return false;
        }

        if (candidate == previousHunkSizeBytes)
        {
            return false;
        }

        retryHunkSizeBytes = candidate;
        return true;
    }

    private static bool TryBuildValidHunkSize(int sectorSize, int sectorsPerHunk, out int hunkSizeBytes)
    {
        hunkSizeBytes = 0;
        if (sectorSize <= 0 || sectorsPerHunk <= 0)
        {
            return false;
        }

        long candidate = (long)sectorSize * sectorsPerHunk;
        if (candidate < 16 || candidate > 1_048_576)
        {
            return false;
        }

        hunkSizeBytes = (int)candidate;
        return hunkSizeBytes % sectorSize == 0;
    }

    private static bool TryResolveCdSectorUnitSize(string inputPath, out int sectorSize)
    {
        sectorSize = 0;
        string extension = Path.GetExtension(inputPath).ToLowerInvariant();

        try
        {
            HashSet<int> sizes = extension switch
            {
                ".gdi" => ParseGdiSectorSizes(inputPath),
                ".cue" => ParseCueSectorSizes(inputPath),
                _ => new HashSet<int>()
            };

            if (sizes.Count == 0)
            {
                return false;
            }

            long lcm = 1;
            foreach (int size in sizes)
            {
                lcm = LeastCommonMultiple(lcm, size);
                if (lcm <= 0 || lcm > 1_048_576)
                {
                    return false;
                }
            }

            sectorSize = (int)lcm;
            return sectorSize > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Debug(ex, "Could not resolve CD sector size before createcd. Input={InputPath}", inputPath);
            return false;
        }
    }

    private static HashSet<int> ParseGdiSectorSizes(string gdiPath)
    {
        var sizes = new HashSet<int>();
        foreach (string rawLine in File.ReadLines(gdiPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || !char.IsDigit(line[0]))
            {
                continue;
            }

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4 && int.TryParse(parts[3], out int sectorSize) && IsPlausibleCdSectorSize(sectorSize))
            {
                sizes.Add(sectorSize);
            }
        }

        return sizes;
    }

    private static HashSet<int> ParseCueSectorSizes(string cuePath)
    {
        var sizes = new HashSet<int>();
        foreach (string rawLine in File.ReadLines(cuePath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            foreach (Match match in Regex.Matches(line, @"/(\d{3,5})"))
            {
                if (int.TryParse(match.Groups[1].Value, out int sectorSize) && IsPlausibleCdSectorSize(sectorSize))
                {
                    sizes.Add(sectorSize);
                }
            }

            if (line.StartsWith("TRACK ", StringComparison.OrdinalIgnoreCase)
                && line.IndexOf("AUDIO", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                sizes.Add(2352);
            }
        }

        return sizes;
    }

    private static bool IsPlausibleCdSectorSize(int sectorSize) => sectorSize is >= 2048 and <= 2448;

    private static long GreatestCommonDivisor(long left, long right)
    {
        left = Math.Abs(left);
        right = Math.Abs(right);
        while (right != 0)
        {
            long temp = left % right;
            left = right;
            right = temp;
        }

        return left == 0 ? 1 : left;
    }

    private static long LeastCommonMultiple(long left, long right)
    {
        if (left <= 0 || right <= 0)
        {
            return 0;
        }

        return checked((left / GreatestCommonDivisor(left, right)) * right);
    }


    public void ReplaceOrAddHunkSizeArgument(List<string> arguments, int hunkSizeBytes)
    {
        for (int i = 0; i < arguments.Count; i++)
        {
            if (!string.Equals(arguments[i], "-hs", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(arguments[i], "--hunksize", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 < arguments.Count)
            {
                arguments[i + 1] = hunkSizeBytes.ToString();
                return;
            }
        }

        arguments.Add("-hs");
        arguments.Add(hunkSizeBytes.ToString());
    }

    public string BuildLogsDirectory()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HakamiqChdTool",
            "Logs");

        Directory.CreateDirectory(path);
        return path;
    }

    public string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(value) ? "file" : value;
    }

    public string NormalizePathForCli(string path) => FilePathExclusiveGate.NormalizePathForExclusiveLock(path);

}
