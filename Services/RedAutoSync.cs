using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

internal sealed class RedumpAutoSyncStartupService
{
    private const int MinimumSyncIntervalHours = 168;
    private const int FailureBackoffHours = 12;

    private static readonly TimeSpan StartupSyncTimeout = TimeSpan.FromSeconds(8);
    private static readonly ILogger Logger = Log.ForContext<RedumpAutoSyncStartupService>();

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Preserved as an instance service method to avoid changing service call sites and DI-facing behavior.")]
    public bool ShouldRun(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.EnableRedumpAutoSync)
        {
            return false;
        }

        if (IsBackoffActive(settings.RedumpAutoSyncBackoffUntilUtc, out DateTimeOffset backoffUntilUtc))
        {
            Logger.Debug(
                "Redump startup auto-sync skipped because failure backoff is active. BackoffUntilUtc={BackoffUntilUtc}",
                backoffUntilUtc);
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.RedumpLastSyncedUtc))
        {
            return true;
        }

        if (!DateTimeOffset.TryParse(
            settings.RedumpLastSyncedUtc,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset lastSyncedUtc))
        {
            return true;
        }

        return DateTimeOffset.UtcNow - lastSyncedUtc.ToUniversalTime() >= TimeSpan.FromHours(MinimumSyncIntervalHours);
    }

    public async Task<RedumpAutoSyncStartupResult> TrySyncAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!ShouldRun(settings))
        {
            return RedumpAutoSyncStartupResult.SkippedRecent;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(StartupSyncTimeout);

            using HttpClient httpClient = new()
            {
                Timeout = StartupSyncTimeout
            };

            using RedumpGitHubSyncManager syncManager = new(httpClient);
            RedumpGitHubSyncResult syncResult = await syncManager
                .SyncFromGitHubAsync(settings.RedumpDatabaseDownloadUrl, progress: null, timeoutCts.Token)
                .ConfigureAwait(false);

            if (!syncResult.Success)
            {
                Logger.Debug(
                    "Redump auto-sync skipped or failed. MessageKey={MessageKey}",
                    syncResult.MessageKey);

                return RedumpAutoSyncStartupResult.Failed(syncResult.MessageKey, BuildFailureBackoffUntilUtc());
            }

            return RedumpAutoSyncStartupResult.Completed(syncResult.ImportedSystems, syncResult.SyncedAtUtc);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            Logger.Debug(ex, "Redump auto-sync timed out during startup.");
            return RedumpAutoSyncStartupResult.Failed("LocRedumpAutoSync_FailedFooter", BuildFailureBackoffUntilUtc());
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or UnauthorizedAccessException
            or System.IO.IOException
            or HttpRequestException
            or System.Text.Json.JsonException
            or System.IO.InvalidDataException)
        {
            Logger.Debug(ex, "Redump auto-sync failed during startup.");
            return RedumpAutoSyncStartupResult.Failed("LocRedumpAutoSync_FailedFooter", BuildFailureBackoffUntilUtc());
        }
    }

    private static DateTimeOffset BuildFailureBackoffUntilUtc() =>
        DateTimeOffset.UtcNow.AddHours(FailureBackoffHours);

    private static bool IsBackoffActive(string? backoffUntilUtcValue, out DateTimeOffset backoffUntilUtc)
    {
        backoffUntilUtc = default;

        if (string.IsNullOrWhiteSpace(backoffUntilUtcValue))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(
            backoffUntilUtcValue,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset parsed))
        {
            return false;
        }

        backoffUntilUtc = parsed.ToUniversalTime();
        return DateTimeOffset.UtcNow < backoffUntilUtc;
    }
}

internal sealed record RedumpAutoSyncStartupResult(
    bool Success,
    bool Skipped,
    int ImportedSystems,
    DateTimeOffset? SyncedAtUtc,
    DateTimeOffset? BackoffUntilUtc,
    string MessageKey)
{
    public static RedumpAutoSyncStartupResult SkippedRecent { get; } =
        new(false, true, 0, null, null, "LocRedumpAutoSync_SkippedRecentFooter");

    public static RedumpAutoSyncStartupResult Completed(int importedSystems, DateTimeOffset syncedAtUtc) =>
        new(true, false, importedSystems, syncedAtUtc, null, "LocRedumpAutoSync_CompletedFooter");

    public static RedumpAutoSyncStartupResult Failed(string messageKey, DateTimeOffset backoffUntilUtc) =>
        new(false, false, 0, null, backoffUntilUtc, string.IsNullOrWhiteSpace(messageKey) ? "LocRedumpAutoSync_FailedFooter" : messageKey);
}
