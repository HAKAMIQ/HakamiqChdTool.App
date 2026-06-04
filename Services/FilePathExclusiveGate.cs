using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public static class FilePathExclusiveGate
{
    private static readonly ILogger Logger = global::Serilog.Log.ForContext(typeof(FilePathExclusiveGate));

    private static readonly ConcurrentDictionary<string, GateEntry> Gates =
        new(StringComparer.OrdinalIgnoreCase);

    public static string NormalizePathForExclusiveLock(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path ?? string.Empty;
        }

        return NormalizePath(path);
    }

    public static async Task RunAsync(
        string path,
        Func<Task> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using IAsyncDisposable lease = await AcquireAsync(path, cancellationToken).ConfigureAwait(false);
        await action().ConfigureAwait(false);
    }

    public static async Task<IAsyncDisposable> AcquireAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return NoopRelease.Instance;
        }

        string key = NormalizePathForExclusiveLock(path);
        if (string.IsNullOrWhiteSpace(key))
        {
            return NoopRelease.Instance;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            GateEntry entry = Gates.GetOrAdd(key, static _ => new GateEntry());

            if (!entry.TryAddReference())
            {
                Gates.TryRemove(new KeyValuePair<string, GateEntry>(key, entry));
                continue;
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new Release(key, entry);
            }
            catch
            {
                ReleaseReference(key, entry);
                throw;
            }
        }
    }

    private static string NormalizePath(string path)
    {
        string trimmed = path.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (ArgumentException ex)
        {
            Logger.Debug(ex, "File path gate normalization failed because the path contains invalid characters. Path={Path}", trimmed);
            return trimmed;
        }
        catch (NotSupportedException ex)
        {
            Logger.Debug(ex, "File path gate normalization failed because the path format is not supported. Path={Path}", trimmed);
            return trimmed;
        }
        catch (PathTooLongException ex)
        {
            Logger.Debug(ex, "File path gate normalization failed because the path is too long. Path={Path}", trimmed);
            return trimmed;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Debug(ex, "File path gate normalization failed because access to the path was denied. Path={Path}", trimmed);
            return trimmed;
        }
    }

    private static void ReleaseReference(string key, GateEntry entry)
    {
        int remainingReferences = entry.ReleaseReference(out bool shouldDispose);

        if (remainingReferences < 0)
        {
            Logger.Warning("File path gate reference count dropped below zero. Key={Key}", key);
            return;
        }

        if (!shouldDispose)
        {
            return;
        }

        Gates.TryRemove(new KeyValuePair<string, GateEntry>(key, entry));
        entry.Dispose();
    }

    private sealed class GateEntry : IDisposable
    {
        private readonly object _syncRoot = new();
        private int _referenceCount;
        private bool _retired;
        private bool _disposed;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public bool TryAddReference()
        {
            lock (_syncRoot)
            {
                if (_retired || _disposed)
                {
                    return false;
                }

                _referenceCount = checked(_referenceCount + 1);
                return true;
            }
        }

        public int ReleaseReference(out bool shouldDispose)
        {
            lock (_syncRoot)
            {
                shouldDispose = false;

                if (_referenceCount <= 0)
                {
                    _referenceCount--;
                    return _referenceCount;
                }

                _referenceCount--;

                if (_referenceCount == 0)
                {
                    _retired = true;
                    shouldDispose = true;
                }

                return _referenceCount;
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _retired = true;
                _disposed = true;
            }

            Semaphore.Dispose();
        }
    }

    private sealed class Release : IAsyncDisposable, IDisposable
    {
        private readonly string _key;
        private GateEntry? _entry;

        public Release(string key, GateEntry entry)
        {
            _key = key;
            _entry = entry;
        }

        public ValueTask DisposeAsync()
        {
            ReleaseCore();
            return ValueTask.CompletedTask;
        }

        public void Dispose() => ReleaseCore();

        private void ReleaseCore()
        {
            GateEntry? entry = Interlocked.Exchange(ref _entry, null);
            if (entry is null)
            {
                return;
            }

            try
            {
                entry.Semaphore.Release();
            }
            catch (ObjectDisposedException ex)
            {
                Logger.Debug(ex, "File path gate semaphore was already disposed before release. Key={Key}", _key);
            }
            catch (SemaphoreFullException ex)
            {
                Logger.Warning(ex, "File path gate semaphore release exceeded its maximum count. Key={Key}", _key);
            }
            finally
            {
                ReleaseReference(_key, entry);
            }
        }
    }

    private sealed class NoopRelease : IAsyncDisposable, IDisposable
    {
        public static readonly NoopRelease Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }
}