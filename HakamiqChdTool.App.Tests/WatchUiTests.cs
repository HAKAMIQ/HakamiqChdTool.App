using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.ViewModels;
using System.IO;
using System.Reflection;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // The watch folder setting loads, saves, counts as a change, is off by default and has its text and controls.
    private static void TestWatchFolderSettingInOptions()
    {
        AssertFalse(new AppSettings().WatchFolderEnabled, "The watch folder is off by default.");
        AppSettings copy = new AppSettings { WatchFolderEnabled = true, WatchFolderPath = @"D:\Incoming" }.Clone();
        AssertTrue(copy.WatchFolderEnabled && copy.WatchFolderPath == @"D:\Incoming", "Clone keeps the watch folder.");

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var options = new OptionsViewModel();
                options.Load(new AppSettings { WatchFolderEnabled = true, WatchFolderPath = @"D:\Incoming" });
                AssertTrue(options.WatchFolderEnabled && options.WatchFolderPath == @"D:\Incoming", "The saved folder is loaded.");
                options.WatchFolderPath = @"  E:\Drop  ";
                AppSettings saved = options.BuildResultSettings(new AppSettings());
                AssertEqual(@"E:\Drop", saved.WatchFolderPath, "The folder is saved trimmed.");
                MethodInfo equal = typeof(OptionsViewModel).GetMethod("CurrentValuesEqual", BindingFlags.Instance | BindingFlags.NonPublic)!;
                AssertFalse((bool)equal.Invoke(options, [new AppSettings { WatchFolderEnabled = true, WatchFolderPath = @"D:\Incoming" }])!, "A changed folder counts as an unsaved change.");
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
        foreach (string key in new[] { "LocWatch_Enable", "LocWatch_EnableDesc", "LocWatch_Folder", "LocWatch_PickFolder", "LocWatch_UnavailableFooter", "LocWatch_AddedFooter" })
        {
            AssertTrue(english.ContainsKey(key) && arabic.ContainsKey(key), $"{key} exists in both languages.");
        }

        string view = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Options", "WinView.xaml")));
        AssertTrue(view.Contains("IsChecked=\"{Binding WatchFolderEnabled", StringComparison.Ordinal), "The checkbox is bound.");
        AssertTrue(view.Contains("AutomationProperties.Name=\"{DynamicResource LocWatch_Folder}\"", StringComparison.Ordinal), "The folder box has an accessible name.");
        AssertTrue(view.Contains("Command=\"{Binding BrowseWatchFolderCommand}\"", StringComparison.Ordinal), "The folder can be browsed.");
    }
}
