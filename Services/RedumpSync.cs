using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public readonly record struct RedumpGitHubSyncProgress(
    string Stage,
    double Percent,
    string MessageKey,
    IReadOnlyList<object?> MessageArgs);

public readonly record struct RedumpGitHubSyncResult(
    bool Success,
    string MessageKey,
    IReadOnlyList<object?> MessageArgs,
    int ImportedSystems,
    DateTimeOffset SyncedAtUtc);

public sealed class RedumpGitHubSyncManager : IDisposable
{
    private const string DefaultGitHubZipUrl = "https://codeload.github.com/Ross-Y/Redump-DATS/zip/refs/heads/main";
    private const int MaxRedirectHops = 5;

    private const string DownloadStage = "download";
    private const string ExtractStage = "extract";
    private const string ImportStage = "import";

    private const string DownloadStartMessageKey = "LocRedumpSync_DownloadStart";
    private const string DownloadProgressMessageKey = "LocRedumpSync_DownloadProgress";
    private const string ExtractZipMessageKey = "LocRedumpSync_ExtractZip";
    private const string DirectDatMessageKey = "LocRedumpSync_DirectDat";
    private const string ImportProgressMessageKey = "LocRedumpSync_ImportProgress";
    private const string NoDatFilesMessageKey = "LocRedumpSync_NoDatFiles";
    private const string SuccessMessageKey = "LocRedumpSync_Success";
    private const string FailedMessageKey = "LocRedumpSync_Failed";
    private const string InvalidSourceUrlMessageKey = "LocRedumpSync_InvalidSourceUrl";
    private const string UnsafeZipEntryMessageKey = "LocRedumpSync_UnsafeZipEntry";

    private static readonly ILogger Logger = global::Serilog.Log.ForContext<RedumpGitHubSyncManager>();

    private readonly HttpClient _httpClient;
    private bool _disposed;

    public RedumpGitHubSyncManager(TimeSpan? timeout = null)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };

        _httpClient = new HttpClient(handler, disposeHandler: true);
        if (timeout.HasValue)
        {
            _httpClient.Timeout = timeout.Value;
        }
    }

    internal RedumpGitHubSyncManager(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _httpClient = new HttpClient(handler, disposeHandler: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _httpClient.Dispose();
    }

    public async Task<RedumpGitHubSyncResult> SyncFromGitHubAsync(
        string? zipUrl,
        IProgress<RedumpGitHubSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        string sourceUrl = string.IsNullOrWhiteSpace(zipUrl)
            ? DefaultGitHubZipUrl
            : zipUrl.Trim();

        if (!IsSupportedSourceUrl(sourceUrl))
        {
            return Failure(InvalidSourceUrlMessageKey, [], 0);
        }

        Uri sourceUri = new(sourceUrl, UriKind.Absolute);

        string workRoot = AppPaths.CombineProcessTemp("RedumpSync", Guid.NewGuid().ToString("N"));
        string payloadPath = Path.Combine(workRoot, "redump_payload.bin");
        string extractPath = Path.Combine(workRoot, "extract");

        int imported = 0;

        try
        {
            EnsureSafeProcessTempDirectory(workRoot);

            progress?.Report(new RedumpGitHubSyncProgress(DownloadStage, 5d, DownloadStartMessageKey, []));
            await DownloadFileAsync(sourceUri, payloadPath, progress, cancellationToken).ConfigureAwait(false);

            EnsureSafeProcessTempDirectory(extractPath);

            if (LooksLikeZip(payloadPath))
            {
                progress?.Report(new RedumpGitHubSyncProgress(ExtractStage, 55d, ExtractZipMessageKey, []));
                ExtractZipSafely(payloadPath, extractPath, cancellationToken);
            }
            else
            {
                progress?.Report(new RedumpGitHubSyncProgress(ExtractStage, 55d, DirectDatMessageKey, []));
                string directName = BuildDirectDatFileName(sourceUrl);
                string directTargetPath = Path.GetFullPath(Path.Combine(extractPath, directName));

                if (!IsUnderDirectory(extractPath, directTargetPath))
                {
                    throw new InvalidDataException(UnsafeZipEntryMessageKey);
                }

                EnsureSafeProcessTempFileTarget(directTargetPath);
                long directDatBytes = new FileInfo(payloadPath).Length;
                if (directDatBytes > ArchiveResourcePolicy.MaxRedumpSingleEntryBytes)
                {
                    throw new ArchiveResourceLimitException("redump-direct-dat");
                }

                EnsureRedumpFreeSpace(extractPath, directDatBytes);
                File.Copy(payloadPath, directTargetPath, overwrite: false);
            }

            List<string> datFiles =
            [
                .. EnumerateDatXmlFilesSafely(extractPath, cancellationToken)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            ];

            if (datFiles.Count == 0)
            {
                return Failure(NoDatFilesMessageKey, [], 0);
            }

            ArchiveResourcePolicy.ThrowIfEntryCountExceeded(
                datFiles.Count,
                ArchiveResourcePolicy.MaxRedumpEntries);

            var importEntries = new List<RedumpLocalLibraryDatEntry>(datFiles.Count);
            for (int index = 0; index < datFiles.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string datFile = datFiles[index];
                string systemName = Path.GetFileNameWithoutExtension(datFile);
                double percent = 60d + ((index + 1) / (double)datFiles.Count) * 38d;

                progress?.Report(new RedumpGitHubSyncProgress(
                    ImportStage,
                    percent,
                    ImportProgressMessageKey,
                    [systemName, index + 1, datFiles.Count]));

                FileInfo file = new(datFile);
                importEntries.Add(new RedumpLocalLibraryDatEntry(
                    file.FullName,
                    file.Name,
                    file.DirectoryName ?? extractPath,
                    file.Extension,
                    "github-sync",
                    systemName,
                    systemName,
                    Version: null,
                    DatDateUtc: null,
                    PreviewGameCount: null,
                    IsSelected: true,
                    Status: "ready",
                    Reason: null,
                    file.Length,
                    file.LastWriteTimeUtc));
            }

            RedumpImportResult atomicImport = await RedumpSqliteManager.Default
                .CleanRebuildFromDatFilesAsync(importEntries, progress: null, cancellationToken)
                .ConfigureAwait(false);

            if (!atomicImport.Success)
            {
                return Failure(FailedMessageKey, [], 0);
            }

            imported = importEntries.Count;

            return new RedumpGitHubSyncResult(
                true,
                SuccessMessageKey,
                [imported],
                imported,
                DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArchiveResourceLimitException ex)
        {
            Logger.Warning(ex, "Redump synchronization stopped by the resource policy. Url={Url}", sourceUrl);
            return Failure(ArchiveResourcePolicy.ResourceLimitMessageKey, [], 0);
        }
        catch (InvalidDataException ex) when (string.Equals(ex.Message, InvalidSourceUrlMessageKey, StringComparison.Ordinal))
        {
            Logger.Warning(ex, "Redump synchronization rejected the final download source. Url={Url}", sourceUrl);
            return Failure(InvalidSourceUrlMessageKey, [], 0);
        }
        catch (InvalidDataException ex) when (string.Equals(ex.Message, UnsafeZipEntryMessageKey, StringComparison.Ordinal))
        {
            Logger.Warning(ex, "Redump synchronization rejected an unsafe ZIP entry. Url={Url}", sourceUrl);
            return Failure(UnsafeZipEntryMessageKey, [], imported);
        }
        catch (Exception ex) when (IsExpectedSyncException(ex))
        {
            Logger.Warning(ex, "Redump synchronization failed. Url={Url}", sourceUrl);
            return Failure(FailedMessageKey, [], imported);
        }
        finally
        {
            DeleteWorkRootSafely(workRoot);
        }
    }

    private async Task DownloadFileAsync(
        Uri url,
        string destinationPath,
        IProgress<RedumpGitHubSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        string fullDestinationPath = Path.GetFullPath(destinationPath);
        EnsureSafeProcessTempFileTarget(fullDestinationPath);

        using HttpResponseMessage response = await SendWithValidatedRedirectsAsync(
            url,
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? -1L;
        if (totalBytes > ArchiveResourcePolicy.MaxRedumpDownloadBytes)
        {
            throw new ArchiveResourceLimitException("redump-content-length");
        }

        await using Stream input = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        await using FileStream output = new(
            fullDestinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);

        byte[] buffer = new byte[64 * 1024];
        long readTotal = 0L;

        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            long nextTotal = SaturatingAdd(readTotal, read);
            if (nextTotal > ArchiveResourcePolicy.MaxRedumpDownloadBytes)
            {
                throw new ArchiveResourceLimitException("redump-download");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

            readTotal = nextTotal;
            if (totalBytes > 0)
            {
                double percent = 5d + (readTotal / (double)totalBytes) * 45d;
                progress?.Report(new RedumpGitHubSyncProgress(
                    DownloadStage,
                    percent,
                    DownloadProgressMessageKey,
                    [percent]));
            }
        }
    }

    private static void ExtractZipSafely(
        string zipPath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        EnsureSafeProcessTempFileForRead(zipPath);

        string destinationRoot = TrimDirectorySeparators(
            Path.GetFullPath(destinationDirectory));
        EnsureSafeProcessTempDirectory(destinationRoot);

        string destinationRootWithSeparator =
            EnsureDirectorySeparatorSuffix(destinationRoot);

        using ZipArchive archive = ZipFile.OpenRead(zipPath);

        ArchiveResourcePolicy.ThrowIfEntryCountExceeded(
            archive.Entries.Count,
            ArchiveResourcePolicy.MaxRedumpEntries);

        long declaredExpandedBytes = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.Length > ArchiveResourcePolicy.MaxRedumpSingleEntryBytes)
            {
                throw new ArchiveResourceLimitException("redump-single-entry");
            }

            declaredExpandedBytes = ArchiveResourcePolicy.SaturatingAdd(
                declaredExpandedBytes,
                Math.Max(0, entry.Length));
            ArchiveResourcePolicy.ThrowIfExpandedBytesExceeded(
                declaredExpandedBytes,
                ArchiveResourcePolicy.MaxRedumpExpandedBytes);
        }

        EnsureRedumpFreeSpace(destinationRoot, declaredExpandedBytes);
        var extractionBudget = new ArchiveExtractionBudget(
            destinationRoot,
            declaredExpandedBytes,
            ArchiveResourcePolicy.MaxRedumpExpandedBytes);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(entry.FullName))
            {
                continue;
            }

            string entryName = entry.FullName.Replace('\\', '/');
            if (entryName.Contains('\0') || Path.IsPathRooted(entryName))
            {
                throw new InvalidDataException(UnsafeZipEntryMessageKey);
            }

            string targetPath = Path.GetFullPath(
                Path.Combine(destinationRoot, entryName));

            if (!targetPath.StartsWith(
                    destinationRootWithSeparator,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(UnsafeZipEntryMessageKey);
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                EnsureSafeProcessTempDirectory(targetPath);
                continue;
            }

            EnsureSafeProcessTempFileTarget(targetPath);
            CopyZipEntryToFile(entry, targetPath, extractionBudget, cancellationToken);
        }
    }

    private static void CopyZipEntryToFile(
        ZipArchiveEntry entry,
        string targetPath,
        ArchiveExtractionBudget extractionBudget,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[64 * 1024];

        using Stream input = entry.Open();
        using FileStream output = new(
            targetPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);

        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            extractionBudget.AddWrittenBytes(read);
            output.Write(buffer, 0, read);
        }
    }

    private static IEnumerable<string> EnumerateDatXmlFilesSafely(
        string rootPath,
        CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(rootPath);
        EnsureSafeProcessTempDirectory(root);

        Stack<string> pending = new();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string currentDirectory = pending.Pop();
            if (!IsUnderDirectory(root, currentDirectory) || HasReparsePointInExistingPathFromVolumeRoot(currentDirectory))
            {
                continue;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(currentDirectory, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (IsExpectedSyncException(ex))
            {
                continue;
            }

            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsUnderDirectory(root, file)
                    || HasReparsePointInExistingPathFromVolumeRoot(file)
                    || !AppPaths.IsPathUnderProcessTempRoot(file))
                {
                    continue;
                }

                string extension = Path.GetExtension(file);
                if (extension.Equals(".dat", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    yield return Path.GetFullPath(file);
                }
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(currentDirectory, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (IsExpectedSyncException(ex))
            {
                continue;
            }

            foreach (string directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsUnderDirectory(root, directory)
                    && !HasReparsePointInExistingPathFromVolumeRoot(directory)
                    && AppPaths.IsPathUnderProcessTempRoot(directory))
                {
                    pending.Push(directory);
                }
            }
        }
    }

    private static bool LooksLikeZip(string filePath)
    {
        EnsureSafeProcessTempFileForRead(filePath);

        Span<byte> header = stackalloc byte[4];

        using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        int read = stream.Read(header);

        return read >= 4
            && header[0] == 0x50
            && header[1] == 0x4B
            && (header[2] == 0x03 || header[2] == 0x05 || header[2] == 0x07)
            && (header[3] == 0x04 || header[3] == 0x06 || header[3] == 0x08);
    }

    private static string BuildDirectDatFileName(string sourceUrl)
    {
        try
        {
            Uri uri = new(sourceUrl, UriKind.Absolute);
            string fileName = Path.GetFileName(uri.LocalPath);

            if (!string.IsNullOrWhiteSpace(fileName)
                && (fileName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
            {
                return SanitizeFileName(fileName);
            }
        }
        catch (UriFormatException)
        {
        }

        return "Redump.dat";
    }

    private static string SanitizeFileName(string value)
    {
        string fileName = Path.GetFileName(value.Trim());
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "Redump.dat";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        char[] chars =
        [
            .. fileName.Select(character => Array.IndexOf(invalid, character) >= 0 ? '_' : character)
        ];

        string cleaned = new string(chars).Trim().TrimEnd('.', ' ');
        return string.IsNullOrWhiteSpace(cleaned) ? "Redump.dat" : cleaned;
    }

    private static bool IsSupportedSourceUrl(string sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return IsSupportedSourceUri(uri);
    }

    private static bool IsSupportedSourceUri(Uri uri)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort)
        {
            return false;
        }

        return uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("codeload.github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureRedumpFreeSpace(string destinationPath, long plannedBytes)
    {
        ArchiveResourcePolicy.ThrowIfExpandedBytesExceeded(
            plannedBytes,
            ArchiveResourcePolicy.MaxRedumpExpandedBytes);

        long required = ArchiveResourcePolicy.SaturatingAdd(
            plannedBytes,
            ArchiveResourcePolicy.MinimumFreeSpaceReserveBytes);
        if (ArchiveResourcePolicy.GetAvailableFreeSpace(destinationPath) < required)
        {
            throw new ArchiveResourceLimitException("redump-free-space");
        }
    }

    private async Task<HttpResponseMessage> SendWithValidatedRedirectsAsync(
        Uri initialUri,
        CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Uri currentUri = initialUri;

        for (int redirectCount = 0; ; redirectCount++)
        {
            if (!IsSupportedSourceUri(currentUri)
                || !visited.Add(currentUri.AbsoluteUri))
            {
                throw new InvalidDataException(InvalidSourceUrlMessageKey);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (!IsRedirectStatusCode(response.StatusCode))
            {
                return response;
            }

            Uri? nextUri = TryResolveRedirectUri(currentUri, response.Headers.Location);
            response.Dispose();

            if (redirectCount >= MaxRedirectHops
                || nextUri is null
                || !IsSupportedSourceUri(nextUri)
                || visited.Contains(nextUri.AbsoluteUri))
            {
                throw new InvalidDataException(InvalidSourceUrlMessageKey);
            }

            currentUri = nextUri;
        }
    }

    private static bool IsRedirectStatusCode(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently
        or HttpStatusCode.Found
        or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect
        or HttpStatusCode.PermanentRedirect;

    private static Uri? TryResolveRedirectUri(Uri currentUri, Uri? location)
    {
        if (location is null)
        {
            return null;
        }

        try
        {
            return location.IsAbsoluteUri
                ? location
                : new Uri(currentUri, location);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static void DeleteWorkRootSafely(string workRoot)
    {
        try
        {
            string fullWorkRoot = Path.GetFullPath(workRoot);

            if (!AppPaths.IsPathUnderProcessTempRoot(fullWorkRoot))
            {
                Logger.Warning("Redump synchronization skipped temp cleanup outside process temp root. Path={Path}", workRoot);
                return;
            }

            DeleteDirectoryTreeWithoutFollowingReparse(fullWorkRoot);
        }
        catch (Exception ex) when (IsExpectedCleanupException(ex))
        {
            Logger.Warning(ex, "Redump synchronization failed to delete temp work directory. Path={Path}", workRoot);
        }
    }

    private static void DeleteDirectoryTreeWithoutFollowingReparse(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        if (IsReparsePoint(directoryPath))
        {
            Directory.Delete(directoryPath);
            return;
        }

        foreach (string file in Directory.GetFiles(directoryPath, "*", SearchOption.TopDirectoryOnly))
        {
            File.Delete(file);
        }

        foreach (string directory in Directory.GetDirectories(directoryPath, "*", SearchOption.TopDirectoryOnly))
        {
            if (IsReparsePoint(directory))
            {
                Directory.Delete(directory);
                continue;
            }

            DeleteDirectoryTreeWithoutFollowingReparse(directory);
        }

        Directory.Delete(directoryPath);
    }

    private static void EnsureSafeProcessTempDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path);

        if (!AppPaths.IsPathUnderProcessTempRoot(fullPath)
            || HasReparsePointInExistingPathFromVolumeRoot(fullPath))
        {
            throw new InvalidDataException(UnsafeZipEntryMessageKey);
        }

        Directory.CreateDirectory(fullPath);

        if (!Directory.Exists(fullPath)
            || !AppPaths.IsPathUnderProcessTempRoot(fullPath)
            || HasReparsePointInExistingPathFromVolumeRoot(fullPath))
        {
            throw new InvalidDataException(UnsafeZipEntryMessageKey);
        }
    }

    private static void EnsureSafeProcessTempFileTarget(string path)
    {
        string fullPath = Path.GetFullPath(path);

        if (!AppPaths.IsPathUnderProcessTempRoot(fullPath)
            || HasReparsePointInExistingPathFromVolumeRoot(fullPath)
            || File.Exists(fullPath)
            || Directory.Exists(fullPath))
        {
            throw new InvalidDataException(UnsafeZipEntryMessageKey);
        }

        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidDataException(UnsafeZipEntryMessageKey);
        }

        EnsureSafeProcessTempDirectory(directory);
    }

    private static void EnsureSafeProcessTempFileForRead(string path)
    {
        string fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath)
            || !AppPaths.IsPathUnderProcessTempRoot(fullPath)
            || HasReparsePointInExistingPathFromVolumeRoot(fullPath))
        {
            throw new InvalidDataException(UnsafeZipEntryMessageKey);
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
        catch (Exception ex) when (IsExpectedSyncException(ex) || IsExpectedCleanupException(ex))
        {
            return true;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (IsExpectedSyncException(ex) || IsExpectedCleanupException(ex))
        {
            return true;
        }
    }

    private static bool IsUnderDirectory(string baseDirectory, string candidate)
    {
        string root = TrimDirectorySeparators(Path.GetFullPath(baseDirectory));
        string path = TrimDirectorySeparators(Path.GetFullPath(candidate));

        return string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(EnsureDirectorySeparatorSuffix(root), StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            TrimDirectorySeparators(Path.GetFullPath(left)),
            TrimDirectorySeparators(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureDirectorySeparatorSuffix(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string TrimDirectorySeparators(string path)
    {
        string? root = Path.GetPathRoot(path);

        if (!string.IsNullOrWhiteSpace(root)
            && path.Length <= root.Length)
        {
            return root;
        }

        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.IsNullOrEmpty(trimmed) && !string.IsNullOrWhiteSpace(root)
            ? root
            : trimmed;
    }

    private static long SaturatingAdd(long left, long right)
    {
        if (right > 0 && left > long.MaxValue - right)
        {
            return long.MaxValue;
        }

        return left + right;
    }

    private static RedumpGitHubSyncResult Failure(
        string messageKey,
        IReadOnlyList<object?> messageArgs,
        int importedSystems) =>
        new(false, messageKey, messageArgs, importedSystems, DateTimeOffset.UtcNow);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static bool IsExpectedSyncException(Exception ex) =>
        ex is HttpRequestException
        or IOException
        or UnauthorizedAccessException
        or InvalidDataException
        or NotSupportedException
        or ArgumentException
        or UriFormatException
        or PathTooLongException
        or System.Security.SecurityException;

    private static bool IsExpectedCleanupException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or NotSupportedException
        or ArgumentException
        or PathTooLongException
        or System.Security.SecurityException;
}
