using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Services.Xbox;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.Views;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private sealed class FakeXboxPicker : IXboxPicker
    {
        public List<string> Images { get; } = [];

        public string? Folder { get; set; }

        public IReadOnlyList<string> PickImages() => Images;

        public string? PickFolder(string titleKey) => Folder;
    }

    private static XboxSourceKind FakeXboxKind(string path) =>
        Directory.Exists(path)
            ? File.Exists(Path.Combine(path, "default.xex")) ? XboxSourceKind.Xbox360Folder
                : File.Exists(Path.Combine(path, "default.xbe")) ? XboxSourceKind.XboxFolder : XboxSourceKind.Unsupported
            : Path.GetFileName(path).Contains("360", StringComparison.Ordinal) ? XboxSourceKind.Xbox360Iso
            : Path.GetFileName(path).Contains("xbox", StringComparison.OrdinalIgnoreCase) ? XboxSourceKind.XboxIso
            : XboxSourceKind.Unsupported;

    // The Xbox window's view model: folders add the games inside them, unsupported items never run, Start needs an
    // output folder, each item reports its result and sizes, failed items run again, Stop cancels, and the Xbox 360
    // format choice is saved.
    private static void TestXboxViewModel(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "xbox-vm-" + Guid.NewGuid().ToString("N"));
        string library = Path.Combine(root, "library");
        Directory.CreateDirectory(Path.Combine(library, "Halo"));
        File.WriteAllBytes(Path.Combine(library, "Halo", "default.xbe"), [1]);
        Directory.CreateDirectory(Path.Combine(library, "Gears"));
        File.WriteAllBytes(Path.Combine(library, "Gears", "default.xex"), [1]);
        Directory.CreateDirectory(Path.Combine(library, "Notes"));
        File.WriteAllBytes(Path.Combine(library, "Some 360 Game.iso"), [1]);
        File.WriteAllBytes(Path.Combine(library, "readme.txt"), [1]);

        var saved = new List<Xbox360Format>();
        var calls = new List<(string Path, Xbox360Format Format)>();
        bool failFirstGears = true;
        var picker = new FakeXboxPicker { Folder = library };
        var vm = new XboxViewModel(
            (path, _) => Task.FromResult(FakeXboxKind(path)),
            (path, output, format, token) =>
            {
                token.ThrowIfCancellationRequested();
                calls.Add((path, format));
                if (path.EndsWith("Gears", StringComparison.Ordinal) && failFirstGears)
                {
                    failFirstGears = false;
                    return Task.FromResult(new XboxConvertResult(XboxConvertOutcome.Failed, XboxConvertService.ToolFailedKey));
                }

                return Task.FromResult(new XboxConvertResult(XboxConvertOutcome.Done, XboxConvertService.DoneKey, Path.Combine(output, "x"), 4_000_000, 1_000_000));
            },
            picker,
            key => "T:" + key,
            Xbox360Format.God,
            saved.Add);

        AssertEqual(2, vm.FormatOptions.Count, "GOD and ZAR are offered.");
        AssertEqual(Xbox360Format.God, vm.SelectedFormat.Format, "The saved format is selected.");
        vm.SelectedFormat = vm.FormatOptions[1];
        AssertTrue(saved.SequenceEqual([Xbox360Format.Zar]), "Choosing ZAR saves it.");

        vm.AddFolderCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        AssertEqual(3, vm.Items.Count, "A folder with games adds the two game folders and the ISO inside it, not other files.");
        AssertTrue(vm.Items.All(item => item.IsSupported), "Every added game is supported.");
        AssertTrue(vm.Items.Any(item => item.KindText == "T:LocXbox_KindXboxFolder") && vm.Items.Any(item => item.KindText == "T:LocXbox_Kind360Iso"), "Each item shows its kind.");

        vm.AddPathsAsync([Path.Combine(library, "readme.txt"), Path.Combine(library, "Halo")]).GetAwaiter().GetResult();
        AssertEqual(4, vm.Items.Count, "A duplicate is not added again; an unsupported file is listed.");
        XboxItem unsupported = vm.Items.Single(item => !item.IsSupported);
        AssertEqual("T:" + XboxConvertService.NotXboxKey, unsupported.StatusText, "The unsupported item says why.");

        AssertFalse(vm.StartCommand.CanExecute(null), "Start needs an output folder.");
        vm.OutputFolder = Path.Combine(root, "out");
        AssertTrue(vm.StartCommand.CanExecute(null), "Start is offered once the output folder is set.");

        vm.StartCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        AssertEqual(3, calls.Count, "Only the supported items run.");
        AssertTrue(calls.All(call => call.Format == Xbox360Format.Zar), "The chosen Xbox 360 format is passed to the converter.");
        AssertFalse(calls.Any(call => call.Path.EndsWith("readme.txt", StringComparison.Ordinal)), "The unsupported item never runs.");
        XboxItem halo = vm.Items.Single(item => item.Name == "Halo");
        AssertTrue(halo.StatusText.StartsWith("T:LocXbox_Done", StringComparison.Ordinal) && halo.StatusText.Contains("→", StringComparison.Ordinal), "A done item shows its sizes.");
        AssertEqual(XboxConvertOutcome.Failed, vm.Items.Single(item => item.Name == "Gears").Outcome, "The failed item is marked.");
        AssertTrue(vm.SummaryText.Length > 0, "A summary is shown.");

        calls.Clear();
        AssertTrue(vm.StartCommand.CanExecute(null), "Start is offered again while an item failed.");
        vm.StartCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        AssertEqual(1, calls.Count, "Only the failed item runs again.");
        AssertEqual(XboxConvertOutcome.Done, vm.Items.Single(item => item.Name == "Gears").Outcome, "The retried item is done.");
        AssertFalse(vm.StartCommand.CanExecute(null), "Nothing is left to run.");

        vm.ClearCommand.Execute(null);
        AssertEqual(0, vm.Items.Count, "Clear empties the list.");
    }

    // Every LocXbox key used by the window, the view model and the service has text in both languages; buttons and
    // inputs have accessible names; the sidebar opens the window.
    private static void TestXboxStringsAndWindow()
    {
        Dictionary<string, string> english = LoadStrings("EnglishStrings.xaml");
        Dictionary<string, string> arabic = LoadStrings("ArabicStrings.xaml");
        string window = File.ReadAllText(FindRepoFile(Path.Combine("Views", "XboxWindow.xaml")));
        string viewModel = File.ReadAllText(FindRepoFile(Path.Combine("ViewModels", "XboxVM.cs")));
        string service = File.ReadAllText(FindRepoFile(Path.Combine("Services", "Xbox", "XboxConvert.cs")));
        string sidebar = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Main", "ProcActions.xaml")));

        HashSet<string> keys = [.. Regex.Matches(window + viewModel + service + sidebar, "LocXbox_[A-Za-z0-9]+").Select(match => match.Value)];
        AssertTrue(keys.Count >= 30, "The Xbox text keys are found.");
        foreach (string key in keys)
        {
            AssertTrue(english.TryGetValue(key, out string? en) && en.Trim().Length > 0, $"{key} has English text.");
            AssertTrue(arabic.TryGetValue(key, out string? ar) && ar.Trim().Length > 0, $"{key} has Arabic text.");
        }

        foreach (string name in new[] { "LocXbox_AddFiles", "LocXbox_AddFolder", "LocXbox_Clear", "LocXbox_Format360", "LocXbox_OutputFolder", "LocXbox_Start", "LocXbox_Stop" })
        {
            AssertTrue(window.Contains($"AutomationProperties.Name=\"{{DynamicResource {name}}}\"", StringComparison.Ordinal), $"The window control for {name} has an accessible name.");
        }

        AssertTrue(sidebar.Contains("Command=\"{Binding OpenXboxCommand}\"", StringComparison.Ordinal), "The sidebar Xbox button opens the window.");
        AssertTrue(sidebar.Contains("AutomationProperties.Name=\"{DynamicResource LocXbox_Open}\"", StringComparison.Ordinal), "The sidebar Xbox button has an accessible name.");
        AssertTrue(sidebar.IndexOf("x:Name=\"ToolsSection\"", StringComparison.Ordinal) < sidebar.IndexOf("x:Name=\"XboxSection\"", StringComparison.Ordinal), "The Xbox section sits under Tools.");
        AssertTrue(window.Contains("Text=\"{Binding Name}\"", StringComparison.Ordinal) && window.Contains("FlowDirection=\"LeftToRight\"", StringComparison.Ordinal), "Game names read left to right in Arabic.");

        // WPF keeps a number next to a unit or a Latin word in order inside Arabic only with a mark around every word.
        AssertEqual("\u200E7.29\u200E \u200EGB\u200E \u200E→\u200E \u200E3.17\u200E \u200EGB\u200E", XboxViewModel.Ltr("7.29 GB → 3.17 GB"), "Each word of a size gets its own left-to-right mark.");
        foreach ((string key, string text) in arabic.Where(pair => (pair.Key.StartsWith("LocXbox_", StringComparison.Ordinal) || pair.Key == "LocIntake_XboxDisc") && pair.Value.Contains("360", StringComparison.Ordinal)))
        {
            AssertTrue(text.Contains("\u200EXbox\u200E \u200E360\u200E", StringComparison.Ordinal), $"{key} keeps Xbox 360 in order in Arabic.");
        }
    }

    // An Xbox window with games listed, for the window layout matrix.
    private static Window CreateXboxMatrixWindow()
    {
        var window = new XboxWindow();
        var vm = new XboxViewModel(
            (path, _) => Task.FromResult(FakeXboxKind(path)),
            (_, _, _, _) => Task.FromResult(new XboxConvertResult(XboxConvertOutcome.Done, XboxConvertService.DoneKey)),
            new FakeXboxPicker(),
            ArabicUi.Get,
            Xbox360Format.God,
            _ => { });
        vm.AddPathsAsync([@"C:\Games\Halo 2 (USA) (En,Ja,Fr,De,Es,It).xbox.iso", @"C:\Games\Some 360 Game (Europe).iso", @"C:\Games\notes.txt"]).GetAwaiter().GetResult();
        vm.OutputFolder = @"C:\Converted";
        window.Attach(vm);
        return window;
    }
}
