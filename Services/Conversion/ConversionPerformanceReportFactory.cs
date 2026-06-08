using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Storage;
using System;
using System.Globalization;
using System.Text;

namespace HakamiqChdTool.App.Services.Conversion;

public sealed record ConversionPerformanceReport(
    long InputBytes,
    long OutputBytes,
    double CompressionRatio,
    long SavedBytes,
    TimeSpan ChdmanDuration,
    TimeSpan VerifyDuration,
    double AverageOutputMBps,
    int NumProcessors,
    string CompressionCodecs,
    int? HunkSizeBytes,
    bool SourceAndOutputSameVolume,
    bool SourceIsExternal,
    bool OutputIsExternal,
    bool PowerGuardEnabled,
    bool TemperatureAvailable,
    string TemperatureCapability,
    int? MaxTemperatureCelsius,
    bool HadInputReadWarning,
    string? CompressionExplanationKey);

internal sealed class ConversionPerformanceReportFactory
{
    public const string PoorCompressionExplanationKey = "LocConversionReport_PoorCompressionPs3";

    public ConversionPerformanceReport Create(
        long inputBytes,
        long outputBytes,
        ChdConversionResult conversion,
        TimeSpan verifyDuration,
        StorageTopologySnapshot topology,
        bool powerGuardEnabled,
        bool temperatureAvailable,
        StorageTemperatureCapability temperatureCapability,
        int? maxTemperatureCelsius,
        bool hadInputReadWarning)
    {
        ArgumentNullException.ThrowIfNull(conversion);
        ArgumentNullException.ThrowIfNull(topology);

        long safeInputBytes = Math.Max(0, inputBytes);
        long safeOutputBytes = Math.Max(0, outputBytes);
        long savedBytes = Math.Max(0, safeInputBytes - safeOutputBytes);
        double compressionRatio = safeInputBytes <= 0
            ? 0
            : safeOutputBytes / (double)safeInputBytes;

        double averageOutputMBps = conversion.ChdmanDuration.TotalSeconds <= 0 || safeOutputBytes <= 0
            ? 0
            : safeOutputBytes / 1024d / 1024d / conversion.ChdmanDuration.TotalSeconds;

        string? explanationKey = compressionRatio >= 0.97d && safeInputBytes > 0 && safeOutputBytes > 0
            ? PoorCompressionExplanationKey
            : null;

        return new ConversionPerformanceReport(
            safeInputBytes,
            safeOutputBytes,
            compressionRatio,
            savedBytes,
            conversion.ChdmanDuration,
            verifyDuration,
            averageOutputMBps,
            conversion.NumProcessors,
            string.IsNullOrWhiteSpace(conversion.CompressionCodecs) ? "default" : conversion.CompressionCodecs,
            conversion.HunkSizeBytes,
            topology.SourceAndFinalOutputSameVolume,
            topology.SourceIsExternal,
            topology.OutputIsExternal,
            powerGuardEnabled,
            temperatureAvailable,
            temperatureCapability.ToString(),
            maxTemperatureCelsius,
            hadInputReadWarning,
            explanationKey);
    }
}

public static class ConversionPerformanceReportFormatter
{
    public static string FormatArabic(ConversionPerformanceReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var builder = new StringBuilder();
        builder.AppendLine("حجم ISO: " + FormatBytes(report.InputBytes));
        builder.AppendLine("حجم CHD: " + FormatBytes(report.OutputBytes));
        builder.AppendLine("نسبة الضغط: " + (report.CompressionRatio * 100d).ToString("0.0", CultureInfo.InvariantCulture) + "%");
        builder.AppendLine("التوفير: " + FormatBytes(report.SavedBytes));
        builder.AppendLine("وقت التحويل: " + FormatDuration(report.ChdmanDuration));
        builder.AppendLine("وقت التحقق: " + FormatDuration(report.VerifyDuration));
        builder.AppendLine("عدد مسارات الضغط: " + (report.NumProcessors > 0 ? report.NumProcessors.ToString(CultureInfo.InvariantCulture) : "افتراضي"));
        builder.AppendLine("المصدر والخرج على نفس القرص: " + FormatYesNo(report.SourceAndOutputSameVolume));
        builder.AppendLine("SourceExternal: " + FormatBoolInvariant(report.SourceIsExternal));
        builder.AppendLine("OutputExternal: " + FormatBoolInvariant(report.OutputIsExternal));
        builder.AppendLine("منع السكون أثناء التحويل: " + (report.PowerGuardEnabled ? "مفعل" : "غير مفعل"));
        builder.AppendLine("حرارة الهارد: " + FormatTemperature(report));

        if (!string.IsNullOrWhiteSpace(report.CompressionExplanationKey))
        {
            builder.AppendLine("التفسير: بيانات اللعبة مضغوطة مسبقا، لذلك نسبة الضغط ضعيفة.");
        }

        return builder.ToString().Trim();
    }

    private static string FormatYesNo(bool value) => value ? "نعم" : "لا";

    private static string FormatBoolInvariant(bool value) => value ? "true" : "false";

    private static string FormatTemperature(ConversionPerformanceReport report)
    {
        if (!report.TemperatureAvailable)
        {
            return string.IsNullOrWhiteSpace(report.TemperatureCapability)
                || string.Equals(report.TemperatureCapability, StorageTemperatureCapability.Unknown.ToString(), StringComparison.Ordinal)
                ? "غير متاحة"
                : "غير متاحة (" + report.TemperatureCapability + ")";
        }

        return report.MaxTemperatureCelsius is int value
            ? "أعلى حرارة " + value.ToString(CultureInfo.InvariantCulture) + "°C"
            : "متاحة";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return "0:00";
        }

        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private static string FormatBytes(long bytes)
    {
        const double kib = 1024d;
        const double mib = kib * 1024d;
        const double gib = mib * 1024d;

        if (bytes >= gib)
        {
            return (bytes / gib).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
        }

        if (bytes >= mib)
        {
            return (bytes / mib).ToString("0.0", CultureInfo.InvariantCulture) + " MiB";
        }

        if (bytes >= kib)
        {
            return (bytes / kib).ToString("0.0", CultureInfo.InvariantCulture) + " KiB";
        }

        return bytes.ToString("N0", CultureInfo.InvariantCulture) + " B";
    }
}
