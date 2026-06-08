using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.IO;

namespace HakamiqChdTool.App.Services;

public readonly record struct IsoChdmanCreateDiagnostics(
    string PlatformName,
    string DetectionReason,
    int ConfidenceScore,
    long FileLengthBytes,
    string AutoSuggestedCommand,
    IsoCreateCommandOverride OverrideMode,
    string Command);

public static class IsoChdmanCreateCommandResolver
{
    internal const long SafeCdIsoUpperBoundBytes = DiscMediaKindResolver.SafeCdIsoUpperBoundBytes;

    private const string CreateCdCommand = "createcd";
    private const string CreateDvdCommand = "createdvd";
    private const string UnknownPlatformName = "Unknown";

    private const string DiscProbeInsufficientMetadataReasonKey = "LocDiscProbe_InsufficientMetadata";
    private const string DiscProbePspStructureReasonKey = "LocDiscProbe_PspStructure";
    private const string DiscProbePs2SystemCnfReasonKey = "LocDiscProbe_SystemCnfPs2Boot2";
    private const string DiscProbePs1SystemCnfReasonKey = "LocDiscProbe_SystemCnfPs1Hint";
    private const string DiscProbePs3BluRayStructureReasonKey = "LocDiscProbe_Ps3BluRayStructure";

    private static readonly ILogger Logger = global::Serilog.Log.ForContext(typeof(IsoChdmanCreateCommandResolver));

    public static string ResolveCreateCompressionCommand(
        string isoPath,
        IsoCreateCommandOverride overrideMode = IsoCreateCommandOverride.Auto) =>
        ResolveCreateCompressionCommandWithDiagnostics(isoPath, overrideMode).Command;

    public static IsoChdmanCreateDiagnostics ResolveCreateCompressionCommandWithDiagnostics(
        string isoPath,
        IsoCreateCommandOverride overrideMode = IsoCreateCommandOverride.Auto)
    {
        if (string.IsNullOrWhiteSpace(isoPath))
        {
            return Fallback(overrideMode);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(isoPath);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            Logger.Debug(ex, "ISO create command resolution rejected an invalid path. Path={Path}", isoPath);
            return Fallback(overrideMode);
        }

        if (!File.Exists(fullPath))
        {
            return Fallback(overrideMode);
        }

        long length;
        try
        {
            length = new FileInfo(fullPath).Length;
        }
        catch (Exception ex) when (IsExpectedReadException(ex) || IsExpectedPathException(ex))
        {
            Logger.Debug(ex, "ISO create command resolution could not read file size. Path={Path}", fullPath);
            return Fallback(overrideMode);
        }

        PlatformDetectionResult detection = DetectPlatformSafely(fullPath);
        DiscMediaKind mediaKind = ResolveIsoMediaKindForChdmanCommand(detection, length);
        string autoSuggested = ToChdmanCreateCommand(mediaKind);

        if (mediaKind == DiscMediaKind.Unknown)
        {
            Logger.Information(
                "ISO create command resolution used DVD-safe default because media kind was unknown. Path={Path}, SizeBytes={SizeBytes}, Platform={Platform}, Confidence={Confidence}, Reason={Reason}, Override={Override}",
                fullPath,
                length,
                detection.PlatformName,
                detection.ConfidenceScore,
                detection.Reason,
                overrideMode);
        }
        else
        {
            Logger.Debug(
                "ISO create command resolved from media evidence. Path={Path}, SizeBytes={SizeBytes}, MediaKind={MediaKind}, DiagnosticPlatform={Platform}, Confidence={Confidence}, Reason={Reason}, Override={Override}",
                fullPath,
                length,
                mediaKind,
                detection.PlatformName,
                detection.ConfidenceScore,
                detection.Reason,
                overrideMode);
        }

        return new IsoChdmanCreateDiagnostics(
            detection.PlatformName ?? string.Empty,
            detection.Reason ?? string.Empty,
            detection.ConfidenceScore,
            length,
            autoSuggested,
            overrideMode,
            ApplyOverride(autoSuggested, overrideMode));
    }

    internal static string ApplyOverride(string autoSuggestedCommand, IsoCreateCommandOverride overrideMode) =>
        overrideMode switch
        {
            IsoCreateCommandOverride.CreateCd => CreateCdCommand,
            IsoCreateCommandOverride.CreateDvd => CreateDvdCommand,
            _ => string.IsNullOrWhiteSpace(autoSuggestedCommand) ? CreateDvdCommand : autoSuggestedCommand
        };

    public static string ResolveFromDetection(string? platformName, long fileLengthBytes)
    {
        if (!string.IsNullOrWhiteSpace(platformName))
        {
            Logger.Debug(
                "ISO create command ignored platform name in legacy ResolveFromDetection call. Platform={Platform}, SizeBytes={SizeBytes}",
                platformName,
                fileLengthBytes);
        }

        return ToChdmanCreateCommand(ResolveIsoMediaKindFromLength(fileLengthBytes));
    }

    private static DiscMediaKind ResolveIsoMediaKindForChdmanCommand(
        PlatformDetectionResult detection,
        long fileLengthBytes)
    {
        if (fileLengthBytes <= 0)
        {
            return DiscMediaKind.Unknown;
        }

        DiscMediaKind structureKind = ResolveIsoMediaKindFromDiscProbeReason(detection.Reason);
        if (structureKind != DiscMediaKind.Unknown)
        {
            return structureKind;
        }

        return ResolveIsoMediaKindFromLength(fileLengthBytes);
    }

    private static DiscMediaKind ResolveIsoMediaKindFromDiscProbeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return DiscMediaKind.Unknown;
        }

        if (string.Equals(reason, DiscProbePs1SystemCnfReasonKey, StringComparison.Ordinal))
        {
            return DiscMediaKind.CdRom;
        }

        if (string.Equals(reason, DiscProbePs2SystemCnfReasonKey, StringComparison.Ordinal)
            || string.Equals(reason, DiscProbePspStructureReasonKey, StringComparison.Ordinal)
            || string.Equals(reason, DiscProbePs3BluRayStructureReasonKey, StringComparison.Ordinal))
        {
            return DiscMediaKind.DvdRom;
        }

        return DiscMediaKind.Unknown;
    }

    private static DiscMediaKind ResolveIsoMediaKindFromLength(long fileLengthBytes)
    {
        if (fileLengthBytes <= 0)
        {
            return DiscMediaKind.Unknown;
        }

        return fileLengthBytes <= SafeCdIsoUpperBoundBytes
            ? DiscMediaKind.CdRom
            : DiscMediaKind.DvdRom;
    }

    private static string ToChdmanCreateCommand(DiscMediaKind mediaKind) =>
        mediaKind switch
        {
            DiscMediaKind.CdRom => CreateCdCommand,
            DiscMediaKind.DvdRom => CreateDvdCommand,
            _ => CreateDvdCommand
        };

    private static IsoChdmanCreateDiagnostics Fallback(IsoCreateCommandOverride overrideMode)
    {
        const long length = 0L;
        const string autoSuggested = CreateDvdCommand;

        return new IsoChdmanCreateDiagnostics(
            UnknownPlatformName,
            string.Empty,
            0,
            length,
            autoSuggested,
            overrideMode,
            ApplyOverride(autoSuggested, overrideMode));
    }

    private static PlatformDetectionResult DetectPlatformSafely(string fullPath)
    {
        try
        {
            return PlatformDetectionService.Detect(fullPath);
        }
        catch (Exception ex) when (IsExpectedReadException(ex) || IsExpectedPathException(ex))
        {
            Logger.Debug(ex, "ISO create command resolution could not detect diagnostic platform. Path={Path}", fullPath);
            return PlatformDetectionResult.Create(string.Empty, string.Empty, 10, DiscProbeInsufficientMetadataReasonKey);
        }
    }

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException;

    private static bool IsExpectedReadException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or InvalidDataException
        or PathTooLongException;
}