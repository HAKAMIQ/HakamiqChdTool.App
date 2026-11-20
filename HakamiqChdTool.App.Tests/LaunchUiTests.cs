using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.ViewModels;
using System.IO;
using System.Reflection;
using System.Threading;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // The Explorer menu checkbox loads, saves and marks unsaved changes, is off by default, and has its text in both languages.
    private static void TestExplorerMenuSettingInOptions()
    {
        AssertFalse(new AppSettings().ExplorerMenuEnabled, "The Explorer menu is off until the user turns it on.");
        AssertTrue(new AppSettings { ExplorerMenuEnabled = true }.Clone().ExplorerMenuEnabled, "Clone keeps the setting.");
        var copy = new AppSettings();
        copy.CopyFrom(new AppSettings { ExplorerMenuEnabled = true });
        AssertTrue(copy.ExplorerMenuEnabled, "CopyFrom keeps the setting.");

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var options = new OptionsViewModel();
                options.Load(new AppSettings { ExplorerMenuEnabled = true });
                AssertTrue(options.ExplorerMenuEnabled, "The saved setting is loaded into Options.");

                options.ExplorerMenuEnabled = false;
                AssertFalse(options.BuildResultSettings(new AppSettings { ExplorerMenuEnabled = true }).ExplorerMenuEnabled, "Options saves the new value.");

                MethodInfo equal = typeof(OptionsViewModel).GetMethod("CurrentValuesEqual", BindingFlags.Instance | BindingFlags.NonPublic)!;
                AssertFalse((bool)equal.Invoke(options, [new AppSettings { ExplorerMenuEnabled = true }])!, "A changed value counts as an unsaved change.");
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
        foreach (string key in new[]
        {
            "LocOptions_WindowsSection", "LocOptions_ExplorerMenu", "LocOptions_ExplorerMenuDesc",
            "LocShell_ConvertToChd", "LocShell_ExtractChd", "LocShell_VerifyChd", "LocShell_FolderMenu",
            "LocLaunch_WaitingFooter", "LocLaunch_AddFailedFooter", "LocLaunch_ExplorerMenuFailedFooter"
        })
        {
            AssertTrue(english.TryGetValue(key, out string? en) && en.Trim().Length > 0, $"{key} has English text.");
            AssertTrue(arabic.TryGetValue(key, out string? ar) && ar.Trim().Length > 0, $"{key} has Arabic text.");
        }

        AssertTrue(arabic["LocShell_ConvertToChd"].Any(c => c is >= '؀' and <= 'ۿ'), "The Arabic menu entry is in Arabic.");

        string view = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Options", "PerfView.xaml")));
        AssertTrue(view.Contains("IsChecked=\"{Binding ExplorerMenuEnabled, Mode=TwoWay", StringComparison.Ordinal), "The Options checkbox is bound to the setting.");
        AssertTrue(view.Contains("AutomationProperties.Name=\"{DynamicResource LocOptions_ExplorerMenu}\"", StringComparison.Ordinal), "The checkbox has an accessible name.");
    }
}
