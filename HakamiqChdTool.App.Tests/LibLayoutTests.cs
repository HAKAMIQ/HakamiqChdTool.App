using HakamiqChdTool.App.Ui.Shell;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // WPF allows one Application per process and ties it to the thread that created it. A failed or
    // hung layout run would leave that Application behind and break every later test in this process,
    // so the layout run happens in a child process that is discarded afterwards.
    private static void TestLibraryWindowRealLayout(string workDirectory) => RunIsolatedUiChild("library-layout", workDirectory);

    private static void TestWindowsMatrixLayout(string workDirectory) => RunIsolatedUiChild("windows-matrix", workDirectory);

    private static void RunIsolatedUiChild(string uiTestName, string workDirectory)
    {
        string appAssembly = Environment.GetCommandLineArgs()
            .SkipWhile(argument => !string.Equals(argument, "--app-assembly", StringComparison.Ordinal))
            .Skip(1)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("The app assembly argument is required for isolated UI tests.");
        string testAssembly = System.Reflection.Assembly.GetEntryAssembly()?.Location
            ?? throw new InvalidOperationException("The test assembly location is unavailable.");

        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (!string.Equals(Path.GetFileNameWithoutExtension(start.FileName), "HakamiqChdTool.App.Tests", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(testAssembly);
        }

        foreach (string argument in new[] { "--app-assembly", appAssembly, "--ui-test", uiTestName, "--work-dir", workDirectory })
        {
            start.ArgumentList.Add(argument);
        }

        using System.Diagnostics.Process child = System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException("The isolated UI test process could not be started.");
        System.Threading.Tasks.Task<string> stdout = child.StandardOutput.ReadToEndAsync();
        System.Threading.Tasks.Task<string> stderr = child.StandardError.ReadToEndAsync();

        if (!child.WaitForExit(TimeSpan.FromSeconds(300)))
        {
            child.Kill(entireProcessTree: true);
            throw new InvalidOperationException("The isolated layout test did not finish within 300 seconds.");
        }

        string output = stdout.GetAwaiter().GetResult();
        string error = stderr.GetAwaiter().GetResult();

        foreach (string line in output.Split('\n').Where(l => l.StartsWith("[LAYOUT", StringComparison.Ordinal)))
        {
            Console.WriteLine(line.TrimEnd('\r'));
        }

        AssertEqual(0, child.ExitCode, "The isolated layout test failed. " + error.Trim());
    }

    // Child-process entry for the isolated run.
    private static int RunIsolatedUiTest(string name, string workDirectory)
    {
        try
        {
            switch (name)
            {
                case "library-layout":
                    RunLibraryWindowRealLayout(workDirectory);
                    return 0;
                case "mainwindow-matrix":
                    RunMainWindowMatrixLayout(workDirectory);
                    return 0;
                case "windows-matrix":
                    RunWindowsMatrixLayout(workDirectory);
                    return 0;
                default:
                    Console.Error.WriteLine("Unknown isolated UI test: " + name);
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    // Real WPF layout of the Library window: both languages, both themes, several work areas.
    // A work area smaller than the content must produce a scroller, never horizontally clipped controls.
    private static void RunLibraryWindowRealLayout(string workDirectory)
    {
        List<string> findings = [];
        List<string> notes = [];
        Exception? failure = null;

        Thread thread = new(() =>
        {
            try
            {
                var app = new global::HakamiqChdTool.App.App
                {
                    IsIntegrationTestHost = true,
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
                app.InitializeComponent();

                string root = AnalysisLibrary(workDirectory, "layout-lib", ("a.bin", [1, 2]), ("b.iso", [3, 4, 5]), ("c.chd", [6]));
                string dat = WriteDat(Path.Combine(workDirectory, "layout-dats"), "l.dat", "Sony - PlayStation",
                    new DatGameSpec("A", "USA", new DatRomSpec("a.bin", 2)));

                foreach (string language in new[] { "ar-SA", "en-US" })
                {
                    foreach (string theme in new[] { "Light", "Dark" })
                    {
                        ApplyLayoutTestTheme(app, theme);
                        ApplyLayoutTestLanguage(app, language);
                        Dictionary<string, string> strings = LoadStrings(language == "ar-SA" ? "ArabicStrings.xaml" : "EnglishStrings.xaml");
                        var vm = new LibraryAnalysisViewModel(RealRunner(), new FakePicker(), key => strings.TryGetValue(key, out string? v) ? v : "MISSING:" + key, action => action());
                        vm.LibraryRoot = root;
                        vm.OutputRoot = Path.Combine(workDirectory, "layout-out");
                        vm.AddDatPath(dat);

                        var window = new LibraryAnalysisWindow(vm)
                        {
                            ShowInTaskbar = false,
                            Left = -32000,
                            Top = -32000
                        };
                        window.FlowDirection = language == "ar-SA" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                        window.Show();
                        PumpDispatcher(window.Dispatcher, TimeSpan.FromMilliseconds(100));

                        _ = window.Dispatcher.InvokeAsync(async () => await vm.StartCommand.ExecuteAsync(null));
                        PumpUntil(window.Dispatcher, () => vm.HasOutcome && !vm.IsRunning, TimeSpan.FromSeconds(20));

                        foreach ((double w, double h) in new[] { (800d, 600d), (900d, 700d), (1040d, 780d), (1600d, 900d) })
                        {
                            WindowBackdrop.ConstrainToWorkArea(window, new Rect(0, 0, w, h));
                            window.Width = Math.Min(Math.Max(window.MinWidth, 1040), w);
                            window.Height = Math.Min(Math.Max(window.MinHeight, 780), h);
                            PumpDispatcher(window.Dispatcher, TimeSpan.FromMilliseconds(50));
                            window.UpdateLayout();
                            CollectLayoutFindings(window, $"{language}/{theme}/{w}x{h}", findings, notes);
                        }

                        window.Close();
                    }
                }

                app.Shutdown();
                PumpUntil(app.Dispatcher, () => Application.Current is null, TimeSpan.FromSeconds(5));
                notes.Add("Application.Current after shutdown: " + (Application.Current is null ? "null" : "still set"));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        AssertTrue(thread.Join(TimeSpan.FromSeconds(120)), "The layout test finished.");
        AssertTrue(failure is null, "The layout test threw: " + failure);

        foreach (string note in notes.Distinct())
        {
            Console.WriteLine("[LAYOUT-NOTE] " + note);
        }

        foreach (string finding in findings.Distinct())
        {
            Console.WriteLine("[LAYOUT] " + finding);
        }

        AssertEqual(0, findings.Distinct().Count(), "The Library window has no clipped or unreachable controls.");
    }

    // The test host's pack URIs resolve against the test executable, so the swaps address the app assembly explicitly.
    private static void SwapMergedDictionary(Application app, string sourceSuffix, string replacementFile)
    {
        var merged = app.Resources.MergedDictionaries;
        for (int i = 0; i < merged.Count; i++)
        {
            if (merged[i].Source is { } source && source.OriginalString.EndsWith(sourceSuffix, StringComparison.OrdinalIgnoreCase))
            {
                merged[i] = new ResourceDictionary { Source = new Uri("pack://application:,,,/HakamiqChdTool;component/Resources/" + replacementFile, UriKind.Absolute) };
                return;
            }
        }
    }

    private static void ApplyLayoutTestTheme(Application app, string theme)
    {
        SwapMergedDictionary(app, "Themes/Light.xaml", "Themes/" + theme + ".xaml");
        SwapMergedDictionary(app, "Themes/Dark.xaml", "Themes/" + theme + ".xaml");
    }

    private static void ApplyLayoutTestLanguage(Application app, string language)
    {
        string file = language == "ar-SA" ? "ArabicStrings.xaml" : "EnglishStrings.xaml";
        SwapMergedDictionary(app, "ArabicStrings.xaml", file);
        SwapMergedDictionary(app, "EnglishStrings.xaml", file);
        FlowDirection flow = language == "ar-SA" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        app.Resources["App.FlowDirection"] = flow;
        app.Resources["App.CaptionButtonsFlowDirection"] = flow;
        app.Resources["App.XmlLanguage"] = System.Windows.Markup.XmlLanguage.GetLanguage(language);
    }

    private static void PumpDispatcher(Dispatcher dispatcher, TimeSpan duration)
    {
        DateTime end = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < end)
        {
            dispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
            Thread.Sleep(5);
        }
    }

    private static void PumpUntil(Dispatcher dispatcher, Func<bool> done, TimeSpan timeout)
    {
        DateTime end = DateTime.UtcNow + timeout;
        while (!done() && DateTime.UtcNow < end)
        {
            dispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(() => { }));
            Thread.Sleep(10);
        }
    }

    private static void CollectLayoutFindings(Window window, string context, List<string> findings, List<string> notes)
    {
        notes.Add($"{context}: window {window.ActualWidth:F0}x{window.ActualHeight:F0}, min {window.MinWidth:F0}x{window.MinHeight:F0}, flow {window.FlowDirection}");

        foreach (ScrollViewer scroller in LayoutDescendants<ScrollViewer>(window))
        {
            if (scroller.ExtentWidth > scroller.ViewportWidth + 1 && scroller.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled)
            {
                findings.Add($"{context}: scroller '{LayoutName(scroller)}' content is {scroller.ExtentWidth:F0} wide in a {scroller.ViewportWidth:F0} viewport with horizontal scrolling disabled (clipped).");
            }
        }

        foreach (FrameworkElement element in LayoutDescendants<FrameworkElement>(window))
        {
            // Control-template internals (scroll bar parts, expander toggles) are sized by their templates, not by this window's layout.
            if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0 || element.TemplatedParent is not null)
            {
                continue;
            }

            if (element is Button or CheckBox or ComboBox or TextBox or ToggleButton)
            {
                if (element.IsEnabled && (!element.Focusable || element is Control { IsTabStop: false }))
                {
                    findings.Add($"{context}: {element.GetType().Name} '{LayoutName(element)}' ({DescribeControl(element)}) is enabled but cannot take keyboard focus.");
                }

                // DesiredSize includes the element's margin; ActualWidth/ActualHeight do not.
                double wantedWidth = element.DesiredSize.Width - element.Margin.Left - element.Margin.Right;
                double wantedHeight = element.DesiredSize.Height - element.Margin.Top - element.Margin.Bottom;

                if (wantedWidth > element.ActualWidth + 1.5 || wantedHeight > element.ActualHeight + 1.5)
                {
                    findings.Add($"{context}: {element.GetType().Name} '{LayoutName(element)}' ({DescribeControl(element)}) wanted {wantedWidth:F0}x{wantedHeight:F0} but got {element.ActualWidth:F0}x{element.ActualHeight:F0}.");
                }

                if (element.ActualHeight < 20)
                {
                    findings.Add($"{context}: {element.GetType().Name} '{LayoutName(element)}' is only {element.ActualHeight:F0} tall.");
                }
            }

            if (element is TextBlock text && text.TextTrimming == TextTrimming.None && text.TextWrapping == TextWrapping.NoWrap
                && text.DesiredSize.Width - text.Margin.Left - text.Margin.Right > text.ActualWidth + 1.5)
            {
                findings.Add($"{context}: non-wrapping TextBlock '{LayoutName(text)}' is clipped ({text.DesiredSize.Width:F0} > {text.ActualWidth:F0}).");
            }
        }
    }

    private static string DescribeControl(FrameworkElement element)
    {
        string content = element is ContentControl { Content: { } c } ? c.ToString() ?? string.Empty : string.Empty;
        string parent = element.Parent is FrameworkElement p ? p.GetType().Name + ":" + p.Name : "?";
        return $"content='{content}', parent={parent}, height={element.Height}, minHeight={element.MinHeight}, margin={element.Margin}";
    }

    private static string LayoutName(FrameworkElement element) =>
        string.IsNullOrEmpty(element.Name) ? (element.Tag?.ToString() ?? element.GetType().Name) : element.Name;

    private static IEnumerable<T> LayoutDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T nested in LayoutDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}
