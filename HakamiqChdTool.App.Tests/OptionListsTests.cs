using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.ViewModels;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // Each list that replaced two options reads and writes the same saved settings, with nothing lost either way.
    private static void TestOptionListsMapToSavedSettings()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach ((bool platform, bool region, string key) in new[] { (false, false, "none"), (true, false, "platform"), (false, true, "region"), (true, true, "both") })
                {
                    var source = new AppSettings { OrganizeByPlatform = platform, OrganizeByRegion = region };
                    var options = Open(source);
                    AssertEqual(key, options.SelectedOutputOrganize?.Key, $"Folders {platform}/{region} open as {key}.");
                    AppSettings result = options.BuildResultSettings(source);
                    AssertTrue(result.OrganizeByPlatform == platform && result.OrganizeByRegion == region, $"{key} saves unchanged.");
                    AssertFalse(options.HasPendingChanges, $"Opening with {key} is not a change.");
                }

                foreach ((bool conversion, bool extraction, string key) in new[] { (false, false, "none"), (true, false, "conversion"), (false, true, "extraction"), (true, true, "both") })
                {
                    var source = new AppSettings { DeleteSourceAfterVerifiedConversion = conversion, DeleteSourceAfterVerifiedExtraction = extraction };
                    var options = Open(source);
                    AssertEqual(key, options.SelectedDeleteSource?.Key, $"Delete {conversion}/{extraction} opens as {key}.");
                    AppSettings result = options.BuildResultSettings(source);
                    AssertTrue(result.DeleteSourceAfterVerifiedConversion == conversion && result.DeleteSourceAfterVerifiedExtraction == extraction, $"{key} saves unchanged.");
                }

                foreach ((bool enabled, bool overwrite, string key) in new[] { (false, false, "off"), (true, false, "keep"), (true, true, "replace") })
                {
                    var source = new AppSettings { EnableAutoM3uGeneration = enabled, OverwriteExistingM3uPlaylists = overwrite };
                    var options = Open(source);
                    AssertEqual(key, options.SelectedM3u?.Key, $"M3U {enabled}/{overwrite} opens as {key}.");
                    AppSettings result = options.BuildResultSettings(source);
                    AssertTrue(result.EnableAutoM3uGeneration == enabled && result.OverwriteExistingM3uPlaylists == overwrite, $"M3U {key} saves unchanged.");
                }

                var dreamcast = Open(new AppSettings { ExtractGdRomAsCueBin = true });
                AssertEqual("cuebin", dreamcast.SelectedDreamcastExtract?.Key, "CUE/BIN for GD-ROM opens as CUE/BIN.");
                dreamcast.SelectedDreamcastExtract = dreamcast.DreamcastExtractOptions.Single(o => o.Key == "gdi");
                AssertFalse(dreamcast.BuildResultSettings(new AppSettings()).ExtractGdRomAsCueBin, "Choosing GDI saves it.");

                // A saved thread count wins at run time, so it is what the list shows; the old mode is kept until a mode is chosen.
                var fixedCount = new AppSettings { PerformanceMode = ConversionPerformanceMode.Fast, MaxProcessorCount = 2 };
                var speed = Open(fixedCount);
                AssertEqual("threads:2", speed.SelectedSpeed?.Key, "A saved count of 2 opens as the fixed count.");
                AppSettings unchanged = speed.BuildResultSettings(fixedCount);
                AssertTrue(unchanged.MaxProcessorCount == 2 && unchanged.PerformanceMode == ConversionPerformanceMode.Fast, "Saving without a change keeps both values.");

                speed.SelectedSpeed = speed.SpeedOptions.Single(o => o.Key == "Balanced");
                AppSettings balanced = speed.BuildResultSettings(fixedCount);
                AssertTrue(balanced.MaxProcessorCount == 0 && balanced.PerformanceMode == ConversionPerformanceMode.Balanced, "Choosing a mode clears the fixed count.");

                speed.SelectedSpeed = speed.SpeedOptions.Single(o => o.Key == "threads:1");
                AssertEqual(1, speed.BuildResultSettings(fixedCount).MaxProcessorCount, "Choosing a count saves it.");

                AssertFalse(Open(new AppSettings()).IsConversionAdvancedExpanded, "Advanced stays folded with default values.");
                AssertTrue(Open(new AppSettings { UseBundledChdman = false, ExternalChdmanPath = @"C:\Tools\chdman.exe" }).IsConversionAdvancedExpanded,
                    "Advanced opens when an external chdman is chosen, so it is not hidden.");
                AssertTrue(Open(new AppSettings { HunkSizeBytes = -3 }).IsConversionAdvancedExpanded, "Advanced opens for a non-default hunk size.");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }

        static OptionsViewModel Open(AppSettings settings)
        {
            var options = new OptionsViewModel();
            options.Load(settings);
            return options;
        }
    }
}