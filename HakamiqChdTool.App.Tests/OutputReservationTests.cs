using HakamiqChdTool.App.Core.Workflow;
using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static void TestSingleInstanceGateBlocksSecondProcess(string workDirectory)
    {
        AssertTrue(HakamiqChdTool.App.SingleInstanceGate.TryAcquire(out Mutex? ownedMutex, out Exception? acquireError),
            "The test process establishes the local single-instance gate. Error=" + acquireError);
        using (ownedMutex)
        {
            string exePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("The test host process path is unavailable.");
            var startInfo = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            AddMutexProbeArguments(startInfo, exePath, "--mutex-probe");
            using Process child = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start the mutex probe child process.");
            AssertTrue(child.WaitForExit(30000), "The second process exits after detecting the existing local mutex.");
            string output = child.StandardOutput.ReadToEnd().Trim();
            string error = child.StandardError.ReadToEnd();
            AssertEqual(0, child.ExitCode, "The second process reports an existing owner. stderr=" + error);
            AssertEqual("EXISTING", output, "The second process cannot acquire the single-instance gate.");
        }
    }

    private static void TestSingleInstanceGateReportsCreationError(string workDirectory)
    {
        string exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The test host process path is unavailable.");
        var startInfo = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        AddMutexProbeArguments(startInfo, exePath, "--mutex-probe", "--mutex-probe-invalid-name");
        using Process child = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the mutex error probe child process.");
        AssertTrue(child.WaitForExit(30000), "The mutex creation error probe exits.");
        string output = child.StandardOutput.ReadToEnd().Trim();
        string error = child.StandardError.ReadToEnd();
        AssertEqual(1, child.ExitCode, "The probe reports creation failure. stderr=" + error);
        AssertEqual("ERROR", output, "Creation failure is distinguishable from an existing owner.");
    }

    private static void AddMutexProbeArguments(ProcessStartInfo startInfo, string exePath, params string[] probeArguments)
    {
        if (string.Equals(Path.GetFileName(exePath), "dotnet", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(exePath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
        }

        foreach (string argument in probeArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }

    private static void TestOutputReservationReleasesAfterFailureAndCancellation(string workDirectory)
    {
        string root = Path.Combine(Path.GetTempPath(), "hct-output-reservation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string final = Path.Combine(root, "result.bin");
        string pending = Path.Combine(root, "pending.bin");
        string sourceA = Path.Combine(root, "A.iso");
        string sourceB = Path.Combine(root, "B.iso");
        string sourceC = Path.Combine(root, "C.iso");
        try
        {
            AssertThrows<IOException>(
                () => WorkflowSessionOutputRegistry.TryPublish(
                    final, sourceA, CancellationToken.None,
                    () => throw new IOException("deterministic publish failure")),
                "A publication failure propagates.");

            File.WriteAllText(pending, "published-after-failure");
            AssertTrue(
                WorkflowSessionOutputRegistry.TryPublish(
                    final, sourceB, CancellationToken.None,
                    () => { File.Move(pending, final); return true; }),
                "A different source can reserve and publish after a failed operation released its reservation.");

            File.Delete(final);
            string cancellationFinal = Path.Combine(root, "cancellation-result.bin");
            using var publisherEntered = new ManualResetEventSlim(false);
            using var releasePublisher = new ManualResetEventSlim(false);
            using var waiterReachedMutex = new ManualResetEventSlim(false);
            using var cancellation = new CancellationTokenSource();
            WorkflowSessionOutputRegistry.BeforeMutexWaitForTests = source =>
            {
                if (string.Equals(source, sourceC, StringComparison.OrdinalIgnoreCase))
                {
                    waiterReachedMutex.Set();
                }
            };

            Task<bool> publisher = Task.Run(() => WorkflowSessionOutputRegistry.TryPublish(
                cancellationFinal, sourceA, CancellationToken.None,
                () =>
                {
                    publisherEntered.Set();
                    if (!releasePublisher.Wait(TimeSpan.FromSeconds(30)))
                    {
                        throw new TimeoutException("Test publisher release was not signaled.");
                    }
                    File.WriteAllText(cancellationFinal, "publisher-result");
                    return true;
                }));
            AssertTrue(publisherEntered.Wait(TimeSpan.FromSeconds(30)), "The first publisher owns the reservation.");

            bool cancelledActionRan = false;
            Task<bool> waiter = Task.Run(() => WorkflowSessionOutputRegistry.TryPublish(
                cancellationFinal, sourceC, cancellation.Token,
                () => { cancelledActionRan = true; return true; }));
            try
            {
                AssertTrue(waiterReachedMutex.Wait(TimeSpan.FromSeconds(30)), "The second publisher reached the mutex wait while the first held it.");
                cancellation.Cancel();
                AssertThrows<OperationCanceledException>(
                    () => waiter.GetAwaiter().GetResult(),
                    "Cancellation while waiting propagates.");
                AssertFalse(cancelledActionRan, "Cancelled work never enters publication.");
            }
            finally
            {
                releasePublisher.Set();
                WorkflowSessionOutputRegistry.BeforeMutexWaitForTests = null;
            }

            AssertTrue(publisher.GetAwaiter().GetResult(), "The original publisher completes after cancellation of its waiter.");
            AssertEqual("publisher-result", File.ReadAllText(cancellationFinal), "The published output remains intact.");

            File.WriteAllText(pending, "published-after-cancellation");
            AssertTrue(
                WorkflowSessionOutputRegistry.TryPublish(
                    cancellationFinal, sourceA, CancellationToken.None,
                    () => { File.Move(pending, cancellationFinal, overwrite: true); return true; }),
                "A later operation acquires the reservation after the cancelled waiter exits.");
        }
        finally
        {
            WorkflowSessionOutputRegistry.BeforeMutexWaitForTests = null;
            Directory.Delete(root, recursive: true);
        }
    }
}
