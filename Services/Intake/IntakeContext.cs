using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace HakamiqChdTool.App.Services.Intake;

public sealed record IntakeContext
{
    public required string InputPath { get; init; }

    public string? FileName { get; init; }

    public string? Extension { get; init; }

    public bool Exists { get; init; }

    public bool IsDirectory { get; init; }

    public bool IsArchive { get; init; }

    public bool IsChd { get; init; }

    public bool IsDiscImage { get; init; }

    public bool IsCueLikeDescriptor { get; init; }

    public bool IsUnsupportedDiscImage { get; init; }

    public string? DetectedPlatform { get; init; }

    public int? PlatformConfidence { get; init; }

    public string? DetectedRegion { get; init; }

    public string? RawSerial { get; init; }

    public string? DiscTitle { get; init; }

    public string? SuggestedCleanName { get; init; }

    public int? NamingConfidence { get; init; }

    public IReadOnlyList<string> ArchiveCandidatePaths { get; init; } = [];

    public IReadOnlyList<IntakeDecisionReason> ProbeReasons { get; init; } = [];

    public IReadOnlyList<IntakeDecisionReason> ProbeWarnings { get; init; } = [];

    public static IntakeContext Missing(string inputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        return new IntakeContext
        {
            InputPath = inputPath,
            FileName = Path.GetFileName(inputPath),
            Extension = Path.GetExtension(inputPath),
            Exists = false
        };
    }

    public static IntakeContext FromExistingPath(string inputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        bool isDirectory = Directory.Exists(inputPath);
        bool isFile = File.Exists(inputPath);

        if (!isDirectory && !isFile)
        {
            return Missing(inputPath);
        }

        string fileName;
        string suggestedCleanName;
        string extension;

        if (isDirectory)
        {
            DirectoryInfo directory = new(inputPath);
            fileName = directory.Name;
            suggestedCleanName = directory.Name;
            extension = string.Empty;
        }
        else
        {
            fileName = Path.GetFileName(inputPath);
            suggestedCleanName = Path.GetFileNameWithoutExtension(inputPath);
            extension = Path.GetExtension(inputPath);
        }

        string normalizedExtension = extension.TrimStart('.').ToLowerInvariant();

        return new IntakeContext
        {
            InputPath = inputPath,
            FileName = fileName,
            Extension = extension,
            Exists = true,
            IsDirectory = isDirectory,
            IsArchive = IsArchiveExtension(normalizedExtension),
            IsChd = string.Equals(normalizedExtension, "chd", StringComparison.OrdinalIgnoreCase),
            IsDiscImage = IsDiscImageExtension(normalizedExtension),
            IsCueLikeDescriptor = IsCueLikeDescriptorExtension(normalizedExtension),
            IsUnsupportedDiscImage = IsUnsupportedDiscImageExtension(normalizedExtension),
            SuggestedCleanName = suggestedCleanName
        };
    }

    public IntakeContext WithProbeReason(IntakeDecisionReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);

        List<IntakeDecisionReason> reasons = new(ProbeReasons)
        {
            reason
        };

        return this with
        {
            ProbeReasons = new ReadOnlyCollection<IntakeDecisionReason>(reasons)
        };
    }

    public IntakeContext WithProbeWarning(IntakeDecisionReason warning)
    {
        ArgumentNullException.ThrowIfNull(warning);

        List<IntakeDecisionReason> warnings = new(ProbeWarnings)
        {
            warning
        };

        return this with
        {
            ProbeWarnings = new ReadOnlyCollection<IntakeDecisionReason>(warnings)
        };
    }

    private static bool IsArchiveExtension(string extension)
    {
        return extension is "zip" or "rar" or "7z";
    }

    private static bool IsDiscImageExtension(string extension)
    {
        return extension is "iso" or "cue" or "gdi" or "toc" or "nrg" or "bin";
    }

    private static bool IsCueLikeDescriptorExtension(string extension)
    {
        return extension is "cue" or "gdi" or "toc";
    }

    private static bool IsUnsupportedDiscImageExtension(string extension)
    {
        return extension is "cdi";
    }
}