using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using System.Text;
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

        string key = CanonicalGateKey(NormalizePathForExclusiveLock(path));
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

    /// <summary>
    /// Lock key only (never passed to a CLI): folds the aliases of one file onto one key, namely the
    /// extended-length prefix and the 8.3 short names of the existing part of the path.
    /// </summary>
    internal static string CanonicalGateKey(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return fullPath;
        }

        string key = fullPath;
        if (key.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            key = @"\\" + key[8..];
        }
        else if (key.StartsWith(@"\\?\", StringComparison.Ordinal) || key.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            key = key[4..];
        }

        if (key.IndexOf('~') < 0)
        {
            return key;
        }

        try
        {
            // Resolve the longest existing prefix; the remainder (a file not created yet) is appended as is.
            string existing = key;
            string tail = string.Empty;
            while (!string.IsNullOrEmpty(existing) && !File.Exists(existing) && !Directory.Exists(existing))
            {
                string? parent = Path.GetDirectoryName(existing);
                if (string.IsNullOrEmpty(parent))
                {
                    return key;
                }

                tail = string.IsNullOrEmpty(tail)
                    ? Path.GetFileName(existing)
                    : Path.Combine(Path.GetFileName(existing), tail);
                existing = parent;
            }

            if (string.IsNullOrEmpty(existing))
            {
                return key;
            }

            StringBuilder buffer = new(1024);
            uint written = GetLongPathNameW(existing, buffer, (uint)buffer.Capacity);
            if (written == 0 || written >= buffer.Capacity)
            {
                return key;
            }

            string resolved = buffer.ToString();
            return string.IsNullOrEmpty(tail) ? resolved : Path.Combine(resolved, tail);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return key;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathNameW(string shortPath, StringBuilder longPath, uint bufferLength);

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