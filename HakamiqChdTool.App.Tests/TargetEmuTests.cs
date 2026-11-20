using HakamiqChdTool.App.Core.Chd;
using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.ViewModels;
using System.IO;
using System.Reflection;
using System.Threading;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // Automatic changes nothing; PCSX2/PPSSPP gives DVD CHDs 2048-byte hunks and leaves CD and HD alone;
    // RetroArch keeps zstd out of every codec choice.
    private static void TestTargetEmulatorPolicy()
    {
        var source = new AppSettings { CompressionCodecs = "preset:fast", HunkSizeBytes = -2 };
        AppSettings auto = QueueTransitionService.BuildEffectiveSettings(source, QueueExecutionProfile.Standard);
        AssertEqual("preset:fast", auto.CompressionCodecs, "Automatic keeps the compression.");
        AssertEqual(-2, auto.HunkSizeBytes, "Automatic keeps the hunk size.");
        AssertEqual("auto", new AppSettings().TargetEmulator, "Automatic is the default.");

        source.TargetEmulator = "dvd2048";
        AppSettings dvd = QueueTransitionService.BuildEffectiveSettings(source, QueueExecutionProfile.Standard);
        AssertEqual(2048, dvd.HunkSizeBytes, "PCSX2 and PPSSPP use 2048-byte hunks.");
        AssertEqual(-2, source.HunkSizeBytes, "The saved settings are not changed.");
        var preparation = new ChdCommandPreparationService();
        AssertEqual(2048, preparation.ResolveHunkSizeSetting(dvd.HunkSizeBytes, "createdvd", "game.iso", out _), "createdvd gets -hs 2048.");
        AssertEqual(0, preparation.ResolveHunkSizeSetting(dvd.HunkSizeBytes, "createhd", "drive.img", out _), "Hard disks are not affected.");
        AssertTrue(preparation.ResolveHunkSizeSetting(dvd.HunkSizeBytes, "createcd", "game.cue", out _) != 2048, "CD CHDs do not get a DVD hunk size.");

        source.TargetEmulator = "nozstd";
        foreach ((string from, string to) in new[]
        {
            ("preset:fast", "preset:max"), ("preset:balanced", "preset:max"), ("preset:max", "preset:max"),
            ("preset:default", "preset:default"), ("zstd,zlib,huff,flac", "zlib,huff,flac"), ("cdzs,cdfl", "cdfl"), ("zstd", "preset:default")
        })
        {
            source.CompressionCodecs = from;
            AppSettings compat = QueueTransitionService.BuildEffectiveSettings(source, QueueExecutionProfile.Standard);
            AssertEqual(to, compat.CompressionCodecs, $"Without zstd, {from} becomes {to}.");
            string[] commands = from.StartsWith("preset:", StringComparison.Ordinal) ? ["createcd", "createdvd"] : from.StartsWith("cd", StringComparison.Ordinal) ? ["createcd"] : ["createdvd"];
            foreach (string command in commands)
            {
                string resolved = preparation.ResolveCompressionSettingWithTruth(compat.CompressionCodecs, command).EffectiveCompression;
                AssertFalse(resolved.Contains("zstd", StringComparison.OrdinalIgnoreCase) || resolved.Contains("cdzs", StringComparison.OrdinalIgnoreCase), $"{command} with {from} resolves without zstd: {resolved}.");
            }
        }

        AssertEqual("auto", TargetEmulatorPolicy.Normalize("something else"), "Unknown values fall back to Automatic.");
    }

    // The Options choice loads, saves, counts as a change, and has text in both languages.
    private static void TestTargetEmulatorOption()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var options = new OptionsViewModel();
                options.Load(new AppSettings { TargetEmulator = "nozstd" });
                AssertEqual("nozstd", options.SelectedTargetEmulator?.Key, "The saved target is loaded.");
                AssertEqual(3, options.TargetEmulatorOptions.Count, "Three targets are offered.");
                options.SelectedTargetEmulator = options.TargetEmulatorOptions.First(option => option.Key == "dvd2048");
                AssertEqual("dvd2048", options.BuildResultSettings(new AppSettings()).TargetEmulator, "The new target is saved.");
                MethodInfo equal = typeof(OptionsViewModel).GetMethod("CurrentValuesEqual", BindingFlags.Instance | BindingFlags.NonPublic)!;
                AssertFalse((bool)equal.Invoke(options, [new AppSettings { TargetEmulator = "nozstd" }])!, "A changed target counts as an unsaved change.");
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
            throw failure is TargetInvocationException { InnerException: { } inner } ? inner : failure;
        }

        Dictionary<string, string> english = LoadStrings("EnglishStrings.xaml");
        Dictionary<string, string> arabic = LoadStrings("ArabicStrings.xaml");
        foreach (string key in new[] { "LocPerformance_TargetEmulator", "LocAdv_TargetEmulatorAutoLabel", "LocAdv_TargetEmulatorAutoDescription", "LocAdv_TargetEmulatorDvd2048Label", "LocAdv_TargetEmulatorDvd2048Description", "LocAdv_TargetEmulatorNoZstdLabel", "LocAdv_TargetEmulatorNoZstdDescription" })
        {
            AssertTrue(english.ContainsKey(key) && arabic.ContainsKey(key), $"{key} exists in both languages.");
        }

        string view = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Options", "ProcView.xaml")));
        AssertTrue(view.Contains("AutomationProperties.Name=\"{DynamicResource LocPerformance_TargetEmulator}\"", StringComparison.Ordinal), "The target choice has an accessible name.");
    }
}
