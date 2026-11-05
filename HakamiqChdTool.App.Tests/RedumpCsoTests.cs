using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Core.Verification;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Configuration;
using HakamiqChdTool.App.Startup;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using HakamiqChdTool.App.Views;
using HakamiqChdTool.App.Views.Options;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static async Task TestRedumpCsoV2(AppReflection app, string workDirectory)
    {
        string root = Path.Combine(workDirectory, "redump-cso-v2");
        Directory.CreateDirectory(root);
        string iso = Path.Combine(root, "source.iso");
        string cso = Path.Combine(root, "source.cso");
        byte[] bytes = Enumerable.Range(0, 65536).Select(i => (byte)(i * 17 % 251)).ToArray();
        await File.WriteAllBytesAsync(iso, bytes);
        object compressed = app.RunBundledCsoKit(["compress", iso, "-o", cso, "--profile", "game-safe", "--threads", "1", "--block", "2048", "--json"]);
        AssertEqual(0, GetInt(compressed, "ExitCode"), "Synthetic CSO creation: " + GetString(compressed, "StandardError"));
        string sourceHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(cso)));
        string dat = Path.Combine(root, "psp.dat");
        await File.WriteAllTextAsync(dat,
            $"<datafile><header><name>Sony - PlayStation Portable - Datfile</name></header><game name=\"Synthetic PSP\"><rom name=\"source.iso\" size=\"{bytes.Length}\" crc=\"{Crc32Hex(bytes)}\" md5=\"{Convert.ToHexString(MD5.HashData(bytes))}\" sha1=\"{Convert.ToHexString(SHA1.HashData(bytes))}\" /></game></datafile>", Encoding.UTF8);
        var database = (RedumpSqliteManager)app.CreateRedumpDatabase(Path.Combine(root, "catalog.db"));
        AssertTrue(GetBool(app.CleanRebuildRedumpDatabase(database, [dat]), "Success"), "Synthetic DAT import.");
        string chdman = app.GetRuntimeChdmanPath(app.CreateRuntimeToolService());
        var events = new ConcurrentQueue<ProgressEvent>();
        DeepHashAnalysisResult result = await ScanWithOuterLeaseAsync(cso, database, chdman, events);
        AssertEqual(IntegrityValidationState.Verified, result.State, DescribeRedumpResult(result));
        AssertEqual(RedumpVerification.Full, result.RedumpVerification, "CSO requires full proof against decompressed content.");
        AssertEqual("LocDeepHash_StatusVerifiedNormalized", result.StatusMessageKey, "V2 normalized status.");
        DeepHashFileDigest digest = result.HashedFiles.Single();
        AssertEqual((long)bytes.Length, digest.SizeBytes, "Hash the ISO size, not the compressed file size.");
        AssertFalse(File.Exists(digest.Path), "V2 must delete its normalized ISO before returning.");
        AssertFalse(Directory.Exists(Path.GetDirectoryName(digest.Path)), "V2 must delete its CSO workspace.");
        foreach (string stage in new[] { "LocRedumpV2_NormalizeCsoIso", "LocDeepHash_CsoStageInfo", "LocDeepHash_CsoStageVerify", "LocDeepHash_CsoStageDecompress", "LocRedumpV2_StepHash", "LocRedumpV2_StepMatch", "LocRedumpV2_StepCleanup" })
            AssertTrue(events.Any(e => e.MessageKey == stage), "Missing CSO V2 stage: " + stage);

        // A different payload must not inherit the first CSO's verification.
        bytes[^1] ^= 0x5A;
        await File.WriteAllBytesAsync(iso, bytes);
        string different = Path.Combine(root, "different.cso");
        compressed = app.RunBundledCsoKit(["compress", iso, "-o", different, "--profile", "game-safe", "--threads", "1", "--block", "2048", "--json"]);
        AssertEqual(0, GetInt(compressed, "ExitCode"), "Mismatch CSO creation.");
        DeepHashAnalysisResult mismatch = await ScanWithOuterLeaseAsync(different, database, chdman, new());
        AssertEqual(IntegrityValidationState.NoRedumpMatch, mismatch.State, "Mismatching normalized content must not verify.");
        AssertTrue(mismatch.HashedFiles.All(f => !File.Exists(f.Path)), "Mismatch workspace cleanup.");

        string corrupt = Path.Combine(root, "corrupt.cso");
        await File.WriteAllBytesAsync(corrupt, [.. await File.ReadAllBytesAsync(cso), 0x5A]);
        DeepHashAnalysisResult failed = await ScanWithOuterLeaseAsync(corrupt, database, chdman, new());
        AssertEqual(IntegrityValidationState.Failed, failed.State, "CsoKit verification failure must remain a failure.");
        AssertEqual(0, failed.HashedFileCount, "Do not hash a failed preparation.");

        using var cancelled = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        bool sawDecompress = false;
        // The existing preprocessor returns a cancelled preparation as a failed V2
        // result; MW.Integrity checks the caller's token before applying that result.
        DeepHashAnalysisResult cancelledResult = await DeepHashAnalyzer.DeepHashAnalyzeAsync(cso, database, cancelled.Token,
            new RedumpV2ScanOptions(chdman), new InlineProgress<ProgressEvent>(e =>
            {
                if (e.MessageKey == "LocDeepHash_CsoStageDecompress")
                {
                    sawDecompress = true;
                    cancelled.Cancel();
                }
            }));
        AssertTrue(sawDecompress, "Cancellation must happen inside preparation.");
        AssertTrue(cancelled.IsCancellationRequested, "The caller retains cancellation state.");
        AssertEqual(IntegrityValidationState.Failed, cancelledResult.State, "Cancelled preparation cannot verify.");
        AssertEqual(0, cancelledResult.HashedFileCount, "Cancelled preparation must not hash or match.");
        AssertEqual(sourceHash, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(cso))), "All scans preserve the CSO source.");
        AssertFalse(Directory.EnumerateFiles(AppPaths.CombineProcessTemp("RedumpV2"), "*.iso", SearchOption.AllDirectories).Any(), "No temporary ISO survives success, mismatch, failure or cancellation.");
    }

    private static void RunRedumpCsoConsentUi(string workDirectory)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new global::HakamiqChdTool.App.App { IsIntegrationTestHost = true, ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.InitializeComponent();
                try
                {
                    AppSettings settings = AppSettings.CreateSafeDefaults();
                    settings.EnableDeepIntegrityCheck = true;
                    var bootstrap = MainWindowBootstrap.CreateDefault(new AppSettingsService(Path.Combine(workDirectory, "consent-settings.json")), settings, AppMetadata.CreateDefault(), RuntimeToolService.Instance);
                    var queue = new QueueManager(bootstrap.WorkflowOrchestrator, () => settings, () => string.Empty, maxConcurrentItems: 1);
                    var window = new MainWindow(bootstrap, queue);
                    window.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), window, typeof(MainWindow).GetMethod("MainWindow_Loaded", BindingFlags.NonPublic | BindingFlags.Instance)!);
                    window.ShowInTaskbar = false;
                    window.Left = -32000;
                    window.Top = -32000;
                    window.Show();
                    try
                    {
                        foreach (string language in new[] { "ar-SA", "en-US" })
                        {
                            ApplyLayoutTestLanguage(app, language);
                            var view = new RedumpSettingsView();
                            var notice = view.FindName("RedumpComplianceNoticeTextBlock") as TextBlock;
                            AssertTrue(notice is not null, "Redump settings must show the compliance notice.");
                            AssertEqual(ArabicUi.Get("LocRedumpSettings_ComplianceNotice"), notice!.Text, "Notice uses the existing localized text.");
                            AssertEqual(Visibility.Visible, notice.Visibility, "Notice remains visible when Redump is disabled.");
                            AssertEqual(TextWrapping.Wrap, notice.TextWrapping, "Notice wraps in both languages.");

                            MethodInfo confirm = typeof(MainWindow).GetMethod("ConfirmCsoRedumpTempIsoAsync", BindingFlags.NonPublic | BindingFlags.Instance)
                                ?? throw new InvalidOperationException("Missing CSO confirmation.");
                            foreach (bool? answer in new bool?[] { false, true, null })
                            {
                                bool handled = false;
                                Exception? dialogFailure = null;
                                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                                timer.Tick += (_, _) =>
                                {
                                    var dialog = app.Windows.OfType<RedumpNoticeDialog>().FirstOrDefault();
                                    if (dialog is null) return;
                                    timer.Stop();
                                    try
                                    {
                                        AssertEqual(window, dialog.Owner!, "Confirmation owner.");
                                        var button = (Button)dialog.FindName("ConfirmButton");
                                        AssertEqual(Visibility.Visible, button.Visibility, "Confirmation requires an explicit choice.");
                                        AssertEqual(ArabicUi.Get("LocRedump_CsoTempIsoConfirm"), (string)button.Content, "Localized confirmation button.");
                                        handled = true;
                                        if (answer is null) dialog.Close();
                                        else ((Button)dialog.FindName(answer.Value ? "ConfirmButton" : "CloseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                    }
                                    catch (Exception ex) { dialogFailure = ex; dialog.Close(); }
                                };
                                timer.Start();
                                bool accepted;
                                try { accepted = ((Task<bool>)confirm.Invoke(window, ["test.CSO"])!).GetAwaiter().GetResult(); }
                                finally { timer.Stop(); }
                                AssertTrue(dialogFailure is null, "Dialog validation: " + dialogFailure);
                                AssertTrue(handled, "CSO must show confirmation.");
                                AssertEqual(answer == true, accepted, "Only explicit confirmation may proceed.");
                            }
                            AssertTrue(((Task<bool>)confirm.Invoke(window, ["test.iso"])!).GetAwaiter().GetResult(), "Direct ISO does not require CSO consent.");
                        }

                        // Reject through the real scan entry point before database/tool work or row mutation.
                        using var item = new TaskQueueItemViewModel { OriginalPath = "reject.cso", StatusDetail = "conversion detail", ProgressValue = 37, IsProgressActive = true, IsIndeterminate = false };
                        var store = (QueueRowStore)typeof(MainWindow).GetField("_queueRowStore", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
                        var row = new QueueRowData { ItemId = item.QueueItemId, OriginalPath = item.OriginalPath, StatusDetail = "conversion detail", Progress = 37, IsProgressActive = true };
                        store.Append(row);
                        long rowVersion = store.Version;
                        var reject = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                        bool rejected = false;
                        reject.Tick += (_, _) =>
                        {
                            var dialog = app.Windows.OfType<RedumpNoticeDialog>().FirstOrDefault();
                            if (dialog is null) return;
                            rejected = true;
                            reject.Stop();
                            ((Button)dialog.FindName("CloseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        };
                        reject.Start();
                        try
                        {
                            var scan = (Task)typeof(MainWindow).GetMethod("RunDeepIntegrityValidationAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [item, "reject.cso", CancellationToken.None])!;
                            PumpUntil(window.Dispatcher, () => scan.IsCompleted, TimeSpan.FromSeconds(10));
                            scan.GetAwaiter().GetResult();
                        }
                        finally { reject.Stop(); }
                        AssertTrue(rejected, "Scan entry point must request consent.");
                        AssertEqual(IntegrityValidationState.None, item.IntegrityState, "Rejected consent must not start scanning.");
                        AssertTrue(item.DeepHashCachedResult is null, "Rejected consent creates no cached result.");
                        AssertEqual(rowVersion, store.Version, "Rejected consent does not mutate the row store.");
                        AssertEqual(37d, item.ProgressValue, "Consent preserves conversion progress.");
                        AssertEqual("conversion detail", item.StatusDetail, "Consent preserves conversion detail.");
                        AssertTrue(item.IsProgressActive && !item.IsIndeterminate, "Consent preserves conversion activity.");

                        typeof(MainWindow).GetMethod("ApplyRedumpProgressAndSync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window,
                            [item, RedumpOperationState.Hashing, "Redump detail", 55d, false, 1024L, 2048L, 100d, TimeSpan.FromSeconds(10)]);
                        AssertEqual(55d, item.RedumpProgressValue, "Independent Redump progress updates.");
                        AssertEqual(55d, row.RedumpProgress, "Row store receives independent Redump progress.");
                        AssertEqual(37d, row.Progress, "Row store preserves conversion progress.");
                        AssertEqual("conversion detail", row.StatusDetail, "Row store preserves conversion detail.");
                        AssertTrue(row.IsProgressActive && !row.IsIndeterminate, "Row store preserves conversion activity.");
                        AssertEqual(37d, item.ProgressValue, "Redump updates preserve conversion progress.");
                        AssertEqual("conversion detail", item.StatusDetail, "Redump updates preserve conversion detail.");
                    }
                    finally { window.Close(); }
                }
                finally { app.Shutdown(); }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        AssertTrue(thread.Join(TimeSpan.FromSeconds(60)), "CSO consent UI test timed out.");
        AssertTrue(failure is null, "CSO consent UI failed: " + failure);
    }
}
