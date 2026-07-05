using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Features;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.Views;
using Serilog;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private async Task RunDeepIntegrityValidationAsync(
        TaskQueueItemViewModel item,
        string probePath,
        bool csoRedumpTempIsoAlreadyConfirmed = false)
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

        bool requiresCsoPreparation = string.Equals(
            Path.GetExtension(probePath),
            ".cso",
            StringComparison.OrdinalIgnoreCase);

        if (requiresCsoPreparation && !csoRedumpTempIsoAlreadyConfirmed)
        {
            bool confirmed = await ConfirmCsoRedumpTempIsoAsync().ConfigureAwait(false);
            if (!confirmed)
            {
                await InvokeOnUiIfAvailableAsync(() =>
                {
                    ApplyIntegrityAndSync(
                        item,
                        IntegrityValidationState.None,
                        ArabicUi.Get(MainWindowMessages.IntegrityCancelledDetail),
                        ArabicUi.Get(MainWindowMessages.IntegrityCancelledDetail));
                }).ConfigureAwait(false);

                return;
            }
        }

        CsoTempWorkspace? csoWorkspace = null;
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

                        string validatingMessage = requiresCsoPreparation
                            ? ArabicUi.Get("LocDeepHash_CsoStageInfo")
                            : ArabicUi.Get(MainWindowMessages.DeepIntegrityScanning);

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

            DeepHashAnalysisResult? cachedResult = null;

            if (!requiresCsoPreparation)
            {
                cachedResult = await InvokeOnUiIfAvailableAsync(
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
            }

            DeepHashAnalysisResult result;
            bool usedCachedResult = cachedResult is not null;

            if (usedCachedResult)
            {
                result = cachedResult!;
            }
            else
            {
                string effectiveProbePath = probePath;

                if (requiresCsoPreparation)
                {
                    csoWorkspace = CsoTempWorkspace.Create();

                    CsoPreprocessResult csoPreparation = await new CsoPreprocessor()
                        .PreprocessAsync(
                            probePath,
                            csoWorkspace.PreparedIsoPath,
                            cancellationToken,
                            messageKey => ApplyCsoRedumpStageAsync(item, messageKey, probePath))
                        .ConfigureAwait(false);

                    if (!csoPreparation.IsSuccess)
                    {
                        string message = ArabicUi.Get(csoPreparation.MessageKey);

                        await InvokeOnUiIfAvailableAsync(() =>
                        {
                            ApplyIntegrityAndSync(
                                item,
                                csoPreparation.WasCancelled ? IntegrityValidationState.None : IntegrityValidationState.Error,
                                message,
                                message);

                            ApplyCsoRedumpProgressAndSync(
                                item,
                                message,
                                0d,
                                isProgressActive: false,
                                isIndeterminate: false);
                        }).ConfigureAwait(false);

                        return;
                    }

                    effectiveProbePath = csoPreparation.PreparedIsoPath;

                    await ApplyCsoRedumpStageAsync(
                            item,
                            "LocDeepHash_CsoStageHashRedump",
                            effectiveProbePath)
                        .ConfigureAwait(false);
                }

                result = await DeepHashAnalyzer
                    .DeepHashAnalyzeAsync(effectiveProbePath, database, cancellationToken)
                    .ConfigureAwait(false);

                if (requiresCsoPreparation)
                {
                    await ApplyCsoRedumpStageAsync(
                            item,
                            "LocDeepHash_CsoStageSave",
                            probePath)
                        .ConfigureAwait(false);

                    SaveCsoRedumpCache(probePath, result);

                    if (csoWorkspace is not null)
                    {
                        await ApplyCsoRedumpStageAsync(
                                item,
                                "LocDeepHash_CsoStageCleanup",
                                csoWorkspace.PreparedIsoPath)
                            .ConfigureAwait(false);

                        csoWorkspace.Dispose();
                        csoWorkspace = null;
                    }
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

                if (requiresCsoPreparation)
                {
                    ApplyCsoRedumpProgressAndSync(
                        item,
                        presentation.StatusMessage,
                        100d,
                        isProgressActive: false,
                        isIndeterminate: false);
                }

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

                if (requiresCsoPreparation)
                {
                    ApplyCsoRedumpProgressAndSync(
                        item,
                        ArabicUi.Get(MainWindowMessages.IntegrityCancelledDetail),
                        0d,
                        isProgressActive: false,
                        isIndeterminate: false);
                }

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
            await ClearCsoProgressAfterErrorIfNeededAsync(item, requiresCsoPreparation).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Warning(
                ex,
                "Deep integrity validation failed due to access permissions. ProbePath={ProbePath}",
                probePath);

            await ApplyDeepIntegrityErrorAsync(item).ConfigureAwait(false);
            await ClearCsoProgressAfterErrorIfNeededAsync(item, requiresCsoPreparation).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Deep integrity validation failed unexpectedly. ProbePath={ProbePath}",
                probePath);

            await ApplyDeepIntegrityErrorAsync(item).ConfigureAwait(false);
            await ClearCsoProgressAfterErrorIfNeededAsync(item, requiresCsoPreparation).ConfigureAwait(false);
        }
        finally
        {
            if (csoWorkspace is not null)
            {
                AppendExecutionLog($"{item.FileName}: {ArabicUi.Get("LocDeepHash_CsoStageCleanup")}");
                csoWorkspace.Dispose();
            }

            if (blockingOperationRegistered)
            {
                Interlocked.Decrement(ref _blockingBackgroundOps);
                await InvokeOnUiIfAvailableAsync(UpdateUiState).ConfigureAwait(false);
            }
        }
    }

    private Task ApplyCsoRedumpStageAsync(
        TaskQueueItemViewModel item,
        string messageKey,
        string detailPath)
    {
        string message = ArabicUi.Get(messageKey);

        AppendExecutionLog($"{item.FileName}: {message}");

        return InvokeOnUiIfAvailableAsync(() =>
        {
            ApplyIntegrityAndSync(
                item,
                IntegrityValidationState.Validating,
                message,
                detailPath);

            ApplyCsoRedumpProgressAndSync(
                item,
                message,
                0d,
                isProgressActive: true,
                isIndeterminate: true);

            SetFooterStatus(message);
        });
    }

    private void ApplyCsoRedumpProgressAndSync(
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

    private Task<bool> ConfirmCsoRedumpTempIsoAsync()
    {
        return InvokeOnUiIfAvailableAsync(
            () =>
            {
                var dialog = new RedumpNoticeDialog(
                    ArabicUi.Get("LocRedump_CsoTempTitle"),
                    ArabicUi.Get("LocRedump_CsoTempBody"),
                    ArabicUi.Get("LocCommon_Cancel"),
                    ArabicUi.Get("LocRedump_CsoTempConfirm"))
                {
                    Owner = this
                };

                return dialog.ShowDialog() == true;
            },
            fallback: false);
    }

    private static bool SaveCsoRedumpCache(
        string sourceCsoPath,
        DeepHashAnalysisResult result)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourceCsoPath))
            {
                return false;
            }

            FileInfo source = new(sourceCsoPath);
            if (!source.Exists)
            {
                return false;
            }

            DeepHashFileDigest? hash = result.HashedFiles.FirstOrDefault();
            if (hash is null)
            {
                return false;
            }

            DeepHashMatch? match = result.Matches.FirstOrDefault();

            string root = AppPaths.LocalAppRoot;
            string cachePath = Path.Combine(root, "cso.json");
            DateTime savedUtc = DateTime.UtcNow;

            var cacheRecord = new
            {
                SourcePath = source.FullName,
                SourceBytes = source.Length,
                SourceModifiedUtc = source.LastWriteTimeUtc,
                NormalizedFormat = "ISO",
                TemporaryFormatUsed = "tmp.iso",
                ComputedSize = hash.SizeBytes,
                ComputedCRC32 = hash.Crc32,
                ComputedMD5 = hash.Md5,
                ComputedSHA1 = hash.Sha1,
                IsoSize = hash.SizeBytes,
                IsoMD5 = hash.Md5,
                IsoSHA1 = hash.Sha1,
                ResultState = result.State.ToString(),
                StatusKey = result.StatusMessageKey,
                RedumpSystem = match?.SystemName ?? string.Empty,
                RedumpGameName = match?.GameName ?? string.Empty,
                RedumpRomName = match?.RomName ?? string.Empty,
                RedumpMatchSource = match?.MatchSource ?? string.Empty,
                RedumpCrc = match?.Crc ?? string.Empty,
                Region = match?.Region ?? string.Empty,
                Version = match?.Version ?? string.Empty,
                SuggestedName = result.SuggestedStandardName,
                MatchedAtUtc = savedUtc,
                SavedUtc = savedUtc
            };

            List<JsonElement> records = ReadCsoCacheRecords(cachePath);
            using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(cacheRecord));
            records.Add(document.RootElement.Clone());

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            using FileStream stream = new(
                cachePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read);

            JsonSerializer.Serialize(stream, records, options);
            return true;
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException
                                  or JsonException
                                  or System.Security.SecurityException)
        {
            Log.Debug(ex, "Could not persist CSO Redump cache.");
            return false;
        }
    }

    private static List<JsonElement> ReadCsoCacheRecords(string cachePath)
    {
        var records = new List<JsonElement>();

        if (!File.Exists(cachePath))
        {
            return records;
        }

        string text = File.ReadAllText(cachePath);
        if (string.IsNullOrWhiteSpace(text))
        {
            return records;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement element in document.RootElement.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Object)
                    {
                        records.Add(element.Clone());
                    }
                }

                return records;
            }

            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                records.Add(document.RootElement.Clone());
                return records;
            }
        }
        catch (JsonException)
        {
        }

        foreach (string line in text.Split(
                     [Environment.NewLine, "\n"],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                using JsonDocument lineDocument = JsonDocument.Parse(line);
                if (lineDocument.RootElement.ValueKind == JsonValueKind.Object)
                {
                    records.Add(lineDocument.RootElement.Clone());
                }
            }
            catch (JsonException)
            {
            }
        }

        return records;
    }

    private Task ClearCsoProgressAfterErrorIfNeededAsync(
        TaskQueueItemViewModel item,
        bool requiresCsoPreparation)
    {
        if (!requiresCsoPreparation)
        {
            return Task.CompletedTask;
        }

        string message = ArabicUi.Get(MainWindowMessages.IntegrityErrorShort);
        return InvokeOnUiIfAvailableAsync(() =>
        {
            ApplyCsoRedumpProgressAndSync(
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
