using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
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

public sealed class RedumpGitHubSyncManager(HttpClient? httpClient = null) : IDisposable
{
    private const string DefaultGitHubZipUrl = "https://codeload.github.com/Ross-Y/Redump-DATS/zip/refs/heads/main";

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

    private readonly HttpClient _httpClient = httpClient ?? new HttpClient();
    private readonly bool _ownsHttpClient = httpClient is null;
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
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

        string workRoot = AppPaths.CombineProcessTemp("RedumpSync", Guid.NewGuid().ToString("N"));
        string payloadPath = Path.Combine(workRoot, "redump_payload.bin");
        string extractPath = Path.Combine(workRoot, "extract");

        int imported = 0;

        try
        {
            EnsureSafeProcessTempDirectory(workRoot);

            progress?.Report(new RedumpGitHubSyncProgress(DownloadStage, 5d, DownloadStartMessageKey, []));
            await DownloadFileAsync(sourceUrl, payloadPath, progress, cancellationToken).ConfigureAwait(false);

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

                File.Copy(payloadPath, directTargetPath, overwrite: true);
            }

            List<string> datFiles =
            [
                .. Directory
                    .EnumerateFiles(extractPath, "*.dat", SearchOption.AllDirectories)
                    .Concat(Directory.EnumerateFiles(extractPath, "*.xml", SearchOption.AllDirectories))
                    .Where(AppPaths.IsPathUnderProcessTempRoot)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            ];

            if (datFiles.Count == 0)
            {
                return Failure(NoDatFilesMessageKey, [], 0);
            }

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

                RedumpImportResult result = await RedumpSqliteManager.Default
                    .ImportDatFileAsync(datFile, systemName, progress: null, cancellationToken)
                    .ConfigureAwait(false);

                if (result.Success)
                {
                    imported++;
                }
            }

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
        string url,
        string destinationPath,
        IProgress<RedumpGitHubSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        string fullDestinationPath = Path.GetFullPath(destinationPath);
        if (!AppPaths.IsPathUnderProcessTempRoot(fullDestinationPath))
        {
            throw new InvalidDataException(UnsafeZipEntryMessageKey);
        }

        using HttpResponseMessage response = await _httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? -1L;

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
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

            readTotal += read;
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
        string destinationRoot = Path.GetFullPath(destinationDirectory);
        EnsureSafeProcessTempDirectory(destinationRoot);

        string root = EnsureTrailingSeparator(destinationRoot);

        using ZipArchive archive = ZipFile.OpenRead(zipPath);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(entry.FullName))
            {
                continue;
            }

            string entryName = entry.FullName.Replace('\\', '/');
            if (entryName.Contains('\0'))
            {
                throw new InvalidDataException(UnsafeZipEntryMessageKey);
            }

            string targetPath = Path.GetFullPath(Path.Combine(root, entryName));

            if (!IsUnderDirectory(root, targetPath))
            {
                throw new InvalidDataException(UnsafeZipEntryMessageKey);
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                EnsureSafeProcessTempDirectory(targetPath);
                continue;
            }

            string? targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                EnsureSafeProcessTempDirectory(targetDirectory);
            }

            entry.ExtractToFile(targetPath, overwrite: true);

            if (!AppPaths.IsPathUnderProcessTempRoot(targetPath))
            {
                throw new InvalidDataException(UnsafeZipEntryMessageKey);
            }
        }
    }

    private static bool LooksLikeZip(string filePath)
    {
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

        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(uri.Host)
            && string.IsNullOrEmpty(uri.UserInfo);
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

            if (Directory.Exists(fullWorkRoot))
            {
                Directory.Delete(fullWorkRoot, recursive: true);
            }
        }
        catch (Exception ex) when (IsExpectedCleanupException(ex))
        {
            Logger.Warning(ex, "Redump synchronization failed to delete temp work directory. Path={Path}", workRoot);
        }
    }

    private static void EnsureSafeProcessTempDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(fullPath);

        if (!Directory.Exists(fullPath) || !AppPaths.IsPathUnderProcessTempRoot(fullPath))
        {
            throw new InvalidDataException(UnsafeZipEntryMessageKey);
        }
    }

    private static bool IsUnderDirectory(string baseDirectory, string candidate)
    {
        string root = EnsureTrailingSeparator(Path.GetFullPath(baseDirectory));
        string path = Path.GetFullPath(candidate);

        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
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
        or UriFormatException;

    private static bool IsExpectedCleanupException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or NotSupportedException
        or ArgumentException;
}