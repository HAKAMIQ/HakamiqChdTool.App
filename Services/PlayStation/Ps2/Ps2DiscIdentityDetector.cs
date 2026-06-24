using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using System;
using System.IO;

namespace HakamiqChdTool.App.Services.PlayStation.Ps2;

internal static class Ps2DiscIdentityDetector
{
    private const long CompactDiscUpperBoundBytes = 900L * 1024L * 1024L;

    public static Ps2DiscIdentity Detect(string path, string? detectedPlatform = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Ps2DiscIdentity.Unknown;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return Ps2DiscIdentity.Unknown;
        }

        if (!File.Exists(fullPath))
        {
            return Ps2DiscIdentity.Unknown;
        }

        Ps2DiscMediaKind mediaKind = DetectMediaKind(fullPath);

        if (DiscRawSerialProbe.TryProbe(fullPath, out DiscRawSerialProbeResult serial)
            && IsPlayStation2Platform(serial.Platform))
        {
            return new Ps2DiscIdentity(
                IsPlayStation2: true,
                Confidence: 96,
                Serial: serial.Serial,
                Region: serial.Region,
                MediaKind: mediaKind,
                IsPathHintOnly: false);
        }

        PlatformDetectionResult platformDetection = PlatformDetectionResult.Create(
            detectedPlatform ?? string.Empty,
            string.Empty,
            ContainsPlayStation2Platform(detectedPlatform) ? 68 : 0,
            string.Empty);

        if (!ContainsPlayStation2Platform(detectedPlatform))
        {
            try
            {
                platformDetection = PlatformDetectionService.Detect(fullPath);
            }
            catch (Exception ex) when (IsExpectedPathException(ex) || IsExpectedReadException(ex) || ex is InvalidDataException or OverflowException)
            {
                platformDetection = PlatformDetectionResult.Create(string.Empty, string.Empty, 0, string.Empty);
            }
        }

        if (!IsPlayStation2Platform(platformDetection.PlatformName))
        {
            return Ps2DiscIdentity.Unknown;
        }

        bool pathHintOnly = IsPathHintReason(platformDetection.Reason)
            || platformDetection.ConfidenceScore <= 70;

        return new Ps2DiscIdentity(
            IsPlayStation2: true,
            Confidence: Math.Clamp(platformDetection.ConfidenceScore, 60, 96),
            Serial: string.Empty,
            Region: string.Empty,
            MediaKind: mediaKind,
            IsPathHintOnly: pathHintOnly);
    }

    private static Ps2DiscMediaKind DetectMediaKind(string fullPath)
    {
        string extension = Path.GetExtension(fullPath).ToLowerInvariant();
        return extension switch
        {
            ".cue" => Ps2DiscMediaKind.CueBinCd,
            ".bin" => Ps2DiscMediaKind.StandaloneBinCd,
            ".chd" => Ps2DiscMediaKind.ChdUnknown,
            ".iso" => DetectIsoMediaKind(fullPath),
            _ => Ps2DiscMediaKind.Unknown
        };
    }

    private static Ps2DiscMediaKind DetectIsoMediaKind(string fullPath)
    {
        try
        {
            long size = new FileInfo(fullPath).Length;
            return size > 0 && size <= CompactDiscUpperBoundBytes
                ? Ps2DiscMediaKind.CompactIsoPossiblyCd
                : Ps2DiscMediaKind.DvdIso;
        }
        catch (Exception ex) when (IsExpectedReadException(ex) || IsExpectedPathException(ex))
        {
            return Ps2DiscMediaKind.Unknown;
        }
    }

    private static bool ContainsPlayStation2Platform(string? platformName) =>
        !string.IsNullOrWhiteSpace(platformName)
        && IsPlayStation2Platform(platformName);

    private static bool IsPlayStation2Platform(string? platformName)
    {
        if (string.IsNullOrWhiteSpace(platformName))
        {
            return false;
        }

        string platform = platformName.Trim();
        return platform.Contains("PlayStation 2", StringComparison.OrdinalIgnoreCase)
            || platform.Contains("Sony PlayStation 2", StringComparison.OrdinalIgnoreCase)
            || platform.Equals("PS2", StringComparison.OrdinalIgnoreCase)
            || platform.Contains(" PS2", StringComparison.OrdinalIgnoreCase)
            || platform.Contains("PS2 ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathHintReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        return reason.Contains("PathHint", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("filename", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("path", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or IOException
        or NotSupportedException
        or PathTooLongException
        or UnauthorizedAccessException
        or System.Security.SecurityException;

    private static bool IsExpectedReadException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or InvalidDataException;
}
