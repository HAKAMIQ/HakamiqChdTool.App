using HakamiqChdTool.App.Core.Input;
using Serilog;
using System.IO;
using System.Threading;

namespace HakamiqChdTool.App.Services.Watch;

/// <summary>
/// Decides when a new file is complete: its size has not changed for the quiet period and it can be opened
/// with no sharing, so no other program is still writing it. Each file is reported once until it is deleted.
/// </summary>
internal sealed class FileStabilityTracker
{
    private readonly TimeSpan _quietPeriod;
    private readonly Dictionary<string, (long Size, DateTime LastChangeUtc)> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reported = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public FileStabilityTracker(TimeSpan quietPeriod) => _quietPeriod = quietPeriod;

    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    public void Observe(string path, DateTime nowUtc)
    {
        lock (_gate)
        {
            if (!_reported.Contains(path) && !_pending.ContainsKey(path))
            {
                _pending[path] = (-1, nowUtc);
            }
        }
    }

    public void Forget(string path)
    {
        lock (_gate)
        {
            _pending.Remove(path);
            _reported.Remove(path);
        }
    }

    /// <param name="sizeOf">The current size, or null when the file is gone.</param>
    public IReadOnlyList<string> TakeReady(DateTime nowUtc, Func<string, long?> sizeOf, Func<string, bool> canOpenExclusively)
    {
        ArgumentNullException.ThrowIfNull(sizeOf);
        ArgumentNullException.ThrowIfNull(canOpenExclusively);
        var ready = new List<string>();
        lock (_gate)
        {
            foreach (string path in _pending.Keys.ToList())
            {
                (long size, DateTime lastChange) = _pending[path];
                long? current = sizeOf(path);
                if (current is null)
                {
                    _pending.Remove(path);
                    continue;
                }

                if (current.Value != size)
                {
                    _pending[path] = (current.Value, nowUtc);
                    continue;
                }

                if (nowUtc - lastChange >= _quietPeriod && current.Value > 0 && canOpenExclusively(path))
                {
                    _pending.Remove(path);
                    _reported.Add(path);
                    ready.Add(path);
                }
            }
        }

        return ready;
    }
}

/// <summary>
/// Watches one folder (not its subfolders) for new disc images and archives and reports each once it is
/// complete. CHD files, hidden or system files and partial downloads are ignored, so the app's own output in
/// that folder is never picked up again.
/// </summary>
internal sealed class WatchFolderService : IDisposable
{
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly string[] PartialSuffixes = [".tmp", ".part", ".partial", ".crdownload", ".download", ".!qb"];

    private readonly FileSystemWatcher _watcher;
    private readonly FileStabilityTracker _tracker;
    private readonly Action<IReadOnlyList<string>> _onReady;
    private readonly Timer _timer;

    private WatchFolderService(string folder, Action<IReadOnlyList<string>> onReady, TimeSpan quietPeriod, TimeSpan pollInterval)
    {
        Folder = folder;
        _onReady = onReady;
        _tracker = new FileStabilityTracker(quietPeriod);
        _watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite
        };
        _watcher.Created += (_, e) => Observe(e.FullPath);
        _watcher.Changed += (_, e) => Observe(e.FullPath);
        _watcher.Renamed += (_, e) =>
        {
            _tracker.Forget(e.OldFullPath);
            Observe(e.FullPath);
        };
        _watcher.Deleted += (_, e) => _tracker.Forget(e.FullPath);
        _watcher.Error += (_, e) => Log.Warning(e.GetException(), "Watch folder events were lost for {Folder}.", folder);
        _timer = new Timer(_ => Poll(), null, pollInterval, pollInterval);
        _watcher.EnableRaisingEvents = true;
    }

    public string Folder { get; }

    /// <summary>Starts watching, or returns null when the folder is missing or not a plain folder.</summary>
    public static WatchFolderService? TryStart(string? folder, Action<IReadOnlyList<string>> onReady, TimeSpan? quietPeriod = null, TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(onReady);
        try
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return null;
            }

            string full = Path.GetFullPath(folder.Trim());
            var info = new DirectoryInfo(full);
            if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return null;
            }

            return new WatchFolderService(full, onReady, quietPeriod ?? QuietPeriod, pollInterval ?? PollInterval);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            Log.Warning(ex, "Could not watch {Folder}.", folder);
            return null;
        }
    }

    public static bool IsWatchedInput(string path)
    {
        string name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name) || name.StartsWith('.') || name.StartsWith('~')
            || PartialSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        string extension = Path.GetExtension(name);
        return !string.Equals(extension, ".chd", StringComparison.OrdinalIgnoreCase)
            && QueueInputClassifier.IsSupportedExtension(extension);
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _timer.Dispose();
    }

    private void Observe(string path)
    {
        if (!IsWatchedInput(path))
        {
            return;
        }

        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        _tracker.Observe(path, DateTime.UtcNow);
    }

    private void Poll()
    {
        IReadOnlyList<string> ready = _tracker.TakeReady(DateTime.UtcNow, SizeOf, CanOpenExclusively);
        if (ready.Count > 0)
        {
            Log.Information("Watch folder found {Count} new file(s) in {Folder}.", ready.Count, Folder);
            _onReady(ready);
        }
    }

    private static long? SizeOf(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool CanOpenExclusively(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
