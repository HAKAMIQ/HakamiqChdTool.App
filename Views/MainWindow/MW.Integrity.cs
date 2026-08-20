using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Features;
using HakamiqChdTool.App.ViewModels;
using Serilog;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private sealed class AsyncProgressQueue<T> : IProgress<T>
    {
        private readonly Func<T, Task> _handler;
        private readonly object _syncRoot = new();
        private Task _tail = Task.CompletedTask;
        private bool _isCompleted;

        public AsyncProgressQueue(Func<T, Task> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public void Report(T value)
        {
            lock (_syncRoot)
            {
                if (_isCompleted)
                {
                    return;
                }

                _tail = AppendAsync(_tail, value);
            }
        }

        public Task CompleteAsync()
        {
            lock (_syncRoot)
            {
                _isCompleted = true;
                return _tail;
            }
        }

        private async Task AppendAsync(Task previous, T value)
        {
            await previous.ConfigureAwait(false);
            await _handler(value).ConfigureAwait(false);
        }
    }

    private async Task RunDeepIntegrityValidationAsync(
        TaskQueueItemViewModel item,
        string probePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (string.IsNullOrWhiteSpace(probePath))
        {
            await InvokeOnUiIfAvailableAsync(() =>
            {
                ApplyIntegrityAndSync(
                    item,
                    IntegrityValidationState.Error,
                    ArabicUi.Get(MainWindowMessages.IntegrityErrorShort),
                    ArabicUi.Get(MainWindowMessages.IntegrityNoDiskFileBody));

                ApplyRedumpProgressAndSync(
                    item,
                    RedumpOperationState.Failed,
                    ArabicUi.Get(MainWindowMessages.IntegrityErrorShort),
                    0d,
                    isIndeterminate: false,
                    currentBytes: 0L,
                    totalBytes: 0L,
                    bytesPerSecond: 0d,
                    eta: null);
            }).ConfigureAwait(false);

            return;
        }

        if (!_settings.EnableDeepIntegrityCheck ||
            !_appFeatureService.IsEnabled(AppFeature.RedumpDeepIntegrity))
        {
            await InvokeOnUiIfAvailableAsync(() =>
            {
                ApplyIntegrityAndSync(
                    item,
                    IntegrityValidationState.None,
                    ArabicUi.Get(MainWindowMessages.DeepIntegrityDisabledShort),
                    ArabicUi.Get(MainWindowMessages.DeepIntegrityDisabledDetail));

                ApplyRedumpProgressAndSync(
                    item,
                    RedumpOperationState.Idle,
                    ArabicUi.Get(MainWindowMessages.DeepIntegrityDisabledShort),
                    0d,
                    isIndeterminate: false,
                    currentBytes: 0L,
                    totalBytes: 0L,
                    bytesPerSecond: 0d,
                    eta: null);
            }).ConfigureAwait(false);

            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        bool blockingOperationRegistered = false;

        try
        {
            Interlocked.Increment(ref _blockingBackgroundOps);
            blockingOperationRegistered = true;

            await InvokeOnUiIfAvailableAsync(UpdateUiState).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            string probeKey = FilePathExclusiveGate.NormalizePathForExclusiveLock(probePath);

            await using IAsyncDisposable exclusivePathLease = await FilePathExclusiveGate
                .AcquireAsync(probeKey, cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            bool proceed = await InvokeOnUiIfAvailableAsync(
                    () =>
                    {
                        if (item.IsRedumpOperationActive)
                        {
                            return false;
                        }

                        string validatingMessage = ArabicUi.Get(MainWindowMessages.DeepIntegrityScanning);

                        ApplyIntegrityAndSync(
                            item,
                            IntegrityValidationState.Validating,
                            validatingMessage,
                            probePath);

                        ApplyRedumpProgressAndSync(
                            item,
                            RedumpOperationState.Queued,
                            validatingMessage,
                            0d,
                            isIndeterminate: true,
                            currentBytes: 0L,
                            totalBytes: 0L,
                            bytesPerSecond: 0d,
                            eta: null);

                        return true;
                    },
                    fallback: false)
                .ConfigureAwait(false);

            if (!proceed)
            {
                return;
            }

            RedumpSqliteManager database = RedumpSqliteManager.Default;

            await Task.Run(
                    () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        database.EnsureInitialized();
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            DeepHashAnalysisResult? cachedResult = await InvokeOnUiIfAvailableAsync(
                    () =>
                    {
                        if (string.Equals(item.DeepHashCachePath, probeKey, StringComparison.OrdinalIgnoreCase))
                        {
                            return item.DeepHashCachedResult;
                        }

                        return null;
                    },
                    fallback: null)
                .ConfigureAwait(false);

            DeepHashAnalysisResult result;
            bool usedCachedResult = cachedResult is not null;

            if (usedCachedResult)
            {
                result = cachedResult!;
            }
            else
            {
                var redumpProgress = new AsyncProgressQueue<ProgressEvent>(
                    progressEvent => ApplyRedumpProgressEventAsync(item, progressEvent, probePath));

                try
                {
                    result = await DeepHashAnalyzer
                        .DeepHashAnalyzeAsync(
                            probePath,
                            database,
                            cancellationToken,
                            new RedumpV2ScanOptions(GetChdmanPath(), _settings),
                            redumpProgress)
                        .ConfigureAwait(false);
                }
                finally
                {
                    await redumpProgress.CompleteAsync().ConfigureAwait(false);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            await InvokeOnUiIfAvailableAsync(() =>
            {
                if (!usedCachedResult)
                {
                    item.DeepHashCachePath = probeKey;
                    item.DeepHashCachedResult = result;
                }

                ApplyRedumpResultAndSync(item, result);

                DeepHashAnalysisView presentation = DeepHashAnalysisPresenter.Format(result);

                ApplyRedumpProgressAndSync(
                    item,
                    RedumpOperationState.Completed,
                    presentation.StatusMessage,
                    100d,
                    isIndeterminate: false,
                    currentBytes: 0L,
                    totalBytes: 0L,
                    bytesPerSecond: 0d,
                    eta: null);

                SetFooterStatus(ArabicUi.Format(
                    MainWindowMessages.Fmt_DeepIntegrityDone,
                    presentation.StatusMessage));
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            Log.Debug(
                ex,
                "Deep integrity validation was cancelled. ProbePath={ProbePath}",
                probePath);

            await InvokeOnUiIfAvailableAsync(() =>
            {
                ApplyIntegrityAndSync(
                    item,
                    IntegrityValidationState.None,
                    ArabicUi.Get(MainWindowMessages.IntegrityCancelledDetail),
                    ArabicUi.Get(MainWindowMessages.IntegrityCancelledDetail));

                ApplyRedumpProgressAndSync(
                    item,
                    RedumpOperationState.Idle,
                    ArabicUi.Get(MainWindowMessages.IntegrityCancelledDetail),
                    0d,
                    isIndeterminate: false,
                    currentBytes: 0L,
                    totalBytes: 0L,
                    bytesPerSecond: 0d,
                    eta: null);

                SetFooterStatus(ArabicUi.Get(MainWindowMessages.IntegrityCancelledDetail));
            }).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            Log.Warning(
                ex,
                "Deep integrity validation failed due to an I/O error. ProbePath={ProbePath}",
                probePath);

            await ApplyDeepIntegrityErrorAsync(item).ConfigureAwait(false);
            await ClearRedumpProgressAfterErrorAsync(item).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Warning(
                ex,
                "Deep integrity validation failed due to access permissions. ProbePath={ProbePath}",
                probePath);

            await ApplyDeepIntegrityErrorAsync(item).ConfigureAwait(false);
            await ClearRedumpProgressAfterErrorAsync(item).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Deep integrity validation failed unexpectedly. ProbePath={ProbePath}",
                probePath);

            await ApplyDeepIntegrityErrorAsync(item).ConfigureAwait(false);
            await ClearRedumpProgressAfterErrorAsync(item).ConfigureAwait(false);
        }
        finally
        {
            if (blockingOperationRegistered)
            {
                Interlocked.Decrement(ref _blockingBackgroundOps);
                await InvokeOnUiIfAvailableAsync(UpdateUiState).ConfigureAwait(false);
            }
        }
    }

    private Task ApplyRedumpProgressEventAsync(
        TaskQueueItemViewModel item,
        ProgressEvent progressEvent,
        string detailPath)
    {
        string message = ArabicUi.Get(progressEvent.MessageKey);
        if (string.IsNullOrWhiteSpace(message))
        {
            message = progressEvent.MessageKey;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            message = ArabicUi.Get(MainWindowMessages.DeepIntegrityScanning);
        }

        double overallProgress = CalculateRedumpOverallProgress(progressEvent);
        RedumpOperationState redumpState = ResolveRedumpOperationState(progressEvent);

        bool isIndeterminate =
            progressEvent.CurrentStep <= 1 &&
            progressEvent.TotalBytes <= 0 &&
            progressEvent.Percent <= 0;

        string progressStatus = BuildRedumpProgressStatus(
            message,
            progressEvent,
            overallProgress,
            isIndeterminate);

        AppendExecutionLog($"{item.FileName}: {progressStatus}");

        return InvokeOnUiIfAvailableAsync(() =>
        {
            ApplyIntegrityAndSync(
                item,
                IntegrityValidationState.Validating,
                progressStatus,
                detailPath);

            bool hasByteMetrics =
                progressEvent.TotalBytes > 0L &&
                progressEvent.OperationType is
                    ProgressOperationType.Hashing or
                    ProgressOperationType.TemporaryNormalization;

            long displayedCurrentBytes = progressEvent.CurrentBytes;

            if (hasByteMetrics &&
                displayedCurrentBytes <= 0L &&
                progressEvent.Percent > 0d)
            {
                displayedCurrentBytes = Math.Min(
                    progressEvent.TotalBytes,
                    (long)Math.Round(
                        progressEvent.TotalBytes *
                        Math.Clamp(progressEvent.Percent, 0d, 100d) /
                        100d));
            }

            bool hasRateMetrics =
                progressEvent.OperationType == ProgressOperationType.Hashing;

            ApplyRedumpProgressAndSync(
                item,
                redumpState,
                message,
                overallProgress,
                isIndeterminate,
                hasByteMetrics ? displayedCurrentBytes : 0L,
                hasByteMetrics ? progressEvent.TotalBytes : 0L,
                hasRateMetrics ? progressEvent.SpeedBytesPerSecond : 0d,
                hasRateMetrics ? progressEvent.Eta : null);

            SetFooterStatus(progressStatus);
        });
    }

    private static RedumpOperationState ResolveRedumpOperationState(
        ProgressEvent progressEvent)
    {
        if (progressEvent.OperationType == ProgressOperationType.Hashing ||
            progressEvent.CurrentStep == 3)
        {
            return RedumpOperationState.Hashing;
        }

        if (progressEvent.CurrentStep >= 4)
        {
            return RedumpOperationState.Matching;
        }

        return RedumpOperationState.Queued;
    }

    private static double CalculateRedumpOverallProgress(ProgressEvent progressEvent)
    {
        double stepPercent = Math.Clamp(progressEvent.Percent, 0d, 100d) / 100d;

        return progressEvent.CurrentStep switch
        {
            1 => Math.Clamp(stepPercent * 2d, 0d, 2d),
            2 => Math.Clamp(2d + (stepPercent * 33d), 2d, 35d),
            3 => Math.Clamp(35d + (stepPercent * 60d), 35d, 95d),
            4 => 96d,
            5 => 98d,
            6 => Math.Clamp(99d + stepPercent, 99d, 100d),
            _ => Math.Clamp(progressEvent.Percent, 0d, 100d)
        };
    }

    private static string BuildRedumpProgressStatus(
        string message,
        ProgressEvent progressEvent,
        double overallProgress,
        bool isIndeterminate)
    {
        var parts = new List<string>
        {
            message
        };

        if (progressEvent.OperationType == ProgressOperationType.Hashing &&
            progressEvent.TotalBytes > 0)
        {
            parts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{Math.Clamp(progressEvent.Percent, 0d, 100d):0}%"));

            parts.Add(
                $"{FormatProgressBytes(progressEvent.CurrentBytes)} / {FormatProgressBytes(progressEvent.TotalBytes)}");

            if (progressEvent.SpeedBytesPerSecond > 0)
            {
                parts.Add($"{FormatProgressBytes(progressEvent.SpeedBytesPerSecond)}/s");
            }

            if (progressEvent.Eta is { } eta && eta > TimeSpan.Zero)
            {
                parts.Add($"ETA {FormatProgressEta(eta)}");
            }
        }
        else if (!isIndeterminate)
        {
            parts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{Math.Clamp(overallProgress, 0d, 100d):0}%"));
        }

        return string.Join("  •  ", parts);
    }

    private static string FormatProgressBytes(double bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unitIndex = 0;

        while (value >= 1024d && unitIndex < units.Length - 1)
        {
            value /= 1024d;
            unitIndex++;
        }

        return unitIndex == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{value:0} {units[unitIndex]}")
            : string.Create(CultureInfo.InvariantCulture, $"{value:0.##} {units[unitIndex]}");
    }

    private static string FormatProgressEta(TimeSpan eta)
    {
        TimeSpan normalized = eta < TimeSpan.Zero
            ? TimeSpan.Zero
            : eta;

        if (normalized.TotalHours >= 1d)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{(int)normalized.TotalHours:00}:{normalized.Minutes:00}:{normalized.Seconds:00}");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{normalized.Minutes:00}:{normalized.Seconds:00}");
    }

    private void ApplyRedumpProgressAndSync(
        TaskQueueItemViewModel item,
        RedumpOperationState state,
        string statusDetail,
        double progress,
        bool isIndeterminate,
        long currentBytes,
        long totalBytes,
        double bytesPerSecond,
        TimeSpan? eta)
    {
        double normalizedProgress = Math.Clamp(progress, 0d, 100d);
        long normalizedCurrentBytes = Math.Max(0L, currentBytes);
        long normalizedTotalBytes = Math.Max(0L, totalBytes);
        double normalizedBytesPerSecond = double.IsFinite(bytesPerSecond)
            ? Math.Max(0d, bytesPerSecond)
            : 0d;
        long normalizedEtaTicks = Math.Max(0L, eta?.Ticks ?? 0L);

        item.SetRedumpProgress(
            state,
            statusDetail,
            normalizedProgress,
            isIndeterminate,
            normalizedCurrentBytes,
            normalizedTotalBytes,
            normalizedBytesPerSecond,
            normalizedEtaTicks > 0L
                ? TimeSpan.FromTicks(normalizedEtaTicks)
                : null);

        _queueRowStore.Mutate(item.QueueItemId, row =>
        {
            row.RedumpState = state;
            row.RedumpStatusText = statusDetail;
            row.RedumpProgress = normalizedProgress;
            row.RedumpIsIndeterminate = isIndeterminate;
            row.RedumpCurrentBytes = normalizedCurrentBytes;
            row.RedumpTotalBytes = normalizedTotalBytes;
            row.RedumpBytesPerSecond = normalizedBytesPerSecond;
            row.RedumpEtaTicks = normalizedEtaTicks;
        });

        RequestUiStateRefresh();
    }

    private Task ClearRedumpProgressAfterErrorAsync(TaskQueueItemViewModel item)
    {
        string message = ArabicUi.Get(MainWindowMessages.IntegrityErrorShort);

        return InvokeOnUiIfAvailableAsync(() =>
        {
            ApplyRedumpProgressAndSync(
                item,
                RedumpOperationState.Failed,
                message,
                0d,
                isIndeterminate: false,
                currentBytes: 0L,
                totalBytes: 0L,
                bytesPerSecond: 0d,
                eta: null);
        });
    }

    private Task ApplyDeepIntegrityErrorAsync(TaskQueueItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return InvokeOnUiIfAvailableAsync(() =>
        {
            ApplyIntegrityAndSync(
                item,
                IntegrityValidationState.Error,
                ArabicUi.Get(MainWindowMessages.IntegrityErrorShort),
                ArabicUi.Get("LocDeepHash_TipHashFailed"));
        });
    }

    private async Task InvokeOnUiIfAvailableAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        try
        {
            await Dispatcher.InvokeAsync(action);
        }
        catch (TaskCanceledException) when (
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
        {
        }
        catch (InvalidOperationException) when (
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
        {
        }
    }

    private async Task<T> InvokeOnUiIfAvailableAsync<T>(
        Func<T> action,
        T fallback)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return fallback;
        }

        if (Dispatcher.CheckAccess())
        {
            return action();
        }

        try
        {
            return await Dispatcher.InvokeAsync(action);
        }
        catch (TaskCanceledException) when (
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
        {
            return fallback;
        }
        catch (InvalidOperationException) when (
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
        {
            return fallback;
        }
    }
}