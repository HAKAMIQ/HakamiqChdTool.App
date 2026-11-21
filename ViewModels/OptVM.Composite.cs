using HakamiqChdTool.App.Localization;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace HakamiqChdTool.App.ViewModels;

/// <summary>
/// One list in the window for settings that used to be two boxes. The saved settings do not change: each list reads
/// and writes the same properties as before (OrganizeByPlatform/Region, DeleteSourceAfterVerified*, M3U, GD-ROM
/// format, PerformanceMode with MaxProcessorCount).
/// </summary>
public sealed partial class OptionsViewModel
{
    private const string ThreadsKeyPrefix = "threads:";

    public ObservableCollection<ChoiceOption> OutputOrganizeOptions { get; } = [];

    public ObservableCollection<ChoiceOption> DeleteSourceOptions { get; } = [];

    public ObservableCollection<ChoiceOption> M3uOptions { get; } = [];

    public ObservableCollection<ChoiceOption> DreamcastExtractOptions { get; } = [];

    /// <summary>Safe, Balanced and Fast pick the thread count themselves; a number fixes it (and the mode is unused).</summary>
    public ObservableCollection<ChoiceOption> SpeedOptions { get; } = [];

    private void BuildCompositeChoices()
    {
        Fill(OutputOrganizeOptions,
            Choice("none", "LocOpt_OrganizeNoneLabel", "LocOpt_OrganizeNoneDescription"),
            Choice("platform", "LocOpt_OrganizePlatformLabel", "LocOpt_OrganizePlatformDescription"),
            Choice("region", "LocOpt_OrganizeRegionLabel", "LocOpt_OrganizeRegionDescription"),
            Choice("both", "LocOpt_OrganizeBothLabel", "LocOpt_OrganizeBothDescription"));
        Fill(DeleteSourceOptions,
            Choice("none", "LocOpt_DeleteSourceNoneLabel", "LocOpt_DeleteSourceNoneDescription"),
            Choice("conversion", "LocOpt_DeleteSourceConversionLabel", "LocOpt_DeleteSourceConversionDescription"),
            Choice("extraction", "LocOpt_DeleteSourceExtractionLabel", "LocOpt_DeleteSourceExtractionDescription"),
            Choice("both", "LocOpt_DeleteSourceBothLabel", "LocOpt_DeleteSourceBothDescription"));
        Fill(M3uOptions,
            Choice("off", "LocOpt_M3uOffLabel", "LocOpt_M3uOffDescription"),
            Choice("keep", "LocOpt_M3uKeepLabel", "LocOpt_M3uKeepDescription"),
            Choice("replace", "LocOpt_M3uReplaceLabel", "LocOpt_M3uReplaceDescription"));
        Fill(DreamcastExtractOptions,
            Choice("gdi", "LocOpt_DreamcastGdiLabel", "LocOpt_DreamcastGdiDescription"),
            Choice("cuebin", "LocOpt_DreamcastCueBinLabel", "LocOpt_DreamcastCueBinDescription"));

        var speed = PerformanceModeOptions.Select(mode => new ChoiceOption(mode.Key, mode.Label, mode.Description)).ToList();
        foreach (ProcessorOption processor in ProcessorOptions.Where(p => p.Value > 0))
        {
            string count = processor.Value.ToString(CultureInfo.InvariantCulture);
            speed.Add(new ChoiceOption(
                ThreadsKeyPrefix + count,
                ArabicUi.Format("LocOpt_SpeedThreadsLabel", count),
                ArabicUi.Format("LocOpt_SpeedThreadsDescription", count)));
        }

        Fill(SpeedOptions, [.. speed]);
        NotifyComposites(null);
    }

    public ChoiceOption? SelectedOutputOrganize
    {
        get => Pick(OutputOrganizeOptions, (OrganizeByPlatform, OrganizeByRegion) switch
        {
            (true, true) => "both",
            (true, false) => "platform",
            (false, true) => "region",
            _ => "none"
        });
        set
        {
            if (value is null)
            {
                return;
            }

            OrganizeByPlatform = value.Key is "platform" or "both";
            OrganizeByRegion = value.Key is "region" or "both";
        }
    }

    public ChoiceOption? SelectedDeleteSource
    {
        get => Pick(DeleteSourceOptions, (DeleteSourceAfterVerifiedConversion, DeleteSourceAfterVerifiedExtraction) switch
        {
            (true, true) => "both",
            (true, false) => "conversion",
            (false, true) => "extraction",
            _ => "none"
        });
        set
        {
            if (value is null)
            {
                return;
            }

            DeleteSourceAfterVerifiedConversion = value.Key is "conversion" or "both";
            DeleteSourceAfterVerifiedExtraction = value.Key is "extraction" or "both";
        }
    }

    public ChoiceOption? SelectedM3u
    {
        get => Pick(M3uOptions, !EnableAutoM3uGeneration ? "off" : OverwriteExistingM3uPlaylists ? "replace" : "keep");
        set
        {
            if (value is null)
            {
                return;
            }

            EnableAutoM3uGeneration = value.Key != "off";
            OverwriteExistingM3uPlaylists = value.Key == "replace";
        }
    }

    public ChoiceOption? SelectedDreamcastExtract
    {
        get => Pick(DreamcastExtractOptions, ExtractGdRomAsCueBin ? "cuebin" : "gdi");
        set
        {
            if (value is not null)
            {
                ExtractGdRomAsCueBin = value.Key == "cuebin";
            }
        }
    }

    // A fixed thread count wins over the mode at run time, so a saved count is what the list shows.
    public ChoiceOption? SelectedSpeed
    {
        get => Pick(SpeedOptions, SelectedProcessorOption is { Value: > 0 } fixedCount
            ? ThreadsKeyPrefix + fixedCount.Value.ToString(CultureInfo.InvariantCulture)
            : SelectedPerformanceMode?.Key);
        set
        {
            if (value is null)
            {
                return;
            }

            if (value.Key.StartsWith(ThreadsKeyPrefix, System.StringComparison.Ordinal)
                && int.TryParse(value.Key[ThreadsKeyPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int count))
            {
                SelectedProcessorOption = ProcessorOptions.FirstOrDefault(p => p.Value == count) ?? SelectedProcessorOption;
                return;
            }

            SelectedPerformanceMode = PerformanceModeOptions.FirstOrDefault(m => m.Key == value.Key) ?? SelectedPerformanceMode;
            SelectedProcessorOption = ProcessorOptions.FirstOrDefault(p => p.Value == 0) ?? SelectedProcessorOption;
        }
    }

    private bool _isConversionAdvancedExpanded;

    /// <summary>The Advanced part of the Conversion tab; opened on load when one of its values is not the default.</summary>
    public bool IsConversionAdvancedExpanded
    {
        get => _isConversionAdvancedExpanded;
        set => SetProperty(ref _isConversionAdvancedExpanded, value);
    }

    private void ExpandAdvancedWhenChanged()
    {
        IsConversionAdvancedExpanded = !UseBundledChdman
            || SelectedHunkPreset?.Key is not (null or "default")
            || SelectedIsoCreateOverride?.Key is not (null or "Auto")
            || SelectedChdPlatformProfile?.Key is not (null or "auto");
    }

    public string SpeedDescription => SelectedSpeed?.Description ?? string.Empty;

    public string DeleteSourceDescription => ArabicUi.Get(VerifyAfterConversion
        ? "LocOpt_DeleteSourceDescription"
        : "LocOpt_DeleteSourceNeedsVerify");

    public string M3uDescription => SelectedM3u?.Description ?? string.Empty;

    private void NotifyComposites(string? changedProperty)
    {
        switch (changedProperty)
        {
            case nameof(OrganizeByPlatform) or nameof(OrganizeByRegion):
                OnPropertyChanged(nameof(SelectedOutputOrganize));
                break;
            case nameof(DeleteSourceAfterVerifiedConversion) or nameof(DeleteSourceAfterVerifiedExtraction):
                OnPropertyChanged(nameof(SelectedDeleteSource));
                break;
            case nameof(VerifyAfterConversion):
                OnPropertyChanged(nameof(DeleteSourceDescription));
                break;
            case nameof(EnableAutoM3uGeneration) or nameof(OverwriteExistingM3uPlaylists):
                OnPropertyChanged(nameof(SelectedM3u));
                OnPropertyChanged(nameof(M3uDescription));
                break;
            case nameof(ExtractGdRomAsCueBin):
                OnPropertyChanged(nameof(SelectedDreamcastExtract));
                break;
            case nameof(SelectedPerformanceMode) or nameof(SelectedProcessorOption):
                OnPropertyChanged(nameof(SelectedSpeed));
                OnPropertyChanged(nameof(SpeedDescription));
                break;
            case null:
                foreach (string name in new[]
                {
                    nameof(SelectedOutputOrganize), nameof(SelectedDeleteSource), nameof(DeleteSourceDescription), nameof(SelectedM3u),
                    nameof(M3uDescription), nameof(SelectedDreamcastExtract), nameof(SelectedSpeed), nameof(SpeedDescription)
                })
                {
                    OnPropertyChanged(name);
                }

                break;
        }
    }

    private static ChoiceOption Choice(string key, string labelKey, string descriptionKey) =>
        new(key, ArabicUi.Get(labelKey), ArabicUi.Get(descriptionKey));

    private static void Fill(ObservableCollection<ChoiceOption> list, params ChoiceOption[] items)
    {
        list.Clear();
        foreach (ChoiceOption item in items)
        {
            list.Add(item);
        }
    }

    private static ChoiceOption? Pick(ObservableCollection<ChoiceOption> list, string? key) =>
        list.FirstOrDefault(option => option.Key == key) ?? list.FirstOrDefault();
}
