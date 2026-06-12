using HakamiqChdTool.App.Core.Workflow;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Localization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Queue;

internal static class QueueConstants
{
    internal const int DefaultMaxConcurrentItems = AppSettings.DefaultMaxConcurrentConversions;

    internal const string StatusPending = TaskQueueStateCodes.Pending;
    internal const string StatusRunning = TaskQueueStateCodes.Processing;
    internal const string StatusCompleted = TaskQueueStateCodes.Completed;
    internal const string StatusSkipped = TaskQueueStateCodes.Skipped;
    internal const string StatusFailed = TaskQueueStateCodes.Failed;
    internal const string StatusCancelled = TaskQueueStateCodes.Cancelled;
    internal const string StatusPasswordRequired = TaskQueueStateCodes.PasswordRequired;

    internal const string DefaultModeVerify = "Verify";

    internal const string CancellationTokenMissingKey = "LocQueue_CancellationTokenMissing";
    internal const string AlreadyScheduledKey = "LocQueue_AlreadyScheduled";
    internal const string OperationCancelledKey = "LocQueue_OperationCancelled";
    internal const string UiBindFailedKey = "LocQueue_UiBindFailed";
    internal const string CancelledBeforeStartKey = "LocQueue_CancelledBeforeStart";
}

internal sealed class QueueRuntimeState
{
    internal QueueRuntimeState(
        IChdWorkflowOrchestrator orchestrator,
        Func<AppSettings> getSettings,
        Func<string> getChdmanPath,
        int maxConcurrentItems,
        Func<AppFeature, bool>? canUseAppFeature)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(getSettings);
        ArgumentNullException.ThrowIfNull(getChdmanPath);

        int concurrency = AppSettings.NormalizeMaxConcurrentConversions(maxConcurrentItems);

        Orchestrator = orchestrator;
        GetSettings = getSettings;
        GetChdmanPath = getChdmanPath;
        CanUseAppFeature = canUseAppFeature ?? (static feature => Enum.IsDefined(feature));
        MaxConcurrentItems = concurrency;
        ProcessConcurrency = new SemaphoreSlim(concurrency, concurrency);
    }

    internal IChdWorkflowOrchestrator Orchestrator { get; }
    internal Func<AppSettings> GetSettings { get; }
    internal Func<string> GetChdmanPath { get; }
    internal Func<AppFeature, bool> CanUseAppFeature { get; }

    internal ConcurrentQueue<ChdQueueItem> WorkQueue { get; } = new();
    internal SemaphoreSlim Signal { get; } = new(0, int.MaxValue);
    internal object ProcessConcurrencyGate { get; } = new();
    internal SemaphoreSlim ProcessConcurrency { get; set; }
    internal int MaxConcurrentItems { get; set; }
    internal int? PendingMaxConcurrentItems { get; set; }

    internal object ItemsLock { get; } = new();
    internal List<ChdQueueItem> Items { get; } = [];
    internal ConcurrentDictionary<Guid, CancellationTokenSource> ItemTokens { get; } = new();
    internal ConcurrentDictionary<Guid, Task> RunningItems { get; } = new();
    internal ConcurrentDictionary<string, byte> ActivePaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal CancellationTokenSource ShutdownCts { get; } = new();
    internal object LoopGate { get; } = new();
    internal Task? LoopTask { get; set; }
    internal int Disposed;
}
