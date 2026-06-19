using HakamiqChdTool.App.Core.Advisory;
using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;

namespace HakamiqChdTool.App.Services;

public sealed class FormatSafetyAdvisor
{
    private const string SourceName = nameof(FormatSafetyAdvisor);

    private static readonly ILogger Log = global::Serilog.Log.ForContext<FormatSafetyAdvisor>();

    public IReadOnlyList<FormatSafetyAdvice> Assess(string inputPath, string? platformHint = null)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return [];
        }

        string extension = Path.GetExtension(inputPath).ToLowerInvariant();
        string platform = platformHint ?? string.Empty;
        var advice = new List<FormatSafetyAdvice>();

        if (extension == ".cso")
        {
            advice.Add(Advice(
                "LocFormatSafety_CsoTitle",
                "LocWorkflow_CsoToIsoThenChdNotice",
                FormatSafetyLevel.EmulatorOnly,
                [".cso", ".iso", ".chd"],
                "LocFormatSafety_KeepOriginalSource"));
        }

        if (extension == ".chd")
        {
            advice.Add(Advice(
                "LocFormatSafety_ChdTitle",
                "LocFormatSafety_ChdEmulatorOnly",
                FormatSafetyLevel.EmulatorOnly,
                [".chd"],
                "LocFormatSafety_KeepOriginalSource"));
        }

        if (extension == ".iso" && LooksCdBased(platform, inputPath))
        {
            advice.Add(Advice(
                "LocFormatSafety_MixedModeTitle",
                "LocFormatSafety_MixedModeIsoWarning",
                FormatSafetyLevel.MostlyReversible,
                [".iso", ".cue", ".bin", ".gdi"],
                "LocFormatSafety_PreferCueBinOrGdi"));
        }

        if (extension is ".cue" or ".gdi")
        {
            advice.Add(Advice(
                "LocFormatSafety_MultiTrackTitle",
                "LocFormatSafety_DoNotDropSidecars",
                FormatSafetyLevel.ArchiveQuality,
                [extension, ".bin", ".sbi"],
                "LocFormatSafety_KeepSidecars"));
        }

        if (extension is ".ecm" or ".pbp" or ".dax" or ".jso" or ".zso")
        {
            advice.Add(Advice(
                "LocFormatSafety_FormatOutsideScopeTitle",
                "LocFormatSafety_PspPs1CompressedOutsideScope",
                FormatSafetyLevel.Unsupported,
                [extension],
                "LocFormatSafety_KeepOriginalSource"));
        }

        if (extension is ".rvz" or ".gcz" or ".wbfs" or ".wia" or ".nkit" or ".wux" or ".wua" or ".xiso" or ".zar" or ".nsz" or ".z3ds")
        {
            advice.Add(Advice(
                "LocFormatSafety_FormatOutsideScopeTitle",
                "LocFormatSafety_PlatformSpecificOutsideChdScope",
                FormatSafetyLevel.Unsupported,
                [extension],
                "LocFormatSafety_UsePlatformTool"));
        }

        if (LooksDestructiveName(inputPath))
        {
            advice.Add(Advice(
                "LocFormatSafety_DestructiveTitle",
                "LocFormatSafety_DestructiveModificationWarning",
                FormatSafetyLevel.Destructive,
                [extension],
                "LocFormatSafety_KeepOriginalSource"));
        }

        if (advice.Count > 0)
        {
            Log.Debug(
                "Format safety advice generated. Input={Input}; Platform={Platform}; AdviceCount={AdviceCount}",
                inputPath,
                platform,
                advice.Count);
        }

        return advice;
    }

    public QueueIntakeAdvisory? BuildQueueAdvisory(
        string inputPath,
        string? platformHint,
        QueueIntakeAdvisory? existingAdvisory)
    {
        IReadOnlyList<FormatSafetyAdvice> advice = Assess(inputPath, platformHint);
        if (advice.Count == 0)
        {
            return existingAdvisory;
        }

        List<QueueIntakeAdvisoryReason> warnings = [];
        foreach (FormatSafetyAdvice item in advice)
        {
            warnings.Add(new QueueIntakeAdvisoryReason(
                "FORMAT_SAFETY_" + item.Level.ToString().ToUpperInvariant(),
                item.MessageKey,
                ToSeverity(item.Level),
                SourceName));
        }

        if (existingAdvisory is not null)
        {
            return existingAdvisory with
            {
                Warnings = [.. existingAdvisory.Warnings, .. warnings]
            };
        }

        return new QueueIntakeAdvisory(
            QueueIntakeAdvisoryAction.Warn,
            80,
            false,
            [],
            warnings,
            null,
            platformHint,
            null,
            null,
            null);
    }

    private static FormatSafetyAdvice Advice(
        string titleKey,
        string messageKey,
        FormatSafetyLevel level,
        IEnumerable<string> relatedFormats,
        string recommendedActionKey) =>
        FormatSafetyAdvice.NonBlocking(titleKey, messageKey, level, relatedFormats, recommendedActionKey);

    private static QueueIntakeAdvisorySeverity ToSeverity(FormatSafetyLevel level) =>
        level switch
        {
            FormatSafetyLevel.Destructive => QueueIntakeAdvisorySeverity.Warning,
            FormatSafetyLevel.Unsupported => QueueIntakeAdvisorySeverity.Warning,
            FormatSafetyLevel.Unknown => QueueIntakeAdvisorySeverity.Info,
            _ => QueueIntakeAdvisorySeverity.Info
        };

    private static bool LooksCdBased(string platform, string inputPath)
    {
        string text = (platform + " " + inputPath).ToLowerInvariant();
        return text.Contains("playstation", StringComparison.Ordinal)
            || text.Contains("ps1", StringComparison.Ordinal)
            || text.Contains("psx", StringComparison.Ordinal)
            || text.Contains("saturn", StringComparison.Ordinal)
            || text.Contains("dreamcast", StringComparison.Ordinal)
            || text.Contains("sega cd", StringComparison.Ordinal)
            || text.Contains("mega cd", StringComparison.Ordinal)
            || text.Contains("pc engine", StringComparison.Ordinal)
            || text.Contains("turbografx", StringComparison.Ordinal)
            || text.Contains("neo geo cd", StringComparison.Ordinal);
    }

    private static bool LooksDestructiveName(string inputPath)
    {
        string name = Path.GetFileNameWithoutExtension(inputPath).ToLowerInvariant();
        return name.Contains("scrub", StringComparison.Ordinal)
            || name.Contains("trim", StringComparison.Ordinal)
            || name.Contains("ripped", StringComparison.Ordinal)
            || name.Contains("dummy", StringComparison.Ordinal)
            || name.Contains("rebuilt", StringComparison.Ordinal)
            || name.Contains("rebuild", StringComparison.Ordinal);
    }
}
