using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using Serilog;
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services.Configuration;

public sealed class AppSettingsService : IDisposable
{
    private const long MaxSettingsJsonBytes = 1024L * 1024L;
    private const int DebouncedSaveDelayMs = 500;

    private static readonly ILogger Logger = global::Serilog.Log.ForContext<AppSettingsService>();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly Func<string> _configPathResolver;
    private readonly string? _fallbackConfigPath;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private CancellationTokenSource? _saveCts;
    private int _disposed;

    public AppSettingsService()
        : this(() => AppPaths.ConfigFilePath, AppPaths.LegacyConfigFilePath)
    {
    }

    public AppSettingsService(string configPath)
        : this(CreateFixedPathResolver(configPath), null)
    {
    }

    public AppSettingsService(Func<string> configPathResolver, string? fallbackConfigPath = null)
    {
        ArgumentNullException.ThrowIfNull(configPathResolver);

        _configPathResolver = configPathResolver;
        _fallbackConfigPath = string.IsNullOrWhiteSpace(fallbackConfigPath)
            ? null
            : Path.GetFullPath(fallbackConfigPath);
    }

    public string ConfigPath => ResolveConfigPath();

    public AppSettings Load()
    {
        ThrowIfDisposed();

        try
        {
            string primaryPath = ConfigPath;
            string sourcePath = ResolveReadableConfigPath(primaryPath);

            if (!File.Exists(sourcePath))
            {
                return new AppSettings();
            }

            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(
                    ReadSmallSettingsText(sourcePath),
                    JsonOptions)
                ?? new AppSettings();

            AppPaths.SetPortableMode(settings.PortableMode);

            if (!string.Equals(sourcePath, ConfigPath, StringComparison.OrdinalIgnoreCase))
            {
                Save(settings);
            }

            return settings;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "AppSettings: failed to load config; using defaults. Path={Path}", TryGetConfigPathForLog());
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        AppSettings snapshot = settings.Clone();

        // An explicit save carries the full current state; a pending debounced snapshot is older and must not overwrite it later.
        CancelPendingSave();

        try
        {
            ThrowIfDisposed();

            _saveGate.Wait();
            try
            {
                ThrowIfDisposed();
                WriteSnapshot(snapshot);
            }
            finally
            {
                _saveGate.Release();
            }
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Debug(ex, "AppSettings: save ignored because service is disposed.");
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "AppSettings: failed to save config. Path={Path}", TryGetConfigPathForLog());
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        AppSettings snapshot = settings.Clone();

        CancelPendingSave();

        try
        {
            ThrowIfDisposed();

            await _saveGate.WaitAsync().ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                await WriteSnapshotAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _saveGate.Release();
            }
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Debug(ex, "AppSettings: async save ignored because service is disposed.");
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "AppSettings: failed to save config asynchronously. Path={Path}", TryGetConfigPathForLog());
        }
    }

    public async Task SaveDebouncedAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        AppSettings snapshot = settings.Clone();
        // Ownership transfers to _saveCts and is released by replacement, completion, or service disposal.
#pragma warning disable CA2000
        CancellationTokenSource fresh = new();
#pragma warning restore CA2000
        bool registered = false;

        try
        {
            ThrowIfDisposed();

            CancellationTokenSource? previous = Interlocked.Exchange(ref _saveCts, fresh);
            registered = true;

            if (previous is not null)
            {
                await previous.CancelAsync().ConfigureAwait(false);
                previous.Dispose();
            }

            ThrowIfDisposed();

            CancellationToken token = fresh.Token;

            await Task.Delay(DebouncedSaveDelayMs, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            await SaveSnapshotAsync(snapshot, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Debug(ex, "AppSettings: debounced save ignored because service is disposed.");
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "AppSettings: failed to save config (debounced). Path={Path}", TryGetConfigPathForLog());
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _saveCts, null, fresh), fresh))
            {
                fresh.Dispose();
            }
            else if (!registered)
            {
                fresh.Dispose();
            }
        }
    }

    public void CancelPendingSave()
    {
        CancellationTokenSource? pending = Interlocked.Exchange(ref _saveCts, null);
        if (pending is null)
        {
            return;
        }

        try
        {
            pending.Cancel();
        }
        finally
        {
            pending.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        CancelPendingSave();
        _saveGate.Dispose();
    }

    private async Task SaveSnapshotAsync(AppSettings snapshot, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await WriteSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void WriteSnapshot(AppSettings snapshot)
    {
        AppPaths.SetPortableMode(snapshot.PortableMode);

        string configPath = ConfigPath;
        string? directory = Path.GetDirectoryName(configPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(snapshot, JsonOptions);
        WriteAtomically(configPath, json);

        if (snapshot.PortableMode && !string.IsNullOrWhiteSpace(_fallbackConfigPath))
        {
            TryDeleteLegacyConfig(_fallbackConfigPath);
        }
    }

    private async Task WriteSnapshotAsync(AppSettings snapshot, CancellationToken cancellationToken)
    {
        AppPaths.SetPortableMode(snapshot.PortableMode);

        string configPath = ConfigPath;
        string? directory = Path.GetDirectoryName(configPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await WriteAtomicallyAsync(configPath, json, cancellationToken).ConfigureAwait(false);

        if (snapshot.PortableMode && !string.IsNullOrWhiteSpace(_fallbackConfigPath))
        {
            TryDeleteLegacyConfig(_fallbackConfigPath);
        }
    }

    // The settings file is replaced, never truncated in place: a crash, power loss or a cancelled
    // debounced save during the write must leave the previous complete file behind.
    private static void WriteAtomically(string configPath, string json)
    {
        string stagedPath = configPath + ".tmp";

        try
        {
            WriteStaged(stagedPath, json);
            File.Move(stagedPath, configPath, overwrite: true);
        }
        catch
        {
            TryDeleteStaged(stagedPath);
            throw;
        }
    }

    private static async Task WriteAtomicallyAsync(string configPath, string json, CancellationToken cancellationToken)
    {
        string stagedPath = configPath + ".tmp";

        try
        {
            await WriteStagedAsync(stagedPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(stagedPath, configPath, overwrite: true);
        }
        catch
        {
            TryDeleteStaged(stagedPath);
            throw;
        }
    }

    // Flushed to disk before the rename so a power loss cannot leave a renamed but empty file.
    private static void WriteStaged(string stagedPath, string json)
    {
        using FileStream stream = new(stagedPath, FileMode.Create, FileAccess.Write, FileShare.None);
        byte[] bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(json)).ToArray();
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static async Task WriteStagedAsync(string stagedPath, string json, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(stagedPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        byte[] bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(json)).ToArray();
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static void TryDeleteStaged(string stagedPath)
    {
        try
        {
            File.Delete(stagedPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Debug(ex, "AppSettings: failed to remove staged settings file. Path={Path}", stagedPath);
        }
    }

    private string ResolveReadableConfigPath(string primaryPath)
    {
        if (File.Exists(primaryPath))
        {
            return primaryPath;
        }

        if (!string.IsNullOrWhiteSpace(_fallbackConfigPath) && File.Exists(_fallbackConfigPath))
        {
            return _fallbackConfigPath;
        }

        return primaryPath;
    }

    private string ResolveConfigPath()
    {
        string path = _configPathResolver();

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("AppSettings config path resolver returned an empty path.");
        }

        return Path.GetFullPath(path);
    }

    private string TryGetConfigPathForLog()
    {
        try
        {
            return ConfigPath;
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private static Func<string> CreateFixedPathResolver(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        string fullPath = Path.GetFullPath(configPath);
        return () => fullPath;
    }

    private static void TryDeleteLegacyConfig(string legacyPath)
    {
        try
        {
            if (File.Exists(legacyPath))
            {
                File.Delete(legacyPath);
            }
        }
        catch (Exception ex) when (
            ex is IOException ||
            ex is UnauthorizedAccessException)
        {
            Logger.Debug(ex, "AppSettings: failed to delete legacy config. Path={Path}", legacyPath);
        }
    }

    private static string ReadSmallSettingsText(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        FileInfo fileInfo = new(path);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Required text file was not found.", path);
        }

        if (fileInfo.Length > MaxSettingsJsonBytes)
        {
            throw new InvalidOperationException("Settings JSON file is too large to read as a bounded text descriptor.");
        }

        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using StreamReader reader = new(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: false);

        return reader.ReadToEnd();
    }
}