using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Models.Chd;
using System;

namespace HakamiqChdTool.App.Services;

internal static class DiscMediaKindResolver
{
    internal const long SafeCdIsoUpperBoundBytes = 800L * 1024L * 1024L;
    internal const long SafeDvdIsoUpperBoundBytes = 9L * 1024L * 1024L * 1024L;
    private const string DiscProbePspStructureReasonKey = "LocDiscProbe_PspStructure";
    private const string DiscProbePs1SystemCnfReasonKey = "LocDiscProbe_SystemCnfPs1Hint";
    private const string DiscProbePs3BluRayStructureReasonKey = "LocDiscProbe_Ps3BluRayStructure";

    public static DiscMediaKind ResolveIsoMediaKind(
        PlatformDetectionResult detection,
        long fileLengthBytes)
        => (DiscMediaKind)ResolveIsoMediaDecision(detection, fileLengthBytes).Kind;

    public static DiscMediaDecision ResolveIsoMediaDecision(PlatformDetectionResult detection, long fileLengthBytes)
    {
        if (fileLengthBytes <= 0)
        {
            return default;
        }

        string? reason = detection.Reason;
        if (string.Equals(reason, DiscProbePs1SystemCnfReasonKey, StringComparison.Ordinal))
        {
            return new(DiscMediaClassification.CdRom, DiscMediaDecisionSource.Structure, DiscMediaStructure.Ps1);
        }

        if (string.Equals(reason, DiscProbePspStructureReasonKey, StringComparison.Ordinal))
        {
            return new(DiscMediaClassification.DvdRom, DiscMediaDecisionSource.Structure, DiscMediaStructure.Psp);
        }

        if (string.Equals(reason, DiscProbePs3BluRayStructureReasonKey, StringComparison.Ordinal))
        {
            return new(DiscMediaClassification.DvdRom, DiscMediaDecisionSource.Structure, DiscMediaStructure.Ps3);
        }

        // PS2 SYSTEM.CNF and unrecognized reasons both fall back to size.
        DiscMediaKind kind = ResolveFromIsoLength(fileLengthBytes);
        return kind == DiscMediaKind.Unknown
            ? default
            : new((DiscMediaClassification)kind, DiscMediaDecisionSource.SizeFallback, DiscMediaStructure.None);
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