using HakamiqChdTool.App.Models;
using System;

namespace HakamiqChdTool.App.Services;

internal static class DiscMediaKindResolver
{
    internal const long SafeCdIsoUpperBoundBytes = 800L * 1024L * 1024L;
    internal const long SafeDvdIsoUpperBoundBytes = 9L * 1024L * 1024L * 1024L;
    private const string DiscProbePspStructureReasonKey = "LocDiscProbe_PspStructure";
    private const string DiscProbePs2SystemCnfReasonKey = "LocDiscProbe_SystemCnfPs2Boot2";
    private const string DiscProbePs1SystemCnfReasonKey = "LocDiscProbe_SystemCnfPs1Hint";
    private const string DiscProbePs3BluRayStructureReasonKey = "LocDiscProbe_Ps3BluRayStructure";

    public static DiscMediaKind ResolveIsoMediaKind(
        PlatformDetectionResult detection,
        long fileLengthBytes)
    {
        if (fileLengthBytes <= 0)
        {
            return DiscMediaKind.Unknown;
        }

        DiscMediaKind structureKind = ResolveFromDiscProbeReason(detection.Reason, fileLengthBytes);
        if (structureKind != DiscMediaKind.Unknown)
        {
            return structureKind;
        }

        return ResolveFromIsoLength(fileLengthBytes);
    }

    private static DiscMediaKind ResolveFromDiscProbeReason(string? reason, long fileLengthBytes)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return DiscMediaKind.Unknown;
        }

        if (string.Equals(reason, DiscProbePs1SystemCnfReasonKey, StringComparison.Ordinal))
        {
            return DiscMediaKind.CdRom;
        }

        if (string.Equals(reason, DiscProbePspStructureReasonKey, StringComparison.Ordinal)
            || string.Equals(reason, DiscProbePs3BluRayStructureReasonKey, StringComparison.Ordinal))
        {
            return DiscMediaKind.DvdRom;
        }

        if (string.Equals(reason, DiscProbePs2SystemCnfReasonKey, StringComparison.Ordinal))
        {
            return ResolveFromIsoLength(fileLengthBytes);
        }

        return DiscMediaKind.Unknown;
    }

    private static DiscMediaKind ResolveFromIsoLength(long fileLengthBytes)
    {
        if (fileLengthBytes <= 0)
        {
            return DiscMediaKind.Unknown;
        }

        if (fileLengthBytes <= SafeCdIsoUpperBoundBytes)
        {
            return DiscMediaKind.CdRom;
        }

        if (fileLengthBytes <= SafeDvdIsoUpperBoundBytes)
        {
            return DiscMediaKind.DvdRom;
        }

        return DiscMediaKind.Unknown;
    }
}