using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Configuration;
using HakamiqChdTool.App.Startup;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using HakamiqChdTool.App.Views.Main;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static void TestMainWindowLayoutAndVirtualization(string workDirectory) => RunIsolatedUiChild("mainwindow-matrix", workDirectory);

    // The real MainWindow, in a child process, against a temporary settings file. The Loaded handler (startup scan,
    // runtime probing, orphan cleanup) is unhooked so the run touches nothing outside the temp directory.
    // 5,000 queue rows are added through the row store to check that the DataGrid realizes only what is visible.
    private static void RunMainWindowMatrixLayout(string workDirectory)
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

                string settingsPath = Path.Combine(workDirectory, "mainwindow-settings", "settings.json");
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
                AppSettings settings = AppSettings.CreateSafeDefaults();
                MainWindowBootstrap bootstrap = MainWindowBootstrap.CreateDefault(
                    new AppSettingsService(settingsPath), settings, AppMetadata.CreateDefault(), RuntimeToolService.Instance);
                var queueManager = new QueueManager(bootstrap.WorkflowOrchestrator, () => settings, () => string.Empty, maxConcurrentItems: 1);

                (double Scale, double W, double H)[] displays =
                [
                    (1.0, 1920, 1040), (1.5, 1920, 1040), (2.0, 1920, 1040), (1.0, 1366, 728), (2.0, 1366, 728)
                ];

                foreach (string language in new[] { "ar-SA", "en-US" })
                {
                    foreach (string theme in new[] { "Light", "Dark" })
                    {
                        ApplyLayoutTestTheme(app, theme);
                        ApplyLayoutTestLanguage(app, language);

                        var window = new MainWindow(bootstrap, queueManager);
                        RoutedEventHandler loaded = (RoutedEventHandler)Delegate.CreateDelegate(
                            typeof(RoutedEventHandler), window,
                            typeof(MainWindow).GetMethod("MainWindow_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!);
                        window.Loaded -= loaded;

                        window.ShowInTaskbar = false;
                        window.WindowStartupLocation = WindowStartupLocation.Manual;
                        window.Left = -32000;
                        window.Top = -32000;
                        window.FlowDirection = language == "ar-SA" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                        window.Show();
                        PumpDispatcher(window.Dispatcher, TimeSpan.FromMilliseconds(200));

                        var store = (QueueRowStore)typeof(MainWindow)
                            .GetField("_queueRowStore", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                        store.AppendRange(Enumerable.Range(0, 5000)
                            .Select(i => new QueueRowData
                            {
                                ItemId = Guid.NewGuid(),
                                OriginalPath = "C:/games/Some Game " + i + " (Europe) (En,Fr,De,Es,It).iso",
                                FileName = "Some Game " + i + " (Europe) (En,Fr,De,Es,It).iso"
                            }).ToArray());
                        PumpDispatcher(window.Dispatcher, TimeSpan.FromMilliseconds(400));

                        FooterStatusStripView? footer = LayoutDescendants<FooterStatusStripView>(window).FirstOrDefault();
                        if (footer is null)
                        {
                            findings.Add($"MainWindow/{language}/{theme}: the footer status strip was not found.");
                        }
                        else
                        {
                            footer.SetQueuedReady(0, 5000);
                            string expectedState = language == "ar-SA" ? "جاهز" : "Ready";
                            if (!string.Equals(footer.StateTextBlock.Text, expectedState, StringComparison.Ordinal))
                            {
                                findings.Add($"MainWindow/{language}/{theme}: expected one localized ready state, got '{footer.StateTextBlock.Text}'.");
                            }

                            string visibleCount = footer.QueueSummaryTextBlock.Text
                                .Replace("\u2066", string.Empty, StringComparison.Ordinal)
                                .Replace("\u2069", string.Empty, StringComparison.Ordinal);
                            if (!string.Equals(visibleCount, "0 / 5000", StringComparison.Ordinal))
                            {
                                findings.Add($"MainWindow/{language}/{theme}: expected independent queue count '0 / 5000', got '{visibleCount}'.");
                            }
                        }

                        foreach ((double scale, double w, double h) in displays)
                        {
                            double logicalW = w / scale;
                            double logicalH = h / scale;
                            WindowBackdrop_Constrain(window, new Rect(0, 0, logicalW, logicalH));
                            PumpDispatcher(window.Dispatcher, TimeSpan.FromMilliseconds(120));
                            window.UpdateLayout();

                            string context = $"MainWindow/{language}/{theme}/{scale * 100:F0}%@{w}x{h}";
                            CollectLayoutFindings(window, context, findings, notes);
                            CollectAccessibilityFindings(window, context, findings);

                            if (window.ActualWidth > logicalW + 1 || window.ActualHeight > logicalH + 1)
                            {
                                findings.Add($"{context}: window {window.ActualWidth:F0}x{window.ActualHeight:F0} exceeds the {logicalW:F0}x{logicalH:F0} logical work area.");
                            }
                        }

                        DataGrid? grid = LayoutDescendants<DataGrid>(window).FirstOrDefault(g => g.Name == "TasksDataGrid");
                        if (grid is null)
                        {
                            findings.Add($"MainWindow/{language}/{theme}: the queue DataGrid was not found.");
                        }
                        else
                        {
                            int realized = LayoutDescendants<DataGridRow>(grid).Count();
                            notes.Add($"MainWindow/{language}/{theme}: {grid.Items.Count} items, {realized} realized rows");
                            if (grid.Items.Count != 5000)
                            {
                                findings.Add($"MainWindow/{language}/{theme}: expected 5000 items, grid has {grid.Items.Count}.");
                            }

                            if (realized > 200)
                            {
                                findings.Add($"MainWindow/{language}/{theme}: {realized} rows are realized for 5000 items (virtualization is not effective).");
                            }

                            grid.ScrollIntoView(grid.Items[grid.Items.Count - 1]);
                            PumpDispatcher(window.Dispatcher, TimeSpan.FromMilliseconds(200));
                            int realizedAtEnd = LayoutDescendants<DataGridRow>(grid).Count();
                            if (realizedAtEnd > 200)
                            {
                                findings.Add($"MainWindow/{language}/{theme}: {realizedAtEnd} rows are realized after scrolling to the end.");
                            }

                            // Active selection through the real bindings: a single-item Add, a Reset (large batch), a
                            // sort and a filter must not drop or change the selected row while it stays visible.
                            object selectedRow = grid.Items[100];
                            grid.SelectedItem = selectedRow;
                            PumpDispatcher(window.Dispatcher, TimeSpan.FromMilliseconds(50));
                            QueueRowData NewRow(int i) => new()
                            {
                                ItemId = Guid.NewGuid(),
                                OriginalPath = "C:/games/Extra " + i + ".iso",
                                FileName = "Extra " + i + ".iso"
                            };

                            store.AppendRange([NewRow(0)]);
                            PumpDispatcher(window.Dispatcher, TimeSpan.FromMilliseconds(100));
                            if (!ReferenceEquals(grid.SelectedItem, selectedRow))
                            {
                                findings.Add($"MainWindow/{language}/{theme}: a single-item Add changed the selected row.");
                            }

                            store.AppendRange(Enumerable.Range(1, 200).Select(NewRow).ToArray());
                            PumpDispatcher(window.Dispatcher, TimeSpan.FromMilliseconds(200));
                            if (!ReferenceEquals(grid.SelectedItem, selectedRow))
                            {
                                notes.Add($"MainWindow/{language}/{theme}: selection after a Reset batch: {(grid.SelectedItem is null ? "cleared" : "changed")}.");
                                findings.Add($"MainWindow/{language}/{theme}: a Reset batch changed the selected row.");
                            }

                            grid.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription("FileName", System.ComponentModel.ListSortDirection.Descending));
                            PumpDispatcher(window.Dispatcher, TimeSpan.FromMilliseconds(200));
                            if (!ReferenceEquals(grid.SelectedItem, selectedRow))
                            {
                                findings.Add($"MainWindow/{language}/{theme}: sorting changed the selected row.");
                            }

                            grid.Items.SortDescriptions.Clear();
                            grid.Items.Filter = o => o is TaskQueueItemViewModel t && t.FileName.StartsWith("Some Game", StringComparison.Ordinal);
                            PumpDispatcher(window.Dispatcher, TimeSpan.FromMilliseconds(200));
                            if (!ReferenceEquals(grid.SelectedItem, selectedRow))
                            {
                                findings.Add($"MainWindow/{language}/{theme}: a filter that keeps the row changed the selected row.");
                            }

                            grid.Items.Filter = null;
                            PumpDispatcher(window.Dispatcher, TimeSpan.FromMilliseconds(100));
                        }

                        store.Clear();
                        PumpDispatcher(window.Dispatcher, TimeSpan.FromMilliseconds(100));
                        window.Close();
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
        AssertTrue(thread.Join(TimeSpan.FromSeconds(280)), "The main window matrix finished.");
        AssertTrue(failure is null, "The main window matrix threw: " + failure);

        foreach (string note in notes.Where(n => n.Contains("realized")).Distinct())
        {
            Console.WriteLine("[LAYOUT-NOTE] " + note);
        }

        foreach (string finding in findings.Distinct())
        {
            Console.WriteLine("[LAYOUT] " + finding);
        }

        AssertEqual(0, findings.Distinct().Count(), "The main window has no clipped, unreachable or unnamed controls and virtualizes the queue.");
    }

    private static void WindowBackdrop_Constrain(Window window, Rect workArea) =>
        HakamiqChdTool.App.Ui.Shell.WindowBackdrop.ConstrainToWorkArea(window, workArea);
}
