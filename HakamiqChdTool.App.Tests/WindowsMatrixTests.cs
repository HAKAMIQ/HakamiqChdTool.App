using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Ui.Shell;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using HakamiqChdTool.App.ViewModels.Dialogs;
using HakamiqChdTool.App.ViewModels.Virtualization;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // Dialog matrix: every secondary window, both languages, both themes, several work areas that stand in for
    // 100/125/150/200 percent DPI on common displays (the logical work area is the pixel size divided by the scale).
    // Reported per case: clipped buttons/text, enabled controls that cannot take keyboard focus, input controls with
    // no accessible name, and windows larger than the logical work area.
    private static void RunWindowsMatrixLayout(string workDirectory)
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

                var factories = new (string Name, Func<Window> Create)[]
                {
                    ("About", () => new AboutWindow(new AboutWindowViewModel(AboutInfoFactory.Create(AppMetadata.CreateDefault()), new ExternalLinkService()))),
                    ("ClearLog", () => new ClearTaskLogConfirmationDialog("Title", "Body text for the confirmation dialog.", "Confirm", "Cancel")),
                    ("OrphanedCleanup", () => new OrphanedCleanupDialog(OrphanedWorkItemScanResult.Empty, "1.5 GB", "C:/Users/Someone/AppData/Local/Temp/HakamiqChdTool")),
                    ("RedumpNotice", () => new RedumpNoticeDialog("Notice", "A fairly long notice message that has to wrap inside the dialog without being clipped at any size.")),
                    ("Rename", () => new RenameConfirmationDialog(new RenameConfirmationViewModel("Some Game (Europe) (En,Fr,De,Es,It).chd"))),
                    ("Options", () => new OptionsWindow(AppSettings.CreateSafeDefaults())),
                    ("VerificationResult", () => new VerificationResultDialog(new QueueVerifyView(
                        "Verification result", "The file was verified.", "File", "Some Game (Europe) (En,Fr,De,Es,It).chd",
                        "Status", "Verified", "Details", "All hunks match. SHA-1 matches the CHD header.",
                        "Note", "This does not prove the image equals the original disc.", "Next step", "No action needed.",
                        new ChdProbeReportView("CHD report", [new ChdLogicalProbeReportMetric("Logical size", "700 MiB"), new ChdLogicalProbeReportMetric("Compression", "cdlz, cdzl, cdfl")])))),
                    ("RedumpDetails", () => new RedumpDetailsDialog(new RedumpDetailsDialogViewModel(
                        new TaskQueueItemViewModel(NamedQueueRow("Some Game (Europe) (En,Fr,De,Es,It).chd")), true, new NoCopy(), new NoTimer())))
                };

                (double Scale, double W, double H)[] displays =
                [
                    (1.0, 1920, 1040), (1.25, 1920, 1040), (1.5, 1920, 1040), (2.0, 1920, 1040),
                    (1.0, 1366, 728), (1.5, 1366, 728), (2.0, 1366, 728)
                ];

                foreach (string language in new[] { "ar-SA", "en-US" })
                {
                    foreach (string theme in new[] { "Light", "Dark" })
                    {
                        ApplyLayoutTestTheme(app, theme);
                        ApplyLayoutTestLanguage(app, language);

                        foreach ((string name, Func<Window> create) in factories)
                        {
                            Window window;
                            try
                            {
                                window = create();
                                if (window is OptionsWindow optionsWindow)
                                {
                                    optionsWindow.SelectTab(OptionsWindow.ExternalToolsTabKey);
                                }
                            }
                            catch (Exception ex)
                            {
                                findings.Add($"{name}/{language}/{theme}: construction failed: {ex.GetType().Name}: {ex.Message}");
                                continue;
                            }

                            window.ShowInTaskbar = false;
                            window.Left = -32000;
                            window.Top = -32000;
                            window.FlowDirection = language == "ar-SA" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                            window.Show();
                            PumpDispatcher(window.Dispatcher, TimeSpan.FromMilliseconds(80));

                            foreach ((double scale, double w, double h) in displays)
                            {
                                double logicalW = w / scale;
                                double logicalH = h / scale;
                                WindowBackdrop.ConstrainToWorkArea(window, new Rect(0, 0, logicalW, logicalH));
                                PumpDispatcher(window.Dispatcher, TimeSpan.FromMilliseconds(40));
                                window.UpdateLayout();

                                string context = $"{name}/{language}/{theme}/{scale * 100:F0}%@{w}x{h}";
                                CollectLayoutFindings(window, context, findings, notes);
                                CollectAccessibilityFindings(window, context, findings);

                                if (window.ActualWidth > logicalW + 1 || window.ActualHeight > logicalH + 1)
                                {
                                    findings.Add($"{context}: window {window.ActualWidth:F0}x{window.ActualHeight:F0} exceeds the {logicalW:F0}x{logicalH:F0} logical work area.");
                                }
                            }

                            window.Close();
                        }
                    }
                }

                app.Shutdown();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        AssertTrue(thread.Join(TimeSpan.FromSeconds(240)), "The window matrix finished.");
        AssertTrue(failure is null, "The window matrix threw: " + failure);

        Console.WriteLine($"[LAYOUT-NOTE] dialog matrix cases measured: {notes.Count}; sample: {notes.FirstOrDefault()} | {notes.LastOrDefault()}");
        foreach (string finding in findings.Distinct())
        {
            Console.WriteLine("[LAYOUT] " + finding);
        }

        AssertEqual(0, findings.Distinct().Count(), "The dialogs have no clipped, unreachable or unnamed controls.");
    }

    private static void CollectAccessibilityFindings(Window window, string context, List<string> findings)
    {
        foreach (FrameworkElement element in LayoutDescendants<FrameworkElement>(window))
        {
            if (!element.IsVisible || element.TemplatedParent is not null || element is not (Button or CheckBox or ComboBox or TextBox or RadioButton or Slider))
            {
                continue;
            }

            AutomationPeer? peer = UIElementAutomationPeer.CreatePeerForElement(element);
            string name = peer?.GetName() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) && peer?.GetLabeledBy() is null)
            {
                findings.Add($"{context}: {element.GetType().Name} '{LayoutName(element)}' has no accessible name.");
            }
        }
    }

    private sealed class NoCopy : IRedumpDetailsTextCopyService
    {
        public bool TrySetText(string text) => true;
    }

    private sealed class NoTimer : IRedumpDetailsFeedbackTimer
    {
        public void Restart(Action elapsed) { }

        public void Stop() { }

        public void Dispose() { }
    }
}
