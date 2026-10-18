using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.QueueRun;
using HakamiqChdTool.App.ViewModels;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private class IntakeSessionProxy : DefaultValueDispatchProxy
    {
        public static Dispatcher? Ui;
        public static int ErrorCount;
        public static int WaitFooterCount;
        public static int OffThreadCalls;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);

            switch (targetMethod.Name)
            {
                case "get_UiDispatcher":
                    return Ui;
                case "get_IncludeSubfolders":
                    return false;
                case "ShowError":
                    Interlocked.Increment(ref ErrorCount);
                    return null;
                case "SetFooterStatus":
                    if (args is [string message] && string.Equals(message, MainWindowMessages.WaitForBackgroundOp, StringComparison.Ordinal))
                    {
                        Interlocked.Increment(ref WaitFooterCount);
                    }

                    return null;
                default:
                    return base.Invoke(targetMethod, args);
            }
        }
    }

    // Two intake requests issued back to back on the UI thread, before either has yielded, must not both
    // become the active intake: the second is refused while the first holds the intake slot, and the
    // IsAddingFiles flag is only ever written on the UI thread.
    private static void TestOverlappingIntakeRequestsAreSerialized(string workDirectory)
    {
        IntakeSessionProxy.ErrorCount = 0;
        IntakeSessionProxy.WaitFooterCount = 0;
        IntakeSessionProxy.OffThreadCalls = 0;
        Exception? failure = null;
        int addingFlagWrittenOffThread = 0;

        // A real archive-shaped file makes the fast path probe it (a real async read) and then decline it.
        string archivePath = Path.Combine(workDirectory, "overlap-probe.zip");
        File.WriteAllBytes(archivePath, [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0]);
        string[] paths = [archivePath];

        var thread = new Thread(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            IntakeSessionProxy.Ui = dispatcher;
            IMainWindowSession session = DispatchProxy.Create<IMainWindowSession, IntakeSessionProxy>();
            IQueueRunCoordinator coordinator = DispatchProxy.Create<IQueueRunCoordinator, DefaultValueDispatchProxy>();

            _ = dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    using var viewModel = new MainWindowViewModel(session, coordinator, new ArrayList());
                    viewModel.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(MainWindowViewModel.IsAddingFiles) && !dispatcher.CheckAccess())
                        {
                            Interlocked.Increment(ref addingFlagWrittenOffThread);
                        }
                    };

                    Task first = viewModel.IngestPathsAsync(paths, QueueIngestKind.Mixed, QueueIntakeSource.UserInitiatedAdd);
                    Task second = viewModel.IngestPathsAsync(paths, QueueIngestKind.Mixed, QueueIntakeSource.UserInitiatedAdd);
                    await Task.WhenAll(first, second);
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
        AssertTrue(thread.Join(TimeSpan.FromSeconds(60)), "The intake dispatcher thread finished.");

        if (failure is not null)
        {
            throw new InvalidOperationException("Overlap scenario failed.", failure);
        }

        AssertEqual(1, IntakeSessionProxy.ErrorCount, "Exactly one of two overlapping intakes ran.");
        AssertEqual(1, IntakeSessionProxy.WaitFooterCount, "The overlapping intake was refused with the wait message.");
        AssertEqual(0, addingFlagWrittenOffThread, "IsAddingFiles is only changed on the UI thread.");
    }
}
