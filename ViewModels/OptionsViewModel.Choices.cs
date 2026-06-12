using CommunityToolkit.Mvvm.ComponentModel;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.RedumpCatalog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class OptionsViewModel
{

    private static string BuildManualProcessorSelectionDescription(int selectedProcessorCount)
    {
        int availableLogicalProcessors = ProcessorTopologyService.GetAvailableLogicalProcessorCount();
        int effectiveProcessorCount = Math.Clamp(selectedProcessorCount, 1, availableLogicalProcessors);
        double usageRatio = (double)effectiveProcessorCount / availableLogicalProcessors;

        string resourceKey = usageRatio switch
        {
            <= 0.25d => "LocAdv_ProcessorSelectionManualLow",
            <= 0.50d => "LocAdv_ProcessorSelectionManualBalanced",
            <= 0.75d => "LocAdv_ProcessorSelectionManualHigh",
            _ => "LocAdv_ProcessorSelectionManualMaximum"
        };

        return ArabicUi.Format(resourceKey, effectiveProcessorCount, availableLogicalProcessors);
    }


    private ChoiceOption? ResolveIsoCreateOverride(IsoCreateCommandOverride value) =>
        IsoCreateOverrideOptions.FirstOrDefault(x => string.Equals(x.Key, value.ToString(), StringComparison.OrdinalIgnoreCase))
        ?? IsoCreateOverrideOptions.FirstOrDefault();
}
