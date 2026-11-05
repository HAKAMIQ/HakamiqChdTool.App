using HakamiqChdTool.App.Core.Workflow;
using HakamiqChdTool.App.Ui.Shell;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using Velopack.Locators;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static void TestPublicationMutexCreationFailure(string workDirectory)
    {
        string final = Path.Combine(workDirectory, "mutex-failure.chd");
        string pending = Path.Combine(workDirectory, "mutex-pending.chd");
        string source = Path.Combine(workDirectory, "mutex-source.iso");
        File.WriteAllText(final, "existing output");
        File.WriteAllText(pending, "pending output");
        File.WriteAllText(source, "original source");
        string mutexName = @"Global\HakamiqChdTool.Output." + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(final).ToUpperInvariant())));
        var sink = new PublicationFailureLogSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        ILogger previous = Log.Logger;
        Log.Logger = logger;
        try
        {
            // Windows shares names across kernel object types: this makes Mutex creation fail deterministically.
            using (var conflictingEvent = new EventWaitHandle(false, EventResetMode.ManualReset, mutexName))
            {
                bool published = false;
                AssertHandledPublicationFailure(() => WorkflowSessionOutputRegistry.TryPublish(
                    final, source, CancellationToken.None, () => { published = true; return true; }));
                AssertFalse(published, "Publication cannot run without its mutex.");
                AssertHandledPublicationFailure(() => WorkflowSessionOutputRegistry.TryPromote(
                    pending, final, source, allowOverwrite: true, CancellationToken.None));
                AssertEqual("existing output", File.ReadAllText(final), "Existing output is preserved.");
                AssertEqual("pending output", File.ReadAllText(pending), "Pending output is preserved.");
                AssertEqual("original source", File.ReadAllText(source), "Source media is preserved.");
                AssertFalse(WorkflowSessionOutputRegistry.IsProducedByOtherSource(final, pending),
                    "Failed publication does not record ownership.");
                AssertEqual(2, sink.Errors.Count, "Both failures are logged as errors.");
                foreach (LogEvent entry in sink.Errors)
                {
                    AssertTrue(entry.Exception is WaitHandleCannotBeOpenedException,
                        "The original mutex creation error remains available in diagnostics.");
                    AssertTrue(entry.RenderMessage().Contains("Publication blocked", StringComparison.Ordinal),
                        "Diagnostics explain that publication was blocked.");
                }
            }

            AssertTrue(WorkflowSessionOutputRegistry.TryPromote(
                pending, final, source, allowOverwrite: true, CancellationToken.None),
                "Publication can recover once mutex creation succeeds.");
            AssertEqual("pending output", File.ReadAllText(final), "Recovery publishes the pending bytes.");
            AssertEqual("original source", File.ReadAllText(source), "Publication preserves the source.");
        }
        finally
        {
            Log.Logger = previous;
        }
    }

    private static void AssertHandledPublicationFailure(Func<bool> publish)
    {
        try
        {
            publish();
            throw new InvalidOperationException("Expected a handled publication I/O failure, not success or SkippedExists.");
        }
        catch (IOException ex)
        {
            AssertEqual("LocConversion_Failed", ex.Message,
                "Existing workflow failure handling receives a localized result detail.");
            AssertTrue(ex.InnerException is null, "The result formatter cannot expose the raw kernel exception.");
        }
    }

    private static void TestUnsetVelopackLocatorSkipsUpdateManager()
    {
        AssertFalse(VelopackLocator.IsCurrentSet, "This regression runs in an unpackaged test process.");
        MethodInfo createManager = typeof(UpdateService).GetMethod("CreateUpdateManager", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Update manager factory not found.");
        int missingLocatorExceptions = 0;
        EventHandler<FirstChanceExceptionEventArgs> observe = (_, args) =>
        {
            if (args.Exception.Message.Contains("No VelopackLocator has been set", StringComparison.Ordinal))
                missingLocatorExceptions++;
        };
        AppDomain.CurrentDomain.FirstChanceException += observe;
        try
        {
            AssertTrue(createManager.Invoke(null, null) is null, "An unset locator skips manager creation.");
            AssertEqual(0, missingLocatorExceptions, "The debugger sees no first-chance missing-locator exception.");
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= observe;
        }
    }

    private sealed class PublicationFailureLogSink : ILogEventSink
    {
        public List<LogEvent> Errors { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            if (logEvent.Level == LogEventLevel.Error)
                Errors.Add(logEvent);
        }
    }
}
