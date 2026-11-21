using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Core.Session;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.PostProcessing;
using HakamiqChdTool.App.Ui.Queue;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using Serilog;

namespace HakamiqChdTool.App.QueueRun;

/// <summary>The result of one quick run, ready to show.</summary>
internal sealed record QuickRunResult(SessionRunMetrics Metrics, RunSummaryCard Card, string Report, bool WasCancelled);

/// <summary>
/// Runs files from the Explorer menu without the main window. It uses the same pieces as the main window:
/// <see cref="MainWindowViewModel"/> intake, <see cref="QueueRunCoordinator"/> (run, cancel), <see cref="QueueController"/>
/// and the given <see cref="QueueManager"/>, so conversion settings, quick profiles and cleanup are the same.
/// Files that arrive while a run is in progress wait and run after it.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
internal sealed class QuickRunHost : IAsyncDisposable
{
    private readonly QueueManager _queue;
    private readonly QueueController _controller;
    private readonly QueueRunCoordinator _coordinator;
    private readonly MainWindowViewModel _intake;
    private readonly QueueRowStore _rows = new();
    private readonly ConcurrentDictionary<Guid, TaskQueueStateAdapter> _sinks = new();
    private readonly Dictionary<Guid, TaskQueueItemViewModel> _items = new();
    private readonly Queue<(QueueExecutionProfile Profile, string[] Paths)> _pending = new();
    private readonly Func<AppSettings> _settings;
    private readonly PostConversionArtifactService _artifacts;
    private readonly Dispatcher _dispatcher;
    private readonly Func<Window?> _owner;

    private QueueExecutionProfile _profile;
    private DateTime _runStartedUtc;
    private int _adding;
    private bool _pumping;
    private bool _disposed;

    public QuickRunHost(
        QueueManager queue,
        Func<AppSettings> settings,
        PostConversionArtifactService artifacts,
        Dispatcher dispatcher,
        Func<Window?> owner,
        bool autoStart)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        AutoStart = autoStart;

        _rows.CollectionChanged += OnRowsChanged;
        _rows.RowMutated += OnRowMutated;
        _queue.ItemUpdated += OnQueueItemUpdated;
        _queue.ConfigureUiBindings(
            id => _rows.GetById(id)?.ToSnapshot(),
            id => _sinks.TryGetValue(id, out TaskQueueStateAdapter? sink) ? sink : null,
            RaiseChanged);

        _controller = new QueueController(_queue);
        _coordinator = new QueueRunCoordinator(_controller, new Port(this));
        _intake = new MainWindowViewModel(new Session(this), _coordinator, new ArrayList());
        _coordinator.RunStateChanged += RaiseChanged;
    }

    /// <summary>Rows in the order they were added, updated on the UI thread.</summary>
    public ObservableCollection<TaskQueueItemViewModel> Items { get; } = [];

    /// <summary>False while the window waits for Start (the run may delete the source).</summary>
    public bool AutoStart { get; private set; }

    public bool IsProcessing => _coordinator.IsProcessing;

    public bool IsBusy => IsProcessing || _pumping || Volatile.Read(ref _adding) > 0;

    public bool HasWaitingFiles => _pending.Count > 0 || EnumerateQueuedIds().Count > 0;

    public QuickRunResult? LastResult { get; private set; }

    public string StatusText { get; private set; } = string.Empty;

    /// <summary>Raised on the UI thread when the rows, the run state or the status change.</summary>
    public event Action? Changed;

    public event Action<QuickRunResult>? RunCompleted;

    public event Action<string, string>? ErrorRaised;

    public static QueueOperationMode OperationFor(QueueExecutionProfile profile) => profile switch
    {
        QueueExecutionProfile.QuickConvert => QueueOperationMode.Convert,
        QueueExecutionProfile.QuickExtract => QueueOperationMode.Extract,
        QueueExecutionProfile.QuickVerify => QueueOperationMode.Verify,
        _ => QueueOperationMode.None
    };

    /// <summary>Adds files for a quick profile. They are added, and run when <see cref="AutoStart"/> is on.</summary>
    public Task AddAsync(QueueExecutionProfile profile, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (_disposed || paths.Count == 0 || OperationFor(profile) == QueueOperationMode.None)
        {
            return Task.CompletedTask;
        }

        _pending.Enqueue((profile, [.. paths]));
        return PumpAsync();
    }

    /// <summary>Starts the waiting files, and keeps starting files that arrive later.</summary>
    public Task StartAsync()
    {
        AutoStart = true;
        return PumpAsync();
    }

    public void Cancel() => _coordinator.RequestCancel();

    /// <summary>Source paths of rows that did not finish (failed, stopped, waiting), for opening them in the main window.</summary>
    public IReadOnlyList<string> UnfinishedSourcePaths()
    {
        var paths = new List<string>();
        foreach (QueueRowData row in _rows.Rows)
        {
            if (row.CurrentState is not (TaskQueueStateCodes.Completed or TaskQueueStateCodes.Skipped)
                && !string.IsNullOrWhiteSpace(row.OriginalPath))
            {
                paths.Add(row.OriginalPath);
            }
        }

        foreach ((_, string[] waiting) in _pending)
        {
            paths.AddRange(waiting);
        }

        return [.. paths.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private async Task PumpAsync()
    {
        if (_pumping || _disposed)
        {
            return;
        }

        _pumping = true;
        RaiseChanged();
        try
        {
            while (!_disposed)
            {
                if (_pending.Count > 0 && !IsProcessing)
                {
                    (QueueExecutionProfile profile, string[] paths) = _pending.Dequeue();
                    var batch = new List<string>(paths);

                    // Explorer starts one process per selected file; requests for the same operation join one batch.
                    while (_pending.Count > 0 && _pending.Peek().Profile == profile)
                    {
                        batch.AddRange(_pending.Dequeue().Paths);
                    }

                    await IngestAsync(profile, batch).ConfigureAwait(true);
                    continue;
                }

                if (AutoStart && !IsProcessing && EnumerateQueuedIds().Count > 0)
                {
                    await _coordinator.StartProcessingAsync().ConfigureAwait(true);
                    continue;
                }

                break;
            }
        }
        finally
        {
            _pumping = false;
            RaiseChanged();
        }
    }

    private async Task IngestAsync(QueueExecutionProfile profile, IReadOnlyList<string> paths)
    {
        _profile = profile;
        Interlocked.Increment(ref _adding);
        try
        {
            QueueIngestKind kind = paths.Any(Directory.Exists) ? QueueIngestKind.Mixed : QueueIngestKind.FilesOnly;
            await _intake.IngestQuickPathsAsync(paths, kind, profile, QueueIntakeSource.UserInitiatedAdd).ConfigureAwait(true);
        }
        catch (OperationCanceledException ex)
        {
            Log.Debug(ex, "Quick window intake cancelled.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Quick window intake failed.");
            ErrorRaised?.Invoke(MainWindowMessages.AddFilesErrorTitle, RuntimeDiagnosticFormatter.SummarizeException(ex));
        }
        finally
        {
            Interlocked.Decrement(ref _adding);
        }
    }

    private bool IsRowQueued(QueueRowData row) =>
        QueueModeResolver.IsWaitingRowRunnableForMode(row, OperationFor(_profile))
        && ProcessingStateMapper.Map(row.CurrentState, null) == ProcessingState.Queued;

    private List<Guid> EnumerateQueuedIds()
    {
        var ids = new List<Guid>();
        foreach (QueueRowData row in _rows.Rows)
        {
            if (IsRowQueued(row))
            {
                ids.Add(row.ItemId);
            }
        }

        return ids;
    }

    private void OnRowsChanged(QueueRowStoreCollectionDelta delta)
    {
        if (delta is QueueRowStoreAppended appended)
        {
            for (int i = 0; i < appended.Count; i++)
            {
                if (_rows.GetByIndex(appended.StartIndex + i) is QueueRowData row)
                {
                    _sinks[row.ItemId] = new TaskQueueStateAdapter(row.ItemId, _rows);
                    OnUi(() =>
                    {
                        if (!_items.ContainsKey(row.ItemId))
                        {
                            var vm = new TaskQueueItemViewModel(row);
                            _items[row.ItemId] = vm;
                            Items.Add(vm);
                        }
                    });
                }
            }
        }

        RaiseChanged();
    }

    private void OnRowMutated(Guid id)
    {
        OnUi(() =>
        {
            if (_items.TryGetValue(id, out TaskQueueItemViewModel? vm) && _rows.GetById(id) is QueueRowData row)
            {
                vm.ApplyRowMutation(row);
            }
        });
    }

    // Same as the main window: a terminal item is no longer bound to the queue, and a cancelled one is shown as cancelled.
    private void OnQueueItemUpdated(ChdQueueItem item)
    {
        if (!QueueRowTerminal.IsTerminal(item))
        {
            return;
        }

        var snapshot = new ChdQueueItem { Id = item.Id, InputPath = item.InputPath, Status = item.Status, Error = item.Error };
        OnUi(() =>
        {
            _rows.Mutate(snapshot.Id, row => row.HasActiveQueueBinding = false);
            if (_items.TryGetValue(snapshot.Id, out TaskQueueItemViewModel? vm))
            {
                vm.HasActiveQueueBinding = false;
            }

            QueueRowTerminal.ApplyCancelled(_rows, snapshot);
        });
    }

    private TaskQueueItemViewModel? GetItem(Guid id)
    {
        if (_items.TryGetValue(id, out TaskQueueItemViewModel? vm))
        {
            return vm;
        }

        if (_rows.GetById(id) is not QueueRowData row)
        {
            return null;
        }

        vm = new TaskQueueItemViewModel(row);
        _items[id] = vm;
        Items.Add(vm);
        return vm;
    }

    private void SetActiveBinding(TaskQueueItemViewModel item, bool value)
    {
        _rows.Mutate(item.QueueItemId, row => row.HasActiveQueueBinding = value);
        item.HasActiveQueueBinding = value;
    }

    private void SetStatus(string keyOrText)
    {
        OnUi(() =>
        {
            StatusText = ArabicUi.ResolveDisplayString(keyOrText);
            Changed?.Invoke();
        });
    }

    private Task<PostConversionArtifactResult> GenerateM3uAsync(IReadOnlyList<string> outputPaths)
    {
        AppSettings settings = _settings();
        if (!settings.EnableAutoM3uGeneration || outputPaths.Count == 0)
        {
            return Task.FromResult(PostConversionArtifactResult.Empty);
        }

        string[] outputs = RunSummaryItems.CompletedChdOutputsForM3u(outputPaths);
        if (outputs.Length == 0)
        {
            return Task.FromResult(PostConversionArtifactResult.Empty);
        }

        try
        {
            return Task.FromResult(_artifacts.GenerateM3uPlaylists(outputs, settings.OverwriteExistingM3uPlaylists));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Warning(ex, "M3U playlist generation failed after a quick run.");
            return Task.FromResult(PostConversionArtifactResult.WithFailure("M3U", "LocPostProcessing_M3uGenerationFailed"));
        }
    }

    private void BuildResult(bool wasCancelled, PostConversionArtifactResult artifacts)
    {
        OnUi(() =>
        {
            SessionRunSummaryItem[] items = [.. _rows.Rows.Select(RunSummaryItems.FromRow)];
            SessionRunMetrics metrics = SessionRunMetricsCalculator.ComputeMetrics(items, artifacts);
            TimeSpan elapsed = _runStartedUtc == default ? TimeSpan.Zero : DateTime.UtcNow - _runStartedUtc;
            var result = new QuickRunResult(
                metrics,
                RunSummaryCardBuilder.Build(metrics, wasCancelled, elapsed, ArabicUi.Get),
                LocalizedSessionRunSummaryFormatter.Format(metrics, wasCancelled),
                wasCancelled);

            LastResult = result;
            StatusText = ArabicUi.ResolveDisplayString(wasCancelled
                ? MainWindowMessages.SessionCancelledFooter
                : MainWindowMessages.SessionCompletedFooter);
            RunCompleted?.Invoke(result);
            Changed?.Invoke();
        });
    }

    private void RaiseChanged() => OnUi(() => Changed?.Invoke());

    private void OnUi(Action action)
    {
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        try
        {
            _ = _dispatcher.BeginInvoke(action, DispatcherPriority.Background);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException)
        {
            Log.Debug(ex, "Quick window update skipped during shutdown.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pending.Clear();
        _coordinator.RunStateChanged -= RaiseChanged;
        _rows.CollectionChanged -= OnRowsChanged;
        _rows.RowMutated -= OnRowMutated;
        _queue.ItemUpdated -= OnQueueItemUpdated;

        // Same order as the main window: view model, coordinator, controller, then the queue.
        _intake.Dispose();
        _coordinator.Dispose();
        _controller.Dispose();
        await _queue.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class Port(QuickRunHost host) : IQueueRunUiPort
    {
        public Window Owner => host._owner() ?? Application.Current.MainWindow;

        public bool IncludeSubfolders => host._settings().IncludeSubfolders;

        public bool IsQueueInteractionLocked => host.IsProcessing || Volatile.Read(ref host._adding) > 0;

        public QueueExecutionProfile GetSelectedInputExecutionProfile() => host._profile;

        public string GetSelectedInputDialogTitle(QueueExecutionProfile profile) => string.Empty;

        public string GetSelectedInputDialogFilter(QueueExecutionProfile profile) => string.Empty;

        public string GetSelectedFolderDialogDescription() => string.Empty;

        public void SetFooterStatus(string message) => host.SetStatus(message);

        public void UpdateUiState() => host.RaiseChanged();

        public void ShowError(string title, string body) => host.OnUi(() => host.ErrorRaised?.Invoke(title, body));

        // Nothing in the quick window changes settings, so there is nothing to capture or save.
        public void CaptureThemeIntoSettings()
        {
        }

        public void PersistSettings()
        {
        }

        public void ResetRunSummary()
        {
            host._runStartedUtc = DateTime.UtcNow;
            host.LastResult = null;
        }

        public void BuildRunSummary(bool showCompletionDialog, bool wasCancelled, bool processedSelectionOnly, PostConversionArtifactResult runArtifacts) =>
            host.BuildResult(wasCancelled, runArtifacts);

        public Task<PostConversionArtifactResult> GenerateM3uPlaylistsForCompletedChdOutputsAsync(IReadOnlyList<string> outputPaths) =>
            host.GenerateM3uAsync(outputPaths);

        // Quick profiles never rename (QTransSvc turns standard naming off for them).
        public Task ApplyAutoStandardizedNameIfEnabledAsync(TaskQueueItemViewModel item) => Task.CompletedTask;

        public void SetHasActiveQueueBindingAndSync(TaskQueueItemViewModel item, bool value) => host.SetActiveBinding(item, value);

        public bool IsRowQueuedForProcessing(Guid itemId) => host._rows.GetById(itemId) is { } row && host.IsRowQueued(row);

        public Task IngestPathsAsync(IEnumerable<string> paths, QueueIngestKind inputKind, QueueIntakeSource intakeSource) =>
            host._intake.IngestQuickPathsAsync(paths, inputKind, host._profile, intakeSource);

        public Task<IReadOnlyList<Guid>> IngestQuickPathsAsync(IEnumerable<string> paths, QueueIngestKind inputKind, QueueExecutionProfile executionProfile, QueueIntakeSource intakeSource) =>
            host._intake.IngestQuickPathsAsync(paths, inputKind, executionProfile, intakeSource);

        public TaskQueueItemViewModel? TryMaterializeOrGet(Guid id) => host.GetItem(id);

        public IReadOnlyList<Guid> EnumerateQueuedItemIds() => host.EnumerateQueuedIds();
    }

    private sealed class Session(QuickRunHost host) : IMainWindowSession
    {
        public Window Owner => host._owner() ?? Application.Current.MainWindow;

        public Dispatcher UiDispatcher => host._dispatcher;

        // Intake runs before a run starts, so only an ongoing run locks it (as in the main window).
        public bool IsQueueInteractionLocked => host.IsProcessing;

        public bool IsProcessing => host.IsProcessing;

        public bool CancellationRequested => host._coordinator.CancellationRequested;

        public bool IncludeSubfolders => host._settings().IncludeSubfolders;

        public QueueRowStore QueueRows => host._rows;

        public AppSettings GetSettings() => host._settings();

        // Quick profiles do not apply standard names, so no naming analysis is needed.
        public (bool IsCompliant, string SuggestedStandardName) AnalyzeNamingForPath(string sourcePath) => (true, string.Empty);

        public void SetFooterStatus(string message) => host.SetStatus(message);

        public void SetFooterIntakeProgress(string stageText, int scannedCount, int totalCount, int acceptedCount, bool hasKnownTotal) =>
            host.SetStatus(stageText);

        public void UpdateUiState() => host.RaiseChanged();

        public void RequestSelectFirstQueueRowIfNone()
        {
        }

        public void OnQueueClearedUi()
        {
        }

        public void ShowError(string titleResourceKey, string messageBody) =>
            host.OnUi(() => host.ErrorRaised?.Invoke(titleResourceKey, messageBody));

        // The commands below belong to the main window's queue and are not offered in the quick window.
        public void OpenExplorerForSelectedQueueItem(TaskQueueItemViewModel? item)
        {
        }

        public Task OpenOperationLogForQueueItemAsync(TaskQueueItemViewModel? item) => Task.CompletedTask;

        public bool CanOpenOperationLogTarget(TaskQueueItemViewModel? item) => false;

        public Task RunRedumpIntegrityForSelectedQueueItemAsync(TaskQueueItemViewModel? item) => Task.CompletedTask;

        public bool CanRunRedumpIntegrityForSelectedQueueItem(TaskQueueItemViewModel? item) => false;

        public Task RunRedumpIntegrityForAllQueueItemsAsync() => Task.CompletedTask;

        public bool CanRunRedumpIntegrityForAnyQueueItem() => false;

        public void CancelRedumpIntegrityScan()
        {
        }

        public bool CanCancelRedumpIntegrityScan() => false;

        public Task ShowRedumpDetails(TaskQueueItemViewModel? item) => Task.CompletedTask;

        public Task ApplyRedumpSuggestedNameAsync(TaskQueueItemViewModel? item) => Task.CompletedTask;

        public bool CanApplyRedumpSuggestedName(TaskQueueItemViewModel? item) => false;

        public bool CanOpenExplorerTarget(TaskQueueItemViewModel? item) => false;

        public void RemoveQueueItem(TaskQueueItemViewModel? item)
        {
        }

        public void RetryQueueItem(TaskQueueItemViewModel? item)
        {
        }

        public void CancelQueueJob(TaskQueueItemViewModel? item)
        {
        }

        public void OpenOptions()
        {
        }

        public void OpenAbout()
        {
        }

        public void OpenLibraryAnalysis()
        {
        }

        public void OpenRecompress()
        {
        }

        public void OpenXbox()
        {
        }
    }
}
