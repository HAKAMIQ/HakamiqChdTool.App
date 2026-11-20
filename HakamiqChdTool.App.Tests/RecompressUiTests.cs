using HakamiqChdTool.App.Services.Recompress;
using HakamiqChdTool.App.ViewModels;
using System.IO;
using System.Reflection;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private sealed class FakeRecompressPicker : IRecompressPicker
    {
        public IReadOnlyList<string> Files { get; set; } = [];

        public string? Folder { get; set; }

        public IReadOnlyList<string> PickChdFiles() => Files;

        public string? PickFolder(string titleKey) => Folder;
    }

    // The window's list accepts only existing CHD files once, Start needs a target, results and the summary show,
    // and Stop leaves the remaining files waiting.
    private static void TestRecompressViewModel()
    {
        Dictionary<string, string> english = LoadStrings("EnglishStrings.xaml");
        string root = Path.Combine(Path.GetTempPath(), "hct-recompress-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        try
        {
            string a = Path.Combine(root, "A.chd");
            string b = Path.Combine(root, "sub", "B.chd");
            string c = Path.Combine(root, "C.chd");
            File.WriteAllBytes(a, new byte[1000]);
            File.WriteAllBytes(b, new byte[2000]);
            File.WriteAllBytes(c, new byte[3000]);
            File.WriteAllBytes(Path.Combine(root, "D.iso"), new byte[10]);

            var calls = new List<string>();
            using var gate = new SemaphoreSlim(0);
            CancellationToken seenToken = default;
            Task<RecompressResult> Runner(string path, RecompressOptions options, IProgress<int> progress, CancellationToken token)
            {
                calls.Add(path);
                seenToken = token;
                progress.Report(50);
                RecompressResult result = Path.GetFileName(path) switch
                {
                    "A.chd" => new(RecompressOutcome.Saved, path, Path.Combine(options.OutputDirectory!, "A.chd"), 1000, 400, ChdRecompressService.SavedKey),
                    "B.chd" => new(RecompressOutcome.NoGain, path, null, 2000, 2000, ChdRecompressService.NoGainKey),
                    _ => new(RecompressOutcome.Failed, path, null, 3000, 0, ChdRecompressService.FailedKey)
                };
                return Task.FromResult(result);
            }

            var picker = new FakeRecompressPicker { Folder = root };
            var vm = new RecompressViewModel(Runner, picker, key => english[key], "preset:fast");
            AssertEqual("preset:fast", vm.SelectedPreset.Key, "The current compression setting is preselected.");
            AssertFalse(vm.StartCommand.CanExecute(null), "Start is off with an empty list.");

            vm.AddFolderCommand.Execute(null);
            AssertEqual(3, vm.Items.Count, "A folder adds its CHD files, including subfolders, and nothing else.");
            vm.AddPaths([a, Path.Combine(root, "D.iso"), Path.Combine(root, "missing.chd")]);
            AssertEqual(3, vm.Items.Count, "Duplicates, other types and missing files are not added.");

            AssertFalse(vm.StartCommand.CanExecute(null), "Start needs an output folder or Replace.");
            vm.OutputFolder = Path.Combine(root, "out");
            AssertTrue(vm.StartCommand.CanExecute(null), "Start is on with files and a folder.");
            vm.ReplaceOriginal = true;
            AssertFalse(vm.IsOutputFolderEnabled, "The folder box is off while replacing.");
            vm.ReplaceOriginal = false;

            vm.StartCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            AssertEqual(3, calls.Count, "Every file runs once.");
            RecompressItem itemA = vm.Items.Single(item => item.FileName == "A.chd");
            AssertEqual(RecompressOutcome.Saved, itemA.Outcome, "A's result is shown.");
            AssertEqual(english[ChdRecompressService.SavedKey], itemA.StatusText, "A's status text is the result text.");
            AssertTrue(itemA.SizeText.Contains('→'), "A shows the size before and after.");
            AssertEqual("1 of 3 files are smaller · 0.0 MB saved · 1 failed", vm.SummaryText.Replace("‎", string.Empty, StringComparison.Ordinal), "The summary counts smaller files, saved space and failures.");
            AssertFalse(vm.IsRunning, "The run has ended.");

            calls.Clear();
            vm.StartCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            AssertEqual(1, calls.Count, "Running again retries only the failed file.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Every text the recompress window and service use exists in both languages, and the window's controls have names.
    private static void TestRecompressStringsAndWindow()
    {
        Dictionary<string, string> english = LoadStrings("EnglishStrings.xaml");
        Dictionary<string, string> arabic = LoadStrings("ArabicStrings.xaml");
        IEnumerable<string> serviceKeys = typeof(ChdRecompressService)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);
        string window = File.ReadAllText(FindRepoFile(Path.Combine("Views", "RecompressWindow.xaml")));
        string model = File.ReadAllText(FindRepoFile(Path.Combine("ViewModels", "RecompressVM.cs")));
        IEnumerable<string> usedKeys = System.Text.RegularExpressions.Regex.Matches(window + model, "LocRecompress_[A-Za-z]+")
            .Select(match => match.Value);

        foreach (string key in serviceKeys.Concat(usedKeys).Append("LocRecompress_FileFilter").Append("LocRecompress_ToolsTitle").Distinct())
        {
            AssertTrue(english.ContainsKey(key), $"{key} has English text.");
            AssertTrue(arabic.ContainsKey(key), $"{key} has Arabic text.");
        }

        AssertTrue(english["LocRecompress_Summary"].Contains("{2}", StringComparison.Ordinal) && arabic["LocRecompress_Summary"].Contains("{2}", StringComparison.Ordinal), "The summary has all value slots in both languages.");
        AssertTrue(english["LocRecompress_FileFilter"].EndsWith("|*.chd", StringComparison.Ordinal) && arabic["LocRecompress_FileFilter"].EndsWith("|*.chd", StringComparison.Ordinal), "The file dialog filter lists CHD files.");
        foreach (string name in new[] { "LocRecompress_Start", "LocRecompress_Stop", "LocRecompress_Compression", "LocRecompress_Replace", "LocRecompress_OutputFolder" })
        {
            AssertTrue(window.Contains($"AutomationProperties.Name=\"{{DynamicResource {name}}}\"", StringComparison.Ordinal), $"The control for {name} has an accessible name.");
        }

        string actions = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Main", "ProcActions.xaml")));
        AssertTrue(actions.Contains("Command=\"{Binding OpenRecompressCommand}\"", StringComparison.Ordinal), "The sidebar opens the recompress window.");
    }
}
