using HakamiqChdTool.App.Core.Workflow;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Queue;

public sealed class QueueManager : IQueueManager
{
    private const int DefaultMaxConcurrentItems = AppSettings.DefaultMaxConcurrentConversions;

    private const string StatusPending = TaskQueueStateCodes.Pending;
    private const string StatusRunning = TaskQueueStateCodes.Processing;
    private const string StatusCompleted = TaskQueueStateCodes.Completed;
    private const string StatusSkipped = TaskQueueStateCodes.Skipped;
    private const string StatusFailed = TaskQueueStateCodes.Failed;
    private const string StatusCancelled = TaskQueueStateCodes.Cancelled;
    private const string StatusPasswordRequired = TaskQueueStateCodes.PasswordRequired;

    private const string DefaultModeVerify = "Verify";

    private const string CancellationTokenMissingKey = "LocQueue_CancellationTokenMissing";
    private const string AlreadyScheduledKey = "LocQueue_AlreadyScheduled";
    private const string OperationCancelledKey = "LocQueue_OperationCancelled";
    private const string UiBindFailedKey = "LocQueue_UiBindFailed";
    private const string CancelledBeforeStartKey = "LocQueue_CancelledBeforeStart";

    private static readonly ILogger Logger = Log.ForContext<QueueManager>();

    private readonly IChdWorkflowOrchestrator _orchestrator;
    private readonly Func<AppSettings> _getSettings;
    private readonly Func<string> _getChdmanPath;
    private readonly Func<AppFeature, bool> _canUseAppFeature;
    private readonly ConcurrentQueue<ChdQueueItem> _workQueue = new();
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private readonly object _processConcurrencyGate = new();
    private SemaphoreSlim _processConcurrency;
    private int _maxConcurrentItems;
    private int? _pendingMaxConcurrentItems;
    private readonly object _itemsLock = new();
    private readonly List<ChdQueueItem> _items = [];
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _itemTokens = new();
    private readonly ConcurrentDictionary<Guid, Task> _runningItems = new();
    private readonly ConcurrentDictionary<string, byte> _activePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly object _loopGate = new();

    private Func<Guid, QueueItemSnapshot?> _resolveSnapshot = static _ => null;
    private Func<Guid, IQueueItemStateSink?> _resolveSink = static _ => null;
    private Action _onUiRefresh = static () => { };
    private Task? _loopTask;
    private int _disposed;

    public event Action<ChdQueueItem>? ItemUpdated;

    public IReadOnlyCollection<ChdQueueItem> Items
    {
        get
        {
            lock (_itemsLock)
            {
                return [.. _items];
            }
        }
    }

    public QueueManager(
        IChdWorkflowOrchestrator orchestrator,
        Func<AppSettings> getSettings,
        Func<string> getChdmanPath,
        int maxConcurrentItems = DefaultMaxConcurrentItems,
        Func<AppFeature, bool>? canUseAppFeature = null)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(getSettings);
        ArgumentNullException.ThrowIfNull(getChdmanPath);

        int concurrency = AppSettings.NormalizeMaxConcurrentConversions(maxConcurrentItems);

        _orchestrator = orchestrator;
        _getSettings = getSettings;
        _getChdmanPath = getChdmanPath;
        _canUseAppFeature = canUseAppFeature ?? (static feature => Enum.IsDefined(feature));
        _maxConcurrentItems = concurrency;
        _processConcurrency = new SemaphoreSlim(concurrency, concurrency);
    }

    public void UpdateMaxConcurrentItems(int maxConcurrentItems)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            Logger.Warning("Queue concurrency update ignored because queue manager has already been disposed.");
            return;
        }

        int normalized = AppSettings.NormalizeMaxConcurrentConversions(maxConcurrentItems);

        lock (_processConcurrencyGate)
        {
            if (normalized == _maxConcurrentItems)
            {
                _pendingMaxConcurrentItems = null;
                return;
            }

            if (HasActiveOrReservedConcurrencySlotUnsafe())
            {
                _pendingMaxConcurrentItems = normalized;

                Logger.Warning(
                    "Queue concurrency update deferred until queued work becomes idle. RequestedMaxConcurrentItems={RequestedMaxConcurrentItems} CurrentMaxConcurrentItems={CurrentMaxConcurrentItems} RunningCount={RunningCount}",
                    normalized,
                    _maxConcurrentItems,
                    _runningItems.Count);

                return;
            }

            ApplyMaxConcurrentItemsUnsafe(normalized);
        }
    }

    public QueueManager(
        IChdWorkflowOrchestrator orchestrator,
        Func<AppSettings> getSettings,
        Func<string> getChdmanPath,
        Func<Guid, QueueItemSnapshot?> resolveSnapshot,
        Func<Guid, IQueueItemStateSink?> resolveSink,
        Action onUiRefresh,
        int maxConcurrentItems = DefaultMaxConcurrentItems,
        Func<AppFeature, bool>? canUseAppFeature = null)
        : this(orchestrator, getSettings, getChdmanPath, maxConcurrentItems, canUseAppFeature)
    {
        ConfigurePresentationBindings(resolveSnapshot, resolveSink, onUiRefresh);
    }

    public void ConfigurePresentationBindings(
        Func<Guid, QueueItemSnapshot?> resolveSnapshot,
        Func<Guid, IQueueItemStateSink?> resolveSink,
        Action onUiRefresh)
    {
        ArgumentNullException.ThrowIfNull(resolveSnapshot);
        ArgumentNullException.ThrowIfNull(resolveSink);
        ArgumentNullException.ThrowIfNull(onUiRefresh);

        _resolveSnapshot = resolveSnapshot;
        _resolveSink = resolveSink;
        _onUiRefresh = onUiRefresh;
    }

    public QueueEnqueueResult Enqueue(ChdQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (Volatile.Read(ref _disposed) != 0)
        {
            Logger.Warning(
                "Queue item rejected because queue manager has already been disposed. QueueItemId={QueueItemId} Input={Input}",
                item.Id,
                item.InputPath);

            return QueueEnqueueResult.RejectedQueueShuttingDown;
        }

        if (string.IsNullOrWhiteSpace(item.InputPath))
        {
            Logger.Warning(
                "Queue item rejected because the input path is empty. QueueItemId={QueueItemId}",
                item.Id);

            return QueueEnqueueResult.RejectedInvalidItem;
        }

        if (_shutdownCts.IsCancellationRequested)
        {
            Logger.Warning(
                "Queue item rejected because queue shutdown has already started. QueueItemId={QueueItemId} Input={Input}",
                item.Id,
                item.InputPath);

            return QueueEnqueueResult.RejectedQueueShuttingDown;
        }

        if (string.Equals(item.Status, StatusRunning, StringComparison.OrdinalIgnoreCase))
        {
            Logger.Warning(
                "Queue item rejected because it is already running. QueueItemId={QueueItemId} Input={Input}",
                item.Id,
                item.InputPath);

            return QueueEnqueueResult.RejectedAlreadyRunning;
        }

        string pathKey = NormalizeQueuePath(item.InputPath);
        if (!_activePaths.TryAdd(pathKey, 0))
        {
            Logger.Information(
                "Queue item rejected because an item with the same normalized input path is already active. QueueItemId={QueueItemId} Input={Input} NormalizedPath={NormalizedPath}",
                item.Id,
                item.InputPath,
                pathKey);

            return QueueEnqueueResult.RejectedDuplicatePath;
        }

        var cts = new CancellationTokenSource();
        if (!_itemTokens.TryAdd(item.Id, cts))
        {
            cts.Dispose();
            _activePaths.TryRemove(pathKey, out _);
            throw new InvalidOperationException("Duplicate queue item id.");
        }

        try
        {
            lock (_itemsLock)
            {
                _items.Add(item);
            }

            _workQueue.Enqueue(item);
            _signal.Release();
            ItemUpdated?.Invoke(item);

            return QueueEnqueueResult.Accepted;
        }
        catch
        {
            if (_itemTokens.TryRemove(item.Id, out CancellationTokenSource? cleanupCts))
            {
                try
                {
                    cleanupCts.Dispose();
                }
                catch (ObjectDisposedException ex)
                {
                    Logger.Debug(
                        ex,
                        "Queue item cancellation token was already disposed during enqueue rollback. QueueItemId={QueueItemId}",
                        item.Id);
                }
            }

            _activePaths.TryRemove(pathKey, out _);

            lock (_itemsLock)
            {
                _items.Remove(item);
            }

            throw;
        }
    }

    public void Start()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            Logger.Warning("Queue start request ignored because queue manager has already been disposed.");
            return;
        }

        if (_shutdownCts.IsCancellationRequested)
        {
            Logger.Warning("Queue start request ignored because shutdown has already started.");
            return;
        }

        lock (_loopGate)
        {
            if (_shutdownCts.IsCancellationRequested)
            {
                Logger.Warning("Queue start request ignored inside loop gate because shutdown has already started.");
                return;
            }

            if (_loopTask is not null && !_loopTask.IsCompleted)
            {
                return;
            }

            _loopTask = Task.Run(() => ProcessLoopAsync(_shutdownCts.Token), CancellationToken.None);
        }
    }

    public void Stop()
    {
        List<ChdQueueItem>? waitingItems = null;

        lock (_itemsLock)
        {
            foreach (ChdQueueItem item in _items)
            {
                if (!string.Equals(item.Status, StatusPending, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                item.Status = StatusCancelled;
                waitingItems ??= [];
                waitingItems.Add(item);
            }
        }

        foreach (KeyValuePair<Guid, CancellationTokenSource> kv in _itemTokens.ToArray())
        {
            try
            {
                kv.Value.Cancel();
            }
            catch (ObjectDisposedException ex)
            {
                Logger.Debug(
                    ex,
                    "Queue item cancellation token was already disposed during Stop. QueueItemId={QueueItemId}",
                    kv.Key);
            }
        }

        if (waitingItems is null)
        {
            return;
        }

        foreach (ChdQueueItem item in waitingItems)
        {
            ItemUpdated?.Invoke(item);
            CompleteScheduledItem(item);
        }
    }

    public void Cancel(Guid id)
    {
        if (_itemTokens.TryGetValue(id, out CancellationTokenSource? cts))
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException ex)
            {
                Logger.Debug(
                    ex,
                    "Queue item cancellation token was already disposed during Cancel. QueueItemId={QueueItemId}",
                    id);
            }
        }

        ChdQueueItem? pending;

        lock (_itemsLock)
        {
            pending = _items.FirstOrDefault(x => x.Id == id);
            if (pending is not null && string.Equals(pending.Status, StatusPending, StringComparison.OrdinalIgnoreCase))
            {
                pending.Status = StatusCancelled;
            }
        }

        if (pending is null)
        {
            return;
        }

        ItemUpdated?.Invoke(pending);

        if (string.Equals(pending.Status, StatusCancelled, StringComparison.OrdinalIgnoreCase))
        {
            CompleteScheduledItem(pending);
        }
    }

    private async Task ProcessLoopAsync(CancellationToken shutdownToken)
    {
        try
        {
            while (!shutdownToken.IsCancellationRequested)
            {
                try
                {
                    await _signal.WaitAsync(shutdownToken).ConfigureAwait(false);

                    if (!_workQueue.TryDequeue(out ChdQueueItem? item) || item is null)
                    {
                        continue;
                    }

                    ScheduleQueuedItem(item, shutdownToken);
                }
                catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
                {
                    Logger.Debug("Queue processing loop cancellation observed inside loop.");
                    break;
                }
                catch (ObjectDisposedException ex) when (shutdownToken.IsCancellationRequested)
                {
                    Logger.Debug(ex, "Queue processing loop stopped because queue resources were disposed during shutdown.");
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Queue processing loop failed while scheduling an item. The loop will continue.");
                }
            }
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
        {
            Logger.Debug("Queue processing loop stopped because shutdown was requested.");
        }
    }

    private void ScheduleQueuedItem(ChdQueueItem item, CancellationToken shutdownToken)
    {
        if (string.Equals(item.Status, StatusCancelled, StringComparison.OrdinalIgnoreCase))
        {
            CompleteScheduledItem(item);
            return;
        }

        if (!_itemTokens.TryGetValue(item.Id, out CancellationTokenSource? itemCts))
        {
            item.Status = StatusFailed;
            item.Error = CancellationTokenMissingKey;
            ItemUpdated?.Invoke(item);
            CompleteScheduledItem(item);
            return;
        }

        Task runningTask = RunScheduledItemAsync(item, itemCts.Token, shutdownToken);

        if (_runningItems.TryAdd(item.Id, runningTask))
        {
            _ = runningTask.ContinueWith(
                completedTask => CompleteRunningItemTracking(item.Id, completedTask),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return;
        }

        item.Status = StatusFailed;
        item.Error = AlreadyScheduledKey;
        ItemUpdated?.Invoke(item);

        try
        {
            itemCts.Cancel();
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Debug(
                ex,
                "Queue item cancellation token was already disposed during duplicate scheduling. QueueItemId={QueueItemId}",
                item.Id);
        }

        CompleteScheduledItem(item);
    }

    private async Task RunScheduledItemAsync(
        ChdQueueItem item,
        CancellationToken itemToken,
        CancellationToken shutdownToken)
    {
        try
        {
            await ProcessQueuedItemWithGateAsync(item, itemToken, shutdownToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            ApplyWorkflowResult(item, WorkflowExecutionResult.Cancelled(OperationCancelledKey));
            ItemUpdated?.Invoke(item);

            Logger.Information(
                ex,
                "Queue item cancellation was contained at the scheduled-item boundary. QueueItemId={QueueItemId} Input={Input}",
                item.Id,
                item.InputPath);
        }
    }

    private void CompleteRunningItemTracking(Guid itemId, Task completedTask)
    {
        ObserveCompletedRunningTask(itemId, completedTask);
        _runningItems.TryRemove(itemId, out _);
        TryApplyPendingMaxConcurrentItemsIfIdle();
    }

    private void TryApplyPendingMaxConcurrentItemsIfIdle()
    {
        lock (_processConcurrencyGate)
        {
            if (_pendingMaxConcurrentItems is not int pending)
            {
                return;
            }

            if (HasActiveOrReservedConcurrencySlotUnsafe())
            {
                return;
            }

            ApplyMaxConcurrentItemsUnsafe(pending);
        }
    }

    private bool HasActiveOrReservedConcurrencySlotUnsafe()
    {
        return !_runningItems.IsEmpty || _processConcurrency.CurrentCount != _maxConcurrentItems;
    }

    private void ApplyMaxConcurrentItemsUnsafe(int maxConcurrentItems)
    {
        int normalized = AppSettings.NormalizeMaxConcurrentConversions(maxConcurrentItems);
        _processConcurrency = new SemaphoreSlim(normalized, normalized);
        _maxConcurrentItems = normalized;
        _pendingMaxConcurrentItems = null;
    }

    private void PruneCompletedRunningItemTracking()
    {
        foreach (KeyValuePair<Guid, Task> kv in _runningItems.ToArray())
        {
            if (!kv.Value.IsCompleted)
            {
                continue;
            }

            ObserveCompletedRunningTask(kv.Key, kv.Value);
            _runningItems.TryRemove(kv.Key, out _);
        }
    }

    private static void ObserveCompletedRunningTask(Guid itemId, Task completedTask)
    {
        if (completedTask.Exception is null)
        {
            return;
        }

        try
        {
            completedTask.Exception.Handle(static _ => true);
        }
        catch (Exception ex)
        {
            Logger.Debug(
                ex,
                "Queue item completed with an unobservable exception while cleaning running-item tracking. QueueItemId={QueueItemId}",
                itemId);
        }
    }

    private async Task ProcessQueuedItemWithGateAsync(
        ChdQueueItem item,
        CancellationToken itemToken,
        CancellationToken shutdownToken)
    {
        SemaphoreSlim processConcurrency;
        bool gateEntered = false;

        lock (_processConcurrencyGate)
        {
            processConcurrency = _processConcurrency;
        }

        try
        {
            using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(itemToken, shutdownToken);

            await processConcurrency.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            gateEntered = true;

            await ProcessQueuedItemAsync(item, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ApplyWorkflowResult(item, WorkflowExecutionResult.Cancelled(OperationCancelledKey));
            ItemUpdated?.Invoke(item);
        }
        catch (Exception ex)
        {
            ApplyWorkflowResult(
                item,
                WorkflowExecutionResult.Failure(
                    QueueItemFailureKind.Failed,
                    RuntimeDiagnosticFormatter.SummarizeException(ex)));

            Logger.Error(
                ex,
                "Queue item worker crashed. QueueItemId={QueueItemId} Input={Input}",
                item.Id,
                item.InputPath);

            ItemUpdated?.Invoke(item);
        }
        finally
        {
            if (gateEntered)
            {
                try
                {
                    processConcurrency.Release();
                }
                catch (ObjectDisposedException ex)
                {
                    Logger.Debug(
                        ex,
                        "Queue concurrency semaphore was already disposed before release. QueueItemId={QueueItemId}",
                        item.Id);
                }
                catch (SemaphoreFullException ex)
                {
                    Logger.Warning(
                        ex,
                        "Queue concurrency semaphore release exceeded its maximum count. QueueItemId={QueueItemId}",
                        item.Id);
                }
            }

            CompleteScheduledItem(item);
        }
    }

    private async Task ProcessQueuedItemAsync(ChdQueueItem item, CancellationToken itemToken)
    {
        try
        {
            await ProcessItemAsync(item, itemToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            WorkflowExecutionResult cancelled = WorkflowExecutionResult.Cancelled(OperationCancelledKey);
            ApplyWorkflowResult(item, cancelled);

            Logger.Information(
                ex,
                "Queue worker observed cancellation outside item processor. QueueItemId={QueueItemId} Input={Input}",
                item.Id,
                item.InputPath);

            ItemUpdated?.Invoke(item);
        }
        catch (Exception ex)
        {
            WorkflowExecutionResult failure = WorkflowExecutionResult.Failure(
                QueueItemFailureKind.Failed,
                RuntimeDiagnosticFormatter.SummarizeException(ex));

            ApplyWorkflowResult(item, failure);

            Logger.Error(
                ex,
                "Queue worker failed outside item processor. QueueItemId={QueueItemId} Input={Input}",
                item.Id,
                item.InputPath);

            ItemUpdated?.Invoke(item);
        }
    }

    private static string NormalizeQueuePath(string? inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return "\0empty";
        }

        string trimmed = inputPath.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "\0empty";
        }

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (ArgumentException ex)
        {
            Logger.Debug(ex, "Queue path normalization failed because the path contains invalid characters. InputPath={InputPath}", trimmed);
            return trimmed;
        }
        catch (NotSupportedException ex)
        {
            Logger.Debug(ex, "Queue path normalization failed because the path format is not supported. InputPath={InputPath}", trimmed);
            return trimmed;
        }
        catch (PathTooLongException ex)
        {
            Logger.Debug(ex, "Queue path normalization failed because the path is too long. InputPath={InputPath}", trimmed);
            return trimmed;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Debug(ex, "Queue path normalization failed because access to the path was denied. InputPath={InputPath}", trimmed);
            return trimmed;
        }
    }

    private void CompleteScheduledItem(ChdQueueItem item)
    {
        if (_itemTokens.TryRemove(item.Id, out CancellationTokenSource? cts))
        {
            try
            {
                cts.Dispose();
            }
            catch (ObjectDisposedException ex)
            {
                Logger.Debug(
                    ex,
                    "Queue item cancellation token was already disposed while completing item. QueueItemId={QueueItemId}",
                    item.Id);
            }
        }

        ReleaseQueuePath(item);

        lock (_itemsLock)
        {
            _items.Remove(item);
        }
    }

    private void ReleaseQueuePath(ChdQueueItem item)
    {
        _activePaths.TryRemove(NormalizeQueuePath(item.InputPath), out _);
    }

    private static AppSettings BuildEffectiveSettings(AppSettings source, QueueExecutionProfile executionProfile)
    {
        AppSettings result = source.Clone();

        if (executionProfile is QueueExecutionProfile.QuickConvert or QueueExecutionProfile.QuickExtract)
        {
            result.SkipExistingOutput = true;
            result.EnableDeepIntegrityCheck = false;
            result.ApplyStandardNamingBasedOnHash = false;
        }

        return result;
    }

    private async Task ProcessItemAsync(ChdQueueItem item, CancellationToken itemToken)
    {
        QueueItemSnapshot? snapshot = _resolveSnapshot(item.Id);
        IQueueItemStateSink? sink = _resolveSink(item.Id);

        if (snapshot is null || sink is null)
        {
            item.Status = StatusFailed;
            item.Error = UiBindFailedKey;

            Logger.Error(
                "Queue item UI bind failed. QueueItemId={QueueItemId} Input={Input}",
                item.Id,
                item.InputPath);

            ItemUpdated?.Invoke(item);
            return;
        }

        if (itemToken.IsCancellationRequested)
        {
            item.Status = StatusCancelled;
            item.Error = CancelledBeforeStartKey;

            Logger.Information(
                "Queue item cancelled before execution started. QueueItemId={QueueItemId} Input={Input}",
                item.Id,
                item.InputPath);

            ItemUpdated?.Invoke(item);
            return;
        }

        item.Status = StatusRunning;
        item.Progress = 0;
        ItemUpdated?.Invoke(item);

        try
        {
            AppSettings settings = BuildEffectiveSettings(_getSettings(), item.ExecutionProfile);

            bool verifyOnly = string.Equals(item.Mode, DefaultModeVerify, StringComparison.OrdinalIgnoreCase);
            ChdWorkflowMode workflowMode = verifyOnly
                ? ChdWorkflowMode.VerifyExistingChd
                : ChdWorkflowMode.ProcessQueueItem;

            var request = new ChdTaskRequest
            {
                InputPath = item.InputPath,
                IsArchive = !verifyOnly && WorkflowPathHelpers.IsArchivePath(snapshot.SourcePath),
                Verify = verifyOnly || settings.VerifyAfterConversion,
                OnProgress = p =>
                {
                    if (itemToken.IsCancellationRequested || IsTerminalStatus(item.Status))
                    {
                        return;
                    }

                    double nextProgress = Math.Clamp(p, 0, 99);

                    lock (item)
                    {
                        if (nextProgress < item.Progress)
                        {
                            return;
                        }

                        item.Progress = nextProgress;
                    }

                    ItemUpdated?.Invoke(item);
                },
                Options = new ChdWorkflowTaskContext
                {
                    Snapshot = snapshot,
                    Sink = sink,
                    Settings = settings,
                    GetChdmanPath = _getChdmanPath,
                    CanUseAppFeature = _canUseAppFeature,
                    Mode = workflowMode,
                    OnUiRefresh = _onUiRefresh
                }
            };

            WorkflowExecutionResult result = await _orchestrator.ProcessAsync(request, itemToken).ConfigureAwait(false);

            if (itemToken.IsCancellationRequested && result.Outcome == WorkflowExecutionOutcome.Success)
            {
                result = WorkflowExecutionResult.Cancelled(OperationCancelledKey);
            }

            ApplyWorkflowResult(item, result);
        }
        catch (OperationCanceledException)
        {
            WorkflowExecutionResult cancelled = WorkflowExecutionResult.Cancelled(OperationCancelledKey);
            ApplyWorkflowResult(item, cancelled);

            Logger.Information(
                "Queue item cancelled during processing. QueueItemId={QueueItemId} Input={Input} Outcome={Outcome}",
                item.Id,
                item.InputPath,
                cancelled.Outcome);
        }
        catch (Exception ex)
        {
            WorkflowExecutionResult failure = WorkflowExecutionResult.Failure(
                QueueItemFailureKind.Failed,
                RuntimeDiagnosticFormatter.SummarizeException(ex));

            ApplyWorkflowResult(item, failure);

            Logger.Error(
                ex,
                "Queue item processing failed. QueueItemId={QueueItemId} Input={Input} Mode={Mode} Outcome={Outcome}",
                item.Id,
                item.InputPath,
                item.Mode,
                failure.Outcome);
        }
        finally
        {
            ItemUpdated?.Invoke(item);
        }
    }

    private static void ApplyWorkflowResult(ChdQueueItem item, WorkflowExecutionResult result)
    {
        if (IsTerminalStatus(item.Status))
        {
            return;
        }

        item.Status = ResolveQueueStatusForWorkflowResult(result);

        if (result.Outcome is WorkflowExecutionOutcome.Failure or WorkflowExecutionOutcome.Cancelled
            && !string.IsNullOrWhiteSpace(result.StatusDetail))
        {
            item.Error = result.StatusDetail;
        }

        if (result.Outcome == WorkflowExecutionOutcome.Success
            && result.TerminalSuccessOutcome is QueueItemTerminalOutcome.Healthy or QueueItemTerminalOutcome.Extracted or QueueItemTerminalOutcome.Moved)
        {
            item.Progress = 100;
        }

        item.OutputPath = string.IsNullOrWhiteSpace(result.OutputPath) ? null : result.OutputPath;
    }

    private static string ResolveQueueStatusForWorkflowResult(WorkflowExecutionResult result)
    {
        return result.Outcome switch
        {
            WorkflowExecutionOutcome.Skipped => StatusSkipped,
            WorkflowExecutionOutcome.Success => StatusCompleted,
            WorkflowExecutionOutcome.Cancelled => StatusCancelled,
            WorkflowExecutionOutcome.Failure => result.TerminalFailureKind switch
            {
                QueueItemFailureKind.PasswordRequired => StatusPasswordRequired,
                QueueItemFailureKind.Unsupported => StatusSkipped,
                _ => StatusFailed
            },
            _ => StatusFailed
        };
    }

    private static bool IsTerminalStatus(string? status) =>
        string.Equals(status, StatusCompleted, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, StatusFailed, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, StatusCancelled, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, StatusSkipped, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, StatusPasswordRequired, StringComparison.OrdinalIgnoreCase);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _shutdownCts.Cancel();
        Stop();

        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException ex)
        {
            Logger.Debug(ex, "Queue shutdown signal was already full during disposal.");
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Debug(ex, "Queue shutdown signal was already disposed during disposal.");
        }

        Task? loopTask;
        lock (_loopGate)
        {
            loopTask = _loopTask;
        }

        if (loopTask is not null)
        {
            try
            {
                Task completedLoop = await Task.WhenAny(
                        loopTask,
                        Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None))
                    .ConfigureAwait(false);

                if (ReferenceEquals(completedLoop, loopTask))
                {
                    await loopTask.ConfigureAwait(false);
                }
                else
                {
                    Logger.Warning("Queue processing loop did not stop within disposal timeout.");
                }
            }
            catch (OperationCanceledException ex)
            {
                Logger.Debug(ex, "Queue processing loop cancelled during disposal.");
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Queue processing loop faulted during disposal.");
            }
        }

        Task[] runningItems = [.. _runningItems.Values];
        bool allRunningItemsCompleted = true;

        if (runningItems.Length > 0)
        {
            try
            {
                Task runningCompletion = Task.WhenAll(runningItems);
                Task completedRunning = await Task.WhenAny(
                        runningCompletion,
                        Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None))
                    .ConfigureAwait(false);

                if (ReferenceEquals(completedRunning, runningCompletion))
                {
                    await runningCompletion.ConfigureAwait(false);
                }
                else
                {
                    allRunningItemsCompleted = false;

                    Logger.Warning(
                        "Queue disposal timed out while waiting for {RunningCount} running item(s). Queue-owned semaphores will not be disposed to avoid ObjectDisposedException in delayed workers.",
                        runningItems.Length);
                }
            }
            catch (OperationCanceledException ex)
            {
                Logger.Debug(ex, "Running queue item cancelled during disposal.");
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "One or more running queue items faulted during disposal.");
            }
        }

        if (allRunningItemsCompleted)
        {
            PruneCompletedRunningItemTracking();
        }

        _activePaths.Clear();

        if (allRunningItemsCompleted && _runningItems.IsEmpty)
        {
            DisposeOwnedSynchronizationResources();
        }
        else
        {
            Logger.Warning(
                "Queue-owned synchronization resources were not disposed because {RunningCount} item(s) are still tracked.",
                _runningItems.Count);
        }

        foreach (Guid id in _itemTokens.Keys.ToArray())
        {
            if (_runningItems.ContainsKey(id))
            {
                Logger.Warning(
                    "Queue item cancellation token was not disposed because the item is still tracked as running. QueueItemId={QueueItemId}",
                    id);

                continue;
            }

            if (_itemTokens.TryRemove(id, out CancellationTokenSource? cts))
            {
                try
                {
                    cts.Dispose();
                }
                catch (ObjectDisposedException ex)
                {
                    Logger.Debug(
                        ex,
                        "Queue item cancellation token was already disposed during QueueManager disposal. QueueItemId={QueueItemId}",
                        id);
                }
            }
        }

        ItemUpdated = null;
        _resolveSnapshot = static _ => null;
        _resolveSink = static _ => null;
        _onUiRefresh = static () => { };
    }

    private void DisposeOwnedSynchronizationResources()
    {
        try
        {
            _signal.Dispose();
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Debug(ex, "Queue signal semaphore was already disposed.");
        }

        try
        {
            _processConcurrency.Dispose();
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Debug(ex, "Queue concurrency semaphore was already disposed.");
        }

        _shutdownCts.Dispose();
    }
}