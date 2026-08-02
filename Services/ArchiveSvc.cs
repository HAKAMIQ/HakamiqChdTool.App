using Serilog;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public sealed class ArchiveExtractionService
{
    private const string SevenZipUnavailableMessageKey = "LocArchive_SevenZipUnavailable";
    private const int BufferSize = 1024 * 1024;
    private const string ArchiveVerificationFailedKey = "LocArchive_VerificationFailed";
    private const string UserCancelledKey = "LocStatus_UserCancelled";

    private static readonly ILogger Logger = Log.ForContext<ArchiveExtractionService>();
    private static readonly SevenZipArchiveExtractionService SevenZipExtractor = new();

    public async Task<ArchiveIntegrityResult> ValidateArchiveReadableAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            return new ArchiveIntegrityResult
            {
                IsValid = false,
                MessageResourceKey = ArchiveVerificationFailedKey
            };
        }

        if (SevenZipExtractor.IsAvailable)
        {
            return await SevenZipExtractor
                .TestArchiveAsync(archivePath, cancellationToken)
                .ConfigureAwait(false);
        }

        ReaderOptions readerOptions = new()
        {
            LookForHeader = true,
            ArchiveEncoding = new ArchiveEncoding()
        };

        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            using IArchive archive = ArchiveFactory.OpenArchive(new FileInfo(archivePath), readerOptions);

            int entryCount = 0;
            long declaredBytes = 0;
            long validatedBytes = 0;

            foreach (IArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ArchiveResourcePolicy.ThrowIfEntryCountExceeded(++entryCount);
                if (entry.IsDirectory)
                {
                    continue;
                }

                declaredBytes = ArchiveResourcePolicy.SaturatingAdd(
                    declaredBytes,
                    Math.Max(0, entry.Size));
                ArchiveResourcePolicy.ThrowIfExpandedBytesExceeded(declaredBytes);

                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    continue;
                }

                await using Stream input = await entry
                    .OpenEntryStreamAsync(cancellationToken)
                    .ConfigureAwait(false);

                while (true)
                {
                    int read = await input
                        .ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)
                        .ConfigureAwait(false);

                    if (read == 0)
                    {
                        break;
                    }

                    validatedBytes = ArchiveResourcePolicy.SaturatingAdd(validatedBytes, read);
                    ArchiveResourcePolicy.ThrowIfExpandedBytesExceeded(validatedBytes);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ArchiveIntegrityResult
            {
                IsValid = false,
                WasCancelled = true,
                MessageResourceKey = UserCancelledKey
            };
        }
        catch (ArchiveResourceLimitException ex)
        {
            Logger.Warning(ex, "Archive integrity validation stopped by the resource policy. Path={Path}", archivePath);

            return new ArchiveIntegrityResult
            {
                IsValid = false,
                MessageResourceKey = ArchiveResourcePolicy.ResourceLimitMessageKey
            };
        }
        catch (IOException ex)
        {
            Logger.Debug(ex, "Archive integrity validation failed due to I/O. Path={Path}", archivePath);

            return new ArchiveIntegrityResult
            {
                IsValid = false,
                MessageResourceKey = ArchiveVerificationFailedKey
            };
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Debug(ex, "Archive integrity validation failed due to access. Path={Path}", archivePath);

            return new ArchiveIntegrityResult
            {
                IsValid = false,
                MessageResourceKey = ArchiveVerificationFailedKey
            };
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Archive integrity validation failed. Path={Path}", archivePath);

            return new ArchiveIntegrityResult
            {
                IsValid = false,
                MessageResourceKey = ArchiveVerificationFailedKey
            };
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return new ArchiveIntegrityResult
        {
            IsValid = true
        };
    }

    public Task<ArchiveExtractionResult> ExtractFirstChdAsync(
        string archivePath,
        string destinationDirectory,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!SevenZipExtractor.IsAvailable)
        {
            return Task.FromResult(CreateSevenZipUnavailableResult());
        }

        return SevenZipExtractor.ExtractFirstChdAsync(
            archivePath,
            destinationDirectory,
            progress,
            cancellationToken);
    }

    public Task<ArchiveExtractionResult> ExtractFirstConvertibleDiscImageAsync(
        string archivePath,
        string destinationDirectory,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!SevenZipExtractor.IsAvailable)
        {
            return Task.FromResult(CreateSevenZipUnavailableResult());
        }

        return SevenZipExtractor.ExtractFirstConvertibleDiscImageAsync(
            archivePath,
            destinationDirectory,
            progress,
            cancellationToken);
    }

    public Task<ArchiveExtractionResult> ExtractFirstSupportedDiscFileAsync(
        string archivePath,
        string destinationDirectory,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ExtractFirstConvertibleDiscImageAsync(
            archivePath,
            destinationDirectory,
            progress,
            cancellationToken);
    }

    private static ArchiveExtractionResult CreateSevenZipUnavailableResult() => new()
    {
        IsSuccess = false,
        ExitCode = -1,
        Message = SevenZipUnavailableMessageKey
    };
}
