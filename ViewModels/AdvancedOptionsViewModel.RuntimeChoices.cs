using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class AdvancedOptionsViewModel
{
    private void LoadProcessorChoices()
    {
        ProcessorOptions.Clear();

        int logicalCount = ProcessorTopologyService.GetAvailableLogicalProcessorCount();
        ProcessorOptions.Add(new AdvancedProcessorOption(0, ArabicUi.Get("LocAdv_ProcessorAutoRecommendedLabel")));

        foreach (int value in BuildProcessorValues(logicalCount))
        {
            ProcessorOptions.Add(new AdvancedProcessorOption(value, value.ToString()));
        }
    }

    private static IEnumerable<int> BuildProcessorValues(int logicalCount)
    {
        if (logicalCount <= 0)
        {
            yield break;
        }

        if (logicalCount <= 16)
        {
            for (int i = 1; i <= logicalCount; i++)
            {
                yield return i;
            }

            yield break;
        }

        int[] preferred = [1, 2, 4, 6, 8, 10, 12, 16, 20, 24, 32];
        foreach (int value in preferred.Where(value => value < logicalCount))
        {
            yield return value;
        }

        yield return logicalCount;
    }

    private static string BuildProcessorSummary()
    {
        ProcessorTopologyInfo info = ProcessorTopologyService.ReadCurrent();
        string coreText = info.PhysicalCores > 0
            ? info.PhysicalCores.ToString()
            : ArabicUi.Get("LocAdv_ProcessorUnknownCoreCount");

        return ArabicUi.Format(
            "LocAdv_ProcessorSummary",
            info.Name,
            coreText,
            info.AvailableLogicalProcessors);
    }

    private AdvancedChoiceOption? ResolveCompressionPreset(string? compressionValue)
    {
        string normalized = string.IsNullOrWhiteSpace(compressionValue)
            ? "preset:default"
            : compressionValue.Trim();

        if (normalized.StartsWith("preset:", StringComparison.OrdinalIgnoreCase))
        {
            string key = normalized[7..].ToLowerInvariant();
            return CompressionPresetOptions.FirstOrDefault(x => x.Key == key)
                ?? CompressionPresetOptions.FirstOrDefault(x => x.Key == "default");
        }

        return CompressionPresetOptions.FirstOrDefault(x => x.Key == "default");
    }

    private AdvancedChoiceOption? ResolveHunkPreset(int hunkSizeBytes) => hunkSizeBytes switch
    {
        -1 => HunkPresetOptions.FirstOrDefault(x => x.Key == "small"),
        -2 => HunkPresetOptions.FirstOrDefault(x => x.Key == "balanced"),
        -3 => HunkPresetOptions.FirstOrDefault(x => x.Key == "large"),
        _ => HunkPresetOptions.FirstOrDefault(x => x.Key == "default")
    };

    private static string? NormalizeStoredTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string text = value.Trim();
        string arabicPrefix = ArabicUi.Get("LocAdv_DatabaseLastSyncedPrefix");

        if (text.StartsWith("Last synced:", StringComparison.OrdinalIgnoreCase))
        {
            text = text["Last synced:".Length..].Trim();
        }
        else if (text.StartsWith(arabicPrefix, StringComparison.OrdinalIgnoreCase))
        {
            text = text[arabicPrefix.Length..].Trim();
        }

        return text == "-" ? null : text;
    }
}