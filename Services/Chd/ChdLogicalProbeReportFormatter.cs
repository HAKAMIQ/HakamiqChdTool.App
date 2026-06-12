using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace HakamiqChdTool.App.Services;

public sealed record ChdLogicalProbeReportMetric(
    string Label,
    string Value);

public sealed record ChdProbeReportView(
    string Title,
    IReadOnlyList<ChdLogicalProbeReportMetric> Metrics)
{
    public bool HasMetrics => Metrics.Count > 0;
}

public static class ChdLogicalProbeReportFormatter
{
    private static readonly char[] LineSeparators = ['\r', '\n'];

    public static ChdProbeReportView? BuildView(ChdLogicalProbeResult result)
    {
        if (!result.HasLogicalGeometry)
        {
            return null;
        }

        return BuildViewCore(
            result.PhysicalBytes,
            result.LogicalBytes,
            result.HunkBytes,
            result.TotalHunks,
            result.DecodedCacheBytes);
    }

    public static ChdProbeReportView? BuildView(ChdInfoResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!result.HasLogicalProbeGeometry || !result.PhysicalBytes.HasValue || !result.LogicalBytes.HasValue || !result.HunkBytes.HasValue || !result.TotalHunks.HasValue)
        {
            return null;
        }

        return BuildViewCore(
            result.PhysicalBytes.Value,
            result.LogicalBytes.Value,
            result.HunkBytes.Value,
            result.TotalHunks.Value,
            result.DecodedCacheBytes.GetValueOrDefault());
    }

    public static string BuildReport(ChdLogicalProbeResult result)
    {
        ChdProbeReportView? presentation = BuildView(result);
        return BuildTextReport(presentation);
    }

    public static string BuildReport(ChdInfoResult result)
    {
        ChdProbeReportView? presentation = BuildView(result);
        return BuildTextReport(presentation);
    }

    public static ChdProbeReportView? TryBuildViewFromInfoLog(string? logPath)
    {
        if (string.IsNullOrWhiteSpace(logPath))
        {
            return null;
        }

        try
        {
            string fullPath = Path.GetFullPath(logPath.Trim());
            if (!File.Exists(fullPath))
            {
                return null;
            }

            string text = File.ReadAllText(fullPath);
            return TryBuildViewFromInfoLogText(text);
        }
        catch (Exception ex) when (IsExpectedIoException(ex))
        {
            return null;
        }
    }

    public static string TryBuildReportFromInfoLog(string? logPath)
    {
        ChdProbeReportView? presentation = TryBuildViewFromInfoLog(logPath);
        return BuildTextReport(presentation);
    }

    public static ChdProbeReportView? TryBuildViewFromInfoLogText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        Dictionary<string, string> values = ParseKeyValueLines(text);

        if (!TryGetInt64(values, "PhysicalBytes", out long physicalBytes)
            || !TryGetInt64(values, "LogicalBytes", out long logicalBytes)
            || !TryGetInt32(values, "HunkBytes", out int hunkBytes)
            || !TryGetInt32(values, "TotalHunks", out int totalHunks))
        {
            return null;
        }

        _ = TryGetInt64(values, "DecodedCacheBytes", out long decodedCacheBytes);

        if (physicalBytes <= 0 || logicalBytes <= 0 || hunkBytes <= 0 || totalHunks <= 0)
        {
            return null;
        }

        return BuildViewCore(
            physicalBytes,
            logicalBytes,
            hunkBytes,
            totalHunks,
            decodedCacheBytes);
    }

    public static string TryBuildReportFromInfoLogText(string? text)
    {
        ChdProbeReportView? presentation = TryBuildViewFromInfoLogText(text);
        return BuildTextReport(presentation);
    }

    private static ChdProbeReportView? BuildViewCore(
        long physicalBytes,
        long logicalBytes,
        int hunkBytes,
        int totalHunks,
        long decodedCacheBytes)
    {
        if (physicalBytes <= 0 || logicalBytes <= 0 || hunkBytes <= 0 || totalHunks <= 0)
        {
            return null;
        }

        double compressionRatio = physicalBytes / (double)logicalBytes;
        double savedRatio = Math.Clamp(1.0 - compressionRatio, 0.0, 1.0);

        var metrics = new List<ChdLogicalProbeReportMetric>
        {
            new(ArabicUi.Get("LocChdLogicalReport_CompressedSizeLabel"), FormatBytes(physicalBytes)),
            new(ArabicUi.Get("LocChdLogicalReport_LogicalSizeLabel"), FormatBytes(logicalBytes)),
            new(ArabicUi.Get("LocChdLogicalReport_StorageSavedLabel"), FormatPercent(savedRatio)),
            new(ArabicUi.Get("LocChdLogicalReport_HunkSizeLabel"), FormatInteger(hunkBytes) + " bytes"),
            new(ArabicUi.Get("LocChdLogicalReport_TotalHunksLabel"), FormatInteger(totalHunks))
        };

        if (decodedCacheBytes > 0)
        {
            metrics.Add(new ChdLogicalProbeReportMetric(
                ArabicUi.Get("LocChdLogicalReport_DecodedCacheLabel"),
                FormatBytes(decodedCacheBytes)));
        }

        return new ChdProbeReportView(
            ArabicUi.Get("LocChdLogicalReport_Title"),
            metrics);
    }

    private static string BuildTextReport(ChdProbeReportView? presentation)
    {
        if (presentation is null || !presentation.HasMetrics)
        {
            return string.Empty;
        }

        IEnumerable<string> lines = presentation.Metrics.Select(metric => string.Concat(metric.Label, ": ", metric.Value));
        return string.Join(Environment.NewLine, new[] { presentation.Title }.Concat(lines));
    }

    private static Dictionary<string, string> ParseKeyValueLines(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string rawLine in text.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            int separator = line.IndexOf(':');
            if (separator <= 0 || separator >= line.Length - 1)
            {
                separator = line.IndexOf('=');
            }

            if (separator <= 0 || separator >= line.Length - 1)
            {
                continue;
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
            values[key] = value;
        }

        return values;
    }

    private static bool TryGetInt64(
        IReadOnlyDictionary<string, string> values,
        string key,
        out long value)
    {
        value = 0;
        return values.TryGetValue(key, out string? raw)
            && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetInt32(
        IReadOnlyDictionary<string, string> values,
        string key,
        out int value)
    {
        value = 0;
        return values.TryGetValue(key, out string? raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string FormatBytes(long bytes)
    {
        const double kib = 1024.0;
        const double mib = kib * 1024.0;
        const double gib = mib * 1024.0;

        if (bytes >= gib)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:N2} GiB", bytes / gib);
        }

        if (bytes >= mib)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:N2} MiB", bytes / mib);
        }

        if (bytes >= kib)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:N2} KiB", bytes / kib);
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:N0} bytes", bytes);
    }

    private static string FormatPercent(double ratio) =>
        string.Format(CultureInfo.InvariantCulture, "{0:0.00}%", ratio * 100.0);

    private static string FormatInteger(long value) =>
        value.ToString("N0", CultureInfo.InvariantCulture);

    private static bool IsExpectedIoException(Exception ex) =>
        ex is ArgumentException
        or IOException
        or NotSupportedException
        or UnauthorizedAccessException
        or System.Security.SecurityException
        or PathTooLongException;
}
