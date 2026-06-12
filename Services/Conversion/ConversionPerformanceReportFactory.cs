using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Storage;
using System;
using System.Globalization;
using System.Text;

namespace HakamiqChdTool.App.Services.Conversion;

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
            string.IsNullOrWhiteSpace(conversion.RequestedCompressionPreset) ? "default" : conversion.RequestedCompressionPreset,
            string.IsNullOrWhiteSpace(conversion.ResolvedCompressionCodecs) ? "default" : conversion.ResolvedCompressionCodecs,
            string.IsNullOrWhiteSpace(conversion.EffectiveCompressionCodecs)
                ? (string.IsNullOrWhiteSpace(conversion.CompressionCodecs) ? "default" : conversion.CompressionCodecs)
                : conversion.EffectiveCompressionCodecs,
            conversion.EffectiveCompressionSameAsMameDefault,
            conversion.CompressionTruthNoteKey,
            conversion.HunkSizeBytes,
            string.IsNullOrWhiteSpace(conversion.RequestedProfile) ? "auto" : conversion.RequestedProfile,
            string.IsNullOrWhiteSpace(conversion.ResolvedCommand) ? "unknown" : conversion.ResolvedCommand,
            string.IsNullOrWhiteSpace(conversion.ResolvedCompression) ? "default" : conversion.ResolvedCompression,
            conversion.ResolvedHunkSize,
            string.IsNullOrWhiteSpace(conversion.EffectiveCompression)
                ? (string.IsNullOrWhiteSpace(conversion.EffectiveCompressionCodecs) ? "default" : conversion.EffectiveCompressionCodecs)
                : conversion.EffectiveCompression,
            conversion.EffectiveHunkSize,
            conversion.SameAsMameDefault || conversion.EffectiveCompressionSameAsMameDefault,
            conversion.CompatibilityNotes,
            conversion.ChdmanVersion,
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
        builder.AppendLine("RequestedProfile: " + FormatInvariant(report.RequestedProfile));
        builder.AppendLine("ResolvedCommand: " + FormatInvariant(report.ResolvedCommand));
        builder.AppendLine("ResolvedCompression: " + FormatInvariant(report.ResolvedCompression));
        builder.AppendLine("ResolvedHunkSize: " + FormatNullableHunk(report.ResolvedHunkSize));
        builder.AppendLine("EffectiveCompression: " + FormatInvariant(report.EffectiveCompression));
        builder.AppendLine("EffectiveHunkSize: " + FormatNullableHunk(report.EffectiveHunkSize));
        builder.AppendLine("SameAsMameDefault: " + FormatBoolInvariant(report.SameAsMameDefault));
        builder.AppendLine("CompatibilityNotes: " + FormatInvariant(report.CompatibilityNotes));
        builder.AppendLine("ChdmanVersion: " + FormatInvariant(report.ChdmanVersion));
        if (!string.IsNullOrWhiteSpace(report.RequestedCompressionPreset))
        {
            builder.AppendLine("وضع الضغط المطلوب: " + FormatCompressionPreset(report.RequestedCompressionPreset));
            builder.AppendLine("ضغط CHD المطبق: " + (string.IsNullOrWhiteSpace(report.EffectiveCompressionCodecs) ? report.CompressionCodecs : report.EffectiveCompressionCodecs));

            if (report.EffectiveCompressionSameAsMameDefault && string.Equals(report.RequestedCompressionPreset, "max", StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine("ملاحظة الضغط: قد لا يتغير الحجم لأن ضغط MAME الافتراضي للـ CD يستخدم CD LZMA بالفعل.");
            }
        }
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

    private static string FormatCompressionPreset(string value) => value.ToLowerInvariant() switch
    {
        "default" => "افتراضي من MAME",
        "fast" => "سريع",
        "balanced" => "متوازن",
        "max" => "LZMA",
        "explicit" => "مخصص",
        _ => value
    };

    private static string FormatBoolInvariant(bool value) => value ? "true" : "false";

    private static string FormatInvariant(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static string FormatNullableHunk(int? value) => value is > 0
        ? value.GetValueOrDefault().ToString(CultureInfo.InvariantCulture)
        : "default";

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
