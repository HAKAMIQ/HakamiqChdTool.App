using System.IO;
using System.Windows;
using System.Windows.Threading;
using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.QueueRun;
using HakamiqChdTool.App.Services.PostProcessing;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.Views;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // The quick window has its text in both languages, binds every button to a command with an accessible name, shows
    // the delete warning, and the app opens it only through QuickLaunch.OpensQuickWindow.
    private static void TestQuickWindowTextAndBindings()
    {
        string[] keys =
        [
            "LocQuick_TitleConvert", "LocQuick_TitleExtract", "LocQuick_TitleVerify",
            "LocQuick_DeleteWarningConvert", "LocQuick_DeleteWarningExtract", "LocQuick_WaitingForStart",
            "LocQuick_OpenInApp", "LocQuick_OpenInAppDesc", "LocQuick_Start", "LocQuick_Stop"
        ];
        Dictionary<string, string> arabic = LoadStrings("ArabicStrings.xaml");
        Dictionary<string, string> english = LoadStrings("EnglishStrings.xaml");
        foreach (string key in keys)
        {
            AssertTrue(arabic.TryGetValue(key, out string? ar) && !string.IsNullOrWhiteSpace(ar), $"{key} has Arabic text.");
            AssertTrue(english.TryGetValue(key, out string? en) && !string.IsNullOrWhiteSpace(en), $"{key} has English text.");
        }

        AssertTrue(arabic["LocQuick_DeleteWarningConvert"].Any(c => c is >= '؀' and <= 'ۿ'), "The Arabic warning is in Arabic.");

        string view = File.ReadAllText(FindRepoFile(Path.Combine("Views", "QuickRunWindow.xaml")));
        foreach ((string command, string name) in new[]
        {
            ("StartCommand", "LocQuick_Start"), ("CancelCommand", "LocQuick_Stop"),
            ("DetailsCommand", "LocRunSummary_Details"), ("OpenInAppCommand", "LocQuick_OpenInApp")
        })
        {
            AssertTrue(view.Contains($"Command=\"{{Binding {command}}}\"", StringComparison.Ordinal), $"A button runs {command}.");
            AssertTrue(view.Contains($"AutomationProperties.Name=\"{{DynamicResource {name}}}\"", StringComparison.Ordinal), $"The {command} button has an accessible name.");
        }

        AssertTrue(view.Contains("Visibility=\"{Binding ShowDeleteWarning", StringComparison.Ordinal), "The delete warning is shown when it applies.");
        AssertTrue(view.Contains("Text=\"{Binding FileName}\"", StringComparison.Ordinal) && view.Contains("FlowDirection=\"LeftToRight\"", StringComparison.Ordinal),
            "File names read left to right in Arabic.");

        string app = File.ReadAllText(FindRepoFile("App.xaml.cs"));
        AssertTrue(app.Contains("if (QuickLaunch.OpensQuickWindow(launch, Settings))", StringComparison.Ordinal), "Startup opens the quick window only through the setting rule.");
        AssertTrue(app.Contains("autoStart: !mayDeleteSource", StringComparison.Ordinal), "The quick window waits for Start when the source may be deleted.");
    }

    // The view model waits for Start with the delete warning, runs on Start, then offers the result and its report.
    private static void TestQuickWindowViewModelFlow(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "quick-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "Game.iso");
        File.WriteAllBytes(file, new byte[64 * 1024]);

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            _ = dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    var orchestrator = new QuickTestOrchestrator();
                    var queue = new QueueManager(orchestrator, AppSettings.CreateSafeDefaults, static () => string.Empty, 1);
                    await using var host = new QuickRunHost(queue, AppSettings.CreateSafeDefaults, new PostConversionArtifactService(), dispatcher, static () => null, autoStart: false);
                    var vm = new QuickRunViewModel(host, QueueExecutionProfile.QuickConvert, mayDeleteSource: true, key => "T:" + key);
                    string? report = null;
                    IReadOnlyList<string>? handed = null;
                    vm.DetailsRequested += (_, text) => report = text;
                    vm.OpenInAppRequested += paths => handed = paths;

                    AssertEqual("T:LocQuick_TitleConvert", vm.Title, "The title follows the operation.");
                    AssertTrue(vm.ShowDeleteWarning, "The delete warning is shown before Start.");
                    AssertEqual("T:LocQuick_DeleteWarningConvert", vm.DeleteWarningText, "The conversion warning is used.");

                    await host.AddAsync(QueueExecutionProfile.QuickConvert, [file]);
                    AssertTrue(vm.IsWaitingForStart && vm.StartCommand.CanExecute(null), "Start is offered while the file waits.");
                    AssertEqual("T:LocQuick_WaitingForStart", vm.StatusText, "The status says the files wait for Start.");
                    AssertFalse(vm.CancelCommand.CanExecute(null), "Nothing runs, so there is nothing to stop.");

                    Task run = vm.StartCommand.ExecuteAsync(null);
                    AssertTrue(await WaitOnDispatcherAsync(() => orchestrator.Started == 1, TimeSpan.FromSeconds(30)), "Start runs the file.");
                    AssertTrue(vm.CancelCommand.CanExecute(null) && !vm.OpenInAppCommand.CanExecute(null), "During the run, Stop is offered and Open in app is not.");
                    AssertFalse(vm.ShowDeleteWarning, "The warning goes away once the user started.");

                    orchestrator.ReleaseOne();
                    await run.WaitAsync(TimeSpan.FromSeconds(60));
                    AssertTrue(await WaitOnDispatcherAsync(() => vm.HasResult, TimeSpan.FromSeconds(30)), "The result is shown.");
                    AssertTrue(vm.ResultTitle.Length > 0, "The result has a title.");

                    vm.DetailsCommand.Execute(null);
                    AssertTrue(!string.IsNullOrWhiteSpace(report), "Details shows the report.");
                    vm.OpenInAppCommand.Execute(null);
                    AssertTrue(handed is { Count: 0 }, "A finished file is not handed to the main window.");
                    vm.Detach();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });

            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        AssertTrue(thread.Join(TimeSpan.FromSeconds(120)), "The quick window view model test finished.");
        if (failure is not null)
        {
            throw new InvalidOperationException("Quick window view model flow failed: " + failure, failure);
        }
    }

    // A quick window with two waiting files and the delete warning, for the window layout matrix.
    private static Window CreateQuickRunMatrixWindow(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "quick-matrix");
        Directory.CreateDirectory(root);
        string[] files =
        [
            Path.Combine(root, "Some Game (Europe) (En,Fr,De,Es,It).iso"),
            Path.Combine(root, "Another Game (USA).iso")
        ];
        foreach (string file in files.Where(path => !File.Exists(path)))
        {
            File.WriteAllBytes(file, new byte[64 * 1024]);
        }

        var window = new QuickRunWindow();
        var queue = new QueueManager(new QuickTestOrchestrator(), AppSettings.CreateSafeDefaults, static () => string.Empty, 1);
        var host = new QuickRunHost(queue, AppSettings.CreateSafeDefaults, new PostConversionArtifactService(), window.Dispatcher, () => window, autoStart: false);
        window.Attach(new QuickRunViewModel(host, QueueExecutionProfile.QuickConvert, mayDeleteSource: true, ArabicUi.Get));
        _ = host.AddAsync(QueueExecutionProfile.QuickConvert, files);
        PumpUntil(window.Dispatcher, () => host.Items.Count == files.Length && !host.IsBusy, TimeSpan.FromSeconds(20));
        return window;
    }
}
