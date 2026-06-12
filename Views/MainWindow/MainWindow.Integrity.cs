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

                        ApplyIntegrityAndSync(
                            item,
                            IntegrityValidationState.Validating,
                            ArabicUi.Get(MainWindowMessages.DeepIntegrityScanning),
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
                result = await DeepHashAnalyzer
                    .DeepHashAnalyzeAsync(probeKey, database, cancellationToken)
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
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Warning(
                ex,
                "Deep integrity validation failed due to access permissions. ProbePath={ProbePath}",
                probePath);

            await ApplyDeepIntegrityErrorAsync(item).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Deep integrity validation failed unexpectedly. ProbePath={ProbePath}",
                probePath);

            await ApplyDeepIntegrityErrorAsync(item).ConfigureAwait(false);
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