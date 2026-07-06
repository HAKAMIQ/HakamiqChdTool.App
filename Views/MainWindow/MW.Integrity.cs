using System;
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
    private async Task RunDeepIntegrityValidationAsync(
        TaskQueueItemViewModel item,
        string probePath)
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
            }).ConfigureAwait(false);

            return;
        }

        CancellationToken cancellationToken = _windowLifetimeCts.Token;

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
                        if (item.IntegrityState == IntegrityValidationState.Validating)
                        {
                            return false;
                        }

                        string validatingMessage = ArabicUi.Get(MainWindowMessages.DeepIntegrityScanning);

                        ApplyIntegrityAndSync(
                            item,
                            IntegrityValidationState.Validating,
                            validatingMessage,
                            probePath);

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

            if (!database.HasAnyRows())
            {
                await InvokeOnUiIfAvailableAsync(() =>
                {
                    ApplyIntegrityAndSync(
                        item,
                        IntegrityValidationState.NoDat,
                        ArabicUi.Get(MainWindowMessages.DeepIntegrityNoDatShort),
                        ArabicUi.Get(MainWindowMessages.DeepIntegrityNoDatDetail));
                }).ConfigureAwait(false);

                return;
            }

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
                var redumpProgress = new Progress<ProgressEvent>(
                    progressEvent => _ = ApplyRedumpProgressEventAsync(item, progressEvent, probePath));

                result = await DeepHashAnalyzer
                    .DeepHashAnalyzeAsync(
                        probePath,
                        database,
                        cancellationToken,
                        new RedumpV2ScanOptions(GetChdmanPath(), _settings),
                        redumpProgress)
                    .ConfigureAwait(false);
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
                    presentation.StatusMessage,
                    100d,
                    isProgressActive: false,
                    isIndeterminate: false);

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
                    ArabicUi.Get(MainWindowMessages.IntegrityCancelledDetail),
                    0d,
                    isProgressActive: false,
                    isIndeterminate: false);

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
        bool isIndeterminate = progressEvent.TotalBytes <= 0
            && progressEvent.Percent <= 0
            && progressEvent.OperationType is ProgressOperationType.TemporaryNormalization or ProgressOperationType.RedumpScan;

        AppendExecutionLog($"{item.FileName}: {message}");

        return InvokeOnUiIfAvailableAsync(() =>
        {
            ApplyIntegrityAndSync(
                item,
                IntegrityValidationState.Validating,
                message,
                detailPath);

            ApplyRedumpProgressAndSync(
                item,
                message,
                overallProgress,
                isProgressActive: true,
                isIndeterminate: isIndeterminate);

            SetFooterStatus(message);
        });
    }

    private static double CalculateRedumpOverallProgress(ProgressEvent progressEvent)
    {
        if (progressEvent.TotalSteps <= 0 || progressEvent.CurrentStep <= 0)
        {
            return Math.Clamp(progressEvent.Percent, 0d, 100d);
        }

        double stepSize = 100d / progressEvent.TotalSteps;
        double completedSteps = Math.Clamp(progressEvent.CurrentStep - 1, 0, progressEvent.TotalSteps) * stepSize;
        double currentStepProgress = Math.Clamp(progressEvent.Percent, 0d, 100d) / 100d * stepSize;
        return Math.Clamp(completedSteps + currentStepProgress, 0d, 99d);
    }

    private void ApplyRedumpProgressAndSync(
        TaskQueueItemViewModel item,
        string statusDetail,
        double progress,
        bool isProgressActive,
        bool isIndeterminate)
    {
        double normalizedProgress = Math.Clamp(progress, 0d, 100d);

        item.StatusDetail = statusDetail;
        item.ProgressValue = normalizedProgress;
        item.IsProgressActive = isProgressActive;
        item.IsIndeterminate = isIndeterminate;

        _queueRowStore.Mutate(item.QueueItemId, row =>
        {
            row.StatusDetail = statusDetail;
            row.Progress = normalizedProgress;
            row.IsProgressActive = isProgressActive;
            row.IsIndeterminate = isIndeterminate;
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
                message,
                0d,
                isProgressActive: false,
                isIndeterminate: false);
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
        catch (TaskCanceledException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
        }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
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
        catch (TaskCanceledException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return fallback;
        }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return fallback;
        }
    }
}
