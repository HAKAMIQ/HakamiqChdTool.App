using Serilog;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public sealed class RuntimeToolService
{
    private const string AppName = "HakamiqChdTool";
    private const string ChdmanResourceName = "HakamiqChdTool.App.Tools.chdman.exe";

    private const string UnsafeRuntimeRootMessageKey = "LocRuntimeTools_UnsafeRuntimeRoot";
    private const string UnsafeToolExtractionPathMessageKey = "LocRuntimeTools_UnsafeToolExtractionPath";
    private const string EmbeddedToolMissingMessageKey = "LocRuntimeTools_EmbeddedToolMissing";
    private const string EmbeddedToolInvalidMessageKey = "LocRuntimeTools_EmbeddedToolInvalid";
    private const string EmbeddedToolEmptyWriteMessageKey = "LocRuntimeTools_EmbeddedToolEmptyWrite";
    private const string EmbeddedToolSizeMismatchMessageKey = "LocRuntimeTools_EmbeddedToolSizeMismatch";
    private const string ExtractedToolUnsafeMessageKey = "LocRuntimeTools_ExtractedToolUnsafe";
    private const string ExtractedToolMissingMessageKey = "LocRuntimeTools_ExtractedToolMissing";
    private const string ExtractedToolInvalidMessageKey = "LocRuntimeTools_ExtractedToolInvalid";

    private static readonly ILogger Logger = global::Serilog.Log.ForContext<RuntimeToolService>();

    private static readonly string SafeAppDataRoot = EnsureTrailingSeparator(InitializeSafeAppDataRoot());

    private static readonly object ExtractLock = new();

    private readonly object _sync = new();
    private readonly object _cleanupSync = new();
    private readonly string _baseRuntimeRoot;
    private readonly string _versionFolder;
    private readonly string _sessionId;
    private readonly string _sessionDirectory;
    private readonly string _ownerPidFile;

    private bool _initialized;
    private string _chdmanPath = string.Empty;
    private Task? _deferredCleanupTask;

    public static RuntimeToolService Instance { get; } = new();

    private RuntimeToolService()
    {
        string safeRoot = TrimDirectorySeparators(SafeAppDataRoot);
        _baseRuntimeRoot = Path.Combine(safeRoot, ".runtime");

        if (!IsStrictlyUnderSafeAppDataRoot(_baseRuntimeRoot))
        {
            throw new InvalidOperationException(UnsafeRuntimeRootMessageKey);
        }

        Version? version = Assembly.GetExecutingAssembly().GetName().Version;
        _versionFolder = version is null
            ? "1.0.0"
            : $"{version.Major}.{version.Minor}.{version.Build}";

        _sessionId = Guid.NewGuid().ToString("N");
        _sessionDirectory = Path.Combine(_baseRuntimeRoot, _versionFolder, _sessionId);
        _ownerPidFile = Path.Combine(_sessionDirectory, "owner.pid");
    }

    public void EnsureInitialized(bool cleanupStaleSessions = true)
    {
        if (Volatile.Read(ref _initialized))
        {
            return;
        }

        lock (_sync)
        {
            if (_initialized)
            {
                return;
            }

            if (cleanupStaleSessions)
            {
                CleanupStaleRuntimeDirectories();
            }

            EnsureSafeRuntimeDirectory(_sessionDirectory);
            TryWriteOwnerPidFile();
            TrySetDirectoryHidden(_sessionDirectory);

            _chdmanPath = Path.Combine(_sessionDirectory, "chdman.exe");

            lock (ExtractLock)
            {
                if (ShouldExtractEmbeddedChdman(_chdmanPath))
                {
                    ExtractEmbeddedTool(ChdmanResourceName, _chdmanPath);
                }

                ValidateExtractedChdman(_chdmanPath);
            }

            Logger.Information("RuntimeTools: chdman ready. Path={Path}", _chdmanPath);
            Volatile.Write(ref _initialized, true);
        }
    }

    public Task StartDeferredCleanupAsync()
    {
        lock (_cleanupSync)
        {
            if (_deferredCleanupTask is not null)
            {
                return _deferredCleanupTask;
            }

            _deferredCleanupTask = Task.Run(() =>
            {
                try
                {
                    CleanupStaleRuntimeDirectories();
                }
                catch (Exception ex) when (IsExpectedCleanupException(ex))
                {
                    Logger.Debug(ex, "RuntimeTools: deferred cleanup failed.");
                }
            });

            return _deferredCleanupTask;
        }
    }

    public Task WaitForDeferredCleanupAsync()
    {
        lock (_cleanupSync)
        {
            return _deferredCleanupTask ?? Task.CompletedTask;
        }
    }

    public string GetChdmanPath()
    {
        EnsureInitialized();
        return _chdmanPath;
    }

    public void TryCleanupCurrentSession()
    {
        try
        {
            TryDeleteDirectorySafely(_sessionDirectory, recursive: true);

            string versionDirectory = Path.Combine(_baseRuntimeRoot, _versionFolder);
            TryDeleteVersionDirectoryIfEmpty(versionDirectory);
        }
        catch (Exception ex) when (IsExpectedCleanupException(ex))
        {
            Logger.Warning(ex, "RuntimeTools: session cleanup failed. SessionDir={Dir}", _sessionDirectory);
        }
    }

    private void TryWriteOwnerPidFile()
    {
        try
        {
            if (!IsStrictlyUnderSafeAppDataRoot(_ownerPidFile)
                || HasReparsePointInExistingPathFromVolumeRoot(_ownerPidFile))
            {
                throw new IOException(UnsafeRuntimeRootMessageKey);
            }

            File.WriteAllText(_ownerPidFile, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            TrySetFileHidden(_ownerPidFile);
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            Logger.Debug(ex, "RuntimeTools: could not write session owner marker. Path={Path}", _ownerPidFile);
        }
    }

    private static bool IsSessionOwnedByLiveProcess(string sessionDirectory)
    {
        if (!TryNormalizeExistingSafeDirectory(sessionDirectory, out string safeSessionDirectory))
        {
            return false;
        }

        string pidFile = Path.Combine(safeSessionDirectory, "owner.pid");
        if (!IsStrictlyUnderSafeAppDataRoot(pidFile)
            || HasReparsePointInExistingPath(pidFile, safeSessionDirectory)
            || !File.Exists(pidFile))
        {
            return false;
        }

        string raw;
        try
        {
            raw = File.ReadAllText(pidFile).Trim();
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            Logger.Debug(ex, "RuntimeTools: could not read session owner marker. Path={Path}", pidFile);
            return false;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid) || pid <= 0)
        {
            return false;
        }

        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception ex) when (IsExpectedProcessException(ex))
        {
            Logger.Debug("RuntimeTools: session owner process is not live. Pid={Pid}", pid);
            return false;
        }
    }

    private static bool IsOldEnoughToDelete(string sessionDirectory)
    {
        try
        {
            if (!TryNormalizeExistingSafeDirectory(sessionDirectory, out string safeSessionDirectory))
            {
                return false;
            }

            DateTime lastWriteUtc = Directory.GetLastWriteTimeUtc(safeSessionDirectory);
            return DateTime.UtcNow - lastWriteUtc >= TimeSpan.FromHours(24);
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            Logger.Debug(ex, "RuntimeTools: could not read stale session timestamp. Dir={Dir}", sessionDirectory);
            return false;
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        if (!TryDeleteDirectorySafely(directory, recursive: true))
        {
            Logger.Debug("RuntimeTools: skipped unsafe stale session directory delete. Dir={Dir}", directory);
        }
    }

    private void CleanupStaleRuntimeDirectories()
    {
        try
        {
            if (!TryNormalizeExistingSafeDirectory(_baseRuntimeRoot, out string safeRuntimeRoot))
            {
                return;
            }

            foreach (string versionDirectory in Directory.EnumerateDirectories(safeRuntimeRoot))
            {
                if (!TryNormalizeExistingSafeDirectory(versionDirectory, out string safeVersionDirectory))
                {
                    continue;
                }

                foreach (string sessionDirectory in Directory.EnumerateDirectories(safeVersionDirectory))
                {
                    if (!TryNormalizeExistingSafeDirectory(sessionDirectory, out string safeSessionDirectory))
                    {
                        continue;
                    }

                    string sessionName = Path.GetFileName(safeSessionDirectory);
                    if (string.Equals(sessionName, _sessionId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (IsSessionOwnedByLiveProcess(safeSessionDirectory))
                    {
                        continue;
                    }

                    string ownerPidPath = Path.Combine(safeSessionDirectory, "owner.pid");
                    if (File.Exists(ownerPidPath) || IsOldEnoughToDelete(safeSessionDirectory))
                    {
                        TryDeleteDirectory(safeSessionDirectory);
                    }
                }

                TryDeleteVersionDirectoryIfEmpty(safeVersionDirectory);
            }
        }
        catch (Exception ex) when (IsExpectedCleanupException(ex))
        {
            Logger.Warning(ex, "RuntimeTools: stale runtime directory scan/delete failed.");
        }
    }

    private static void TryDeleteVersionDirectoryIfEmpty(string versionDirectory)
    {
        try
        {
            if (!TryNormalizeExistingSafeDirectory(versionDirectory, out string safeVersionDirectory))
            {
                return;
            }

            if (!Directory.EnumerateFileSystemEntries(safeVersionDirectory).Any())
            {
                TryDeleteDirectorySafely(safeVersionDirectory, recursive: false);
            }
        }
        catch (Exception ex) when (IsExpectedCleanupException(ex))
        {
            Logger.Debug(ex, "RuntimeTools: could not remove empty version directory. Dir={Dir}", versionDirectory);
        }
    }

    private static bool ShouldExtractEmbeddedChdman(string chdmanPath)
    {
        string fullPath = Path.GetFullPath(chdmanPath);

        if (!IsStrictlyUnderSafeAppDataRoot(fullPath))
        {
            throw new InvalidOperationException(ExtractedToolUnsafeMessageKey);
        }

        if (HasReparsePointInExistingPathFromVolumeRoot(fullPath))
        {
            throw new InvalidOperationException(ExtractedToolUnsafeMessageKey);
        }

        if (!File.Exists(fullPath))
        {
            return true;
        }

        try
        {
            return new FileInfo(fullPath).Length == 0;
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            Logger.Debug(ex, "RuntimeTools: could not verify existing chdman; will re-extract. Path={Path}", fullPath);
            return true;
        }
    }

    private static void ExtractEmbeddedTool(string resourceName, string outputPath)
    {
        string fullOutputPath = Path.GetFullPath(outputPath);
        if (!IsStrictlyUnderSafeAppDataRoot(fullOutputPath))
        {
            throw new InvalidOperationException(UnsafeToolExtractionPathMessageKey);
        }

        string? outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException(UnsafeToolExtractionPathMessageKey);
        }

        if (!TryNormalizeExistingSafeDirectory(outputDirectory, out string safeOutputDirectory)
            || !IsSamePathOrChild(fullOutputPath, safeOutputDirectory))
        {
            throw new InvalidOperationException(UnsafeToolExtractionPathMessageKey);
        }

        if (File.Exists(fullOutputPath)
            && HasReparsePointInExistingPath(fullOutputPath, safeOutputDirectory))
        {
            throw new InvalidOperationException(ExtractedToolUnsafeMessageKey);
        }

        Assembly assembly = Assembly.GetExecutingAssembly();

        using Stream resourceStream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(EmbeddedToolMissingMessageKey);

        long? expectedLength = resourceStream.CanSeek ? resourceStream.Length : null;
        if (expectedLength is long length && length <= 0)
        {
            Logger.Error("RuntimeTools: embedded chdman resource has invalid length. Length={Length}", length);
            throw new InvalidOperationException(EmbeddedToolInvalidMessageKey);
        }

        string outputFileName = Path.GetFileName(fullOutputPath);
        if (string.IsNullOrWhiteSpace(outputFileName))
        {
            throw new InvalidOperationException(UnsafeToolExtractionPathMessageKey);
        }

        string tempPath = Path.Combine(
            safeOutputDirectory,
            $"{outputFileName}.{Guid.NewGuid():N}.tmp");

        if (!IsSamePathOrChild(tempPath, safeOutputDirectory)
            || HasReparsePointInExistingPathFromVolumeRoot(tempPath))
        {
            throw new InvalidOperationException(UnsafeToolExtractionPathMessageKey);
        }

        try
        {
            using (FileStream fileStream = new(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 1024 * 1024,
                       FileOptions.SequentialScan | FileOptions.WriteThrough))
            {
                resourceStream.CopyTo(fileStream);
                fileStream.Flush(flushToDisk: true);
            }

            if (HasReparsePointInExistingPath(tempPath, safeOutputDirectory))
            {
                throw new IOException(ExtractedToolUnsafeMessageKey);
            }

            long written = new FileInfo(tempPath).Length;
            if (written == 0)
            {
                throw new IOException(EmbeddedToolEmptyWriteMessageKey);
            }

            if (expectedLength.HasValue && written != expectedLength.Value)
            {
                throw new IOException(EmbeddedToolSizeMismatchMessageKey);
            }

            if (File.Exists(fullOutputPath)
                && HasReparsePointInExistingPath(fullOutputPath, safeOutputDirectory))
            {
                throw new IOException(ExtractedToolUnsafeMessageKey);
            }

            File.Move(tempPath, fullOutputPath, overwrite: true);
            ValidateExtractedChdman(fullOutputPath);

            Logger.Information("RuntimeTools: embedded chdman extracted. Target={Path}, Bytes={Bytes}", fullOutputPath, written);
        }
        catch (Exception ex) when (IsExpectedExtractionException(ex))
        {
            Logger.Error(ex, "RuntimeTools: embedded chdman extraction failed. Target={Path}", fullOutputPath);
            TryDeleteFileIfExists(tempPath);
            throw;
        }

        TrySetFileHidden(fullOutputPath);
    }

    private static void ValidateExtractedChdman(string chdmanPath)
    {
        string fullPath = Path.GetFullPath(chdmanPath);
        if (!IsStrictlyUnderSafeAppDataRoot(fullPath))
        {
            throw new InvalidOperationException(ExtractedToolUnsafeMessageKey);
        }

        if (!string.Equals(Path.GetFileName(fullPath), "chdman.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(ExtractedToolInvalidMessageKey);
        }

        if (HasReparsePointInExistingPathFromVolumeRoot(fullPath))
        {
            throw new InvalidOperationException(ExtractedToolUnsafeMessageKey);
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(ExtractedToolMissingMessageKey, fullPath);
        }

        FileInfo fileInfo = new(fullPath);
        if (fileInfo.Length <= 0)
        {
            throw new InvalidOperationException(ExtractedToolInvalidMessageKey);
        }
    }

    private static void TryDeleteFileIfExists(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);

            if (!IsStrictlyUnderSafeAppDataRoot(fullPath)
                || HasReparsePointInExistingPathFromVolumeRoot(fullPath))
            {
                return;
            }

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            Logger.Debug(ex, "RuntimeTools: could not delete temp extraction file. Path={Path}", path);
        }
    }

    private static void TrySetDirectoryHidden(string directoryPath)
    {
        try
        {
            string fullPath = Path.GetFullPath(directoryPath);
            if (!TryNormalizeExistingSafeDirectory(fullPath, out string safeDirectory))
            {
                return;
            }

            FileAttributes current = File.GetAttributes(safeDirectory);
            if (!current.HasFlag(FileAttributes.Hidden))
            {
                File.SetAttributes(safeDirectory, current | FileAttributes.Hidden);
            }
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            Logger.Debug(ex, "RuntimeTools: could not set hidden attribute on session directory. Path={Path}", directoryPath);
        }
    }

    private static void TrySetFileHidden(string filePath)
    {
        try
        {
            string fullPath = Path.GetFullPath(filePath);
            if (!IsStrictlyUnderSafeAppDataRoot(fullPath)
                || HasReparsePointInExistingPathFromVolumeRoot(fullPath)
                || !File.Exists(fullPath))
            {
                return;
            }

            FileAttributes current = File.GetAttributes(fullPath);
            File.SetAttributes(fullPath, current | FileAttributes.Hidden | FileAttributes.Temporary);
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            Logger.Debug(ex, "RuntimeTools: could not set attributes on extracted tool. Path={Path}", filePath);
        }
    }

    private static string InitializeSafeAppDataRoot()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException(UnsafeRuntimeRootMessageKey);
        }

        string fullLocalAppData = Path.GetFullPath(localAppData);
        if (!Directory.Exists(fullLocalAppData)
            || IsUnsafeRoot(fullLocalAppData)
            || HasReparsePointInExistingPathFromVolumeRoot(fullLocalAppData))
        {
            throw new InvalidOperationException(UnsafeRuntimeRootMessageKey);
        }

        string appRoot = Path.GetFullPath(Path.Combine(fullLocalAppData, AppName));
        if (!IsSamePathOrChild(appRoot, fullLocalAppData) || PathsEqual(appRoot, fullLocalAppData))
        {
            throw new InvalidOperationException(UnsafeRuntimeRootMessageKey);
        }

        Directory.CreateDirectory(appRoot);

        if (!Directory.Exists(appRoot)
            || IsUnsafeRoot(appRoot)
            || HasReparsePointInExistingPathFromVolumeRoot(appRoot))
        {
            throw new InvalidOperationException(UnsafeRuntimeRootMessageKey);
        }

        return appRoot;
    }

    private static void EnsureSafeRuntimeDirectory(string directoryPath)
    {
        string fullPath = Path.GetFullPath(directoryPath);

        if (!IsStrictlyUnderSafeAppDataRoot(fullPath)
            || IsUnsafeRoot(fullPath)
            || HasReparsePointInExistingPathFromVolumeRoot(fullPath))
        {
            throw new InvalidOperationException(UnsafeRuntimeRootMessageKey);
        }

        Directory.CreateDirectory(fullPath);

        if (!Directory.Exists(fullPath)
            || HasReparsePointInExistingPathFromVolumeRoot(fullPath))
        {
            throw new InvalidOperationException(UnsafeRuntimeRootMessageKey);
        }
    }

    private static bool TryNormalizeExistingSafeDirectory(string path, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path.Trim());

            if (!Directory.Exists(fullPath)
                || !IsStrictlyUnderSafeAppDataRoot(fullPath)
                || IsUnsafeRoot(fullPath)
                || HasReparsePointInExistingPathFromVolumeRoot(fullPath))
            {
                return false;
            }

            normalized = fullPath;
            return true;
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            return false;
        }
    }

    private static bool TryDeleteDirectorySafely(string directory, bool recursive)
    {
        try
        {
            if (!TryNormalizeExistingSafeDirectory(directory, out string safeDirectory))
            {
                return false;
            }

            if (recursive && ContainsReparsePointUnderDirectory(safeDirectory))
            {
                Logger.Warning("RuntimeTools: refusing recursive delete because a reparse point exists under directory. Dir={Dir}", safeDirectory);
                return false;
            }

            Directory.Delete(safeDirectory, recursive);
            return true;
        }
        catch (Exception ex) when (IsExpectedCleanupException(ex))
        {
            Logger.Debug(ex, "RuntimeTools: could not remove directory. Dir={Dir}", directory);
            return false;
        }
    }

    private static bool ContainsReparsePointUnderDirectory(string directory)
    {
        try
        {
            string fullDirectory = Path.GetFullPath(directory);
            if (IsReparsePoint(fullDirectory))
            {
                return true;
            }

            foreach (string entry in Directory.EnumerateFileSystemEntries(fullDirectory))
            {
                if (IsReparsePoint(entry))
                {
                    return true;
                }

                if (Directory.Exists(entry) && ContainsReparsePointUnderDirectory(entry))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            return true;
        }
    }

    private static bool HasReparsePointInExistingPathFromVolumeRoot(string candidatePath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string? root = Path.GetPathRoot(candidate);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return HasReparsePointInExistingPath(candidate, root);
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            return true;
        }
    }

    private static bool HasReparsePointInExistingPath(string candidatePath, string rootPath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string root = Path.GetFullPath(rootPath);

            if (!IsSamePathOrChild(candidate, root))
            {
                return true;
            }

            string current = candidate;

            while (true)
            {
                if ((File.Exists(current) || Directory.Exists(current)) && IsReparsePoint(current))
                {
                    return true;
                }

                if (PathsEqual(current, root))
                {
                    return false;
                }

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent) || PathsEqual(parent, current))
                {
                    return true;
                }

                current = parent;
            }
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            return true;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            return true;
        }
    }

    private static bool IsUnsafeRoot(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            string normalized = TrimDirectorySeparators(fullPath);
            string? root = Path.GetPathRoot(fullPath);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            string normalizedRoot = TrimDirectorySeparators(root);
            return string.Equals(normalized, normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return true;
        }
    }

    private static bool IsStrictlyUnderSafeAppDataRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string root = TrimDirectorySeparators(SafeAppDataRoot);
            string candidate = TrimDirectorySeparators(Path.GetFullPath(path));

            return !string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
                && IsSamePathOrChild(candidate, root);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            Logger.Debug(ex, "RuntimeTools: path normalization failed for safety check.");
            return false;
        }
    }

    private static bool IsSamePathOrChild(string candidatePath, string rootPath)
    {
        string candidate = TrimDirectorySeparators(Path.GetFullPath(candidatePath));
        string root = TrimDirectorySeparators(Path.GetFullPath(rootPath));

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            TrimDirectorySeparators(Path.GetFullPath(left)),
            TrimDirectorySeparators(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        char last = path[^1];
        return last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string TrimDirectorySeparators(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException
        or System.Security.SecurityException;

    private static bool IsExpectedFileSystemException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException
        or PathTooLongException
        or InvalidOperationException
        or System.Security.SecurityException;

    private static bool IsExpectedCleanupException(Exception ex) =>
        IsExpectedFileSystemException(ex)
        || ex is DirectoryNotFoundException;

    private static bool IsExpectedExtractionException(Exception ex) =>
        IsExpectedFileSystemException(ex)
        || ex is InvalidOperationException;

    private static bool IsExpectedProcessException(Exception ex) =>
        ex is ArgumentException
        or InvalidOperationException
        or System.ComponentModel.Win32Exception
        or NotSupportedException;
}