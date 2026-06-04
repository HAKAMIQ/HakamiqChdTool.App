using Serilog;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public sealed class ArchiveExtractionService
{
    private const int BufferSize = 1024 * 1024;
    private const long MaxDescriptorTextBytes = 4L * 1024L * 1024L;

    private const string ArchiveVerificationFailedKey = "LocArchive_VerificationFailed";
    private const string ArchiveUnreadableKey = "LocArchive_PreviewUnreadable";
    private const string ArchivePasswordProtectedKey = "LocArchive_PasswordProtected";
    private const string ArchiveNoConvertibleDiscImageKey = "LocArchive_NoConvertibleDiscImage";
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

            foreach (IArchiveEntry entry in archive.Entries.Where(entry => !entry.IsDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();

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
        if (SevenZipExtractor.IsAvailable)
        {
            return SevenZipExtractor.ExtractFirstChdAsync(
                archivePath,
                destinationDirectory,
                progress,
                cancellationToken);
        }

        return ExtractAsync(
            archivePath,
            destinationDirectory,
            [".chd"],
            extractArchiveBundle: false,
            progress,
            cancellationToken);
    }

    public Task<ArchiveExtractionResult> ExtractFirstConvertibleDiscImageAsync(
        string archivePath,
        string destinationDirectory,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (SevenZipExtractor.IsAvailable)
        {
            return SevenZipExtractor.ExtractFirstConvertibleDiscImageAsync(
                archivePath,
                destinationDirectory,
                progress,
                cancellationToken);
        }

        return ExtractAsync(
            archivePath,
            destinationDirectory,
            ArchiveCandidateDiscovery.ConvertibleLeaderPriority,
            extractArchiveBundle: true,
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

    private static async Task<ArchiveExtractionResult> ExtractAsync(
        string archivePath,
        string destinationDirectory,
        IReadOnlyList<string> preferredExtensions,
        bool extractArchiveBundle,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        if (!TryValidateInputs(archivePath, destinationDirectory))
        {
            return new ArchiveExtractionResult
            {
                IsSuccess = false,
                ExitCode = -1,
                Message = ArchiveUnreadableKey
            };
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new ArchiveExtractionResult
            {
                IsSuccess = false,
                WasCancelled = true,
                ExitCode = ChdmanProcessRunner.CanceledExitCode,
                Message = UserCancelledKey
            };
        }

        Directory.CreateDirectory(destinationDirectory);
        progress?.Report(0);

        ReaderOptions readerOptions = new()
        {
            LookForHeader = true,
            ArchiveEncoding = new ArchiveEncoding()
        };

        List<string> materializedPaths = [];
        string? currentOutputPath = null;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            using IArchive archive = ArchiveFactory.OpenArchive(new FileInfo(archivePath), readerOptions);

            cancellationToken.ThrowIfCancellationRequested();

            List<IArchiveEntry> entries = archive.Entries
                .Where(entry => !entry.IsDirectory)
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
                .ToList();

            cancellationToken.ThrowIfCancellationRequested();

            if (entries.Any(IsEncryptedEntry))
            {
                return new ArchiveExtractionResult
                {
                    IsSuccess = false,
                    RequiresPassword = true,
                    ExitCode = -1,
                    Message = ArchivePasswordProtectedKey
                };
            }

            string[] entryKeys = entries
                .Select(entry => entry.Key ?? string.Empty)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToArray();

            if (entryKeys.Length == 0)
            {
                return new ArchiveExtractionResult
                {
                    IsSuccess = false,
                    ExitCode = -1,
                    Message = ArchiveCandidateDiscovery.EmptyArchiveMessageResourceKey
                };
            }

            if (extractArchiveBundle && ArchiveCandidateDiscovery.HasMultipleEffectiveConvertibleLeaderPaths(entryKeys))
            {
                return new ArchiveExtractionResult
                {
                    IsSuccess = false,
                    ExitCode = -1,
                    Message = ArchiveCandidateDiscovery.MultipleConvertibleImageSetsMessageResourceKey
                };
            }

            IArchiveEntry? primaryEntry = FindPrimaryEntry(entries, preferredExtensions);
            if (primaryEntry is null)
            {
                return new ArchiveExtractionResult
                {
                    IsSuccess = false,
                    ExitCode = -1,
                    Message = ArchiveCandidateDiscovery.HasUnsupportedDiscImagePath(entryKeys)
                        ? ArchiveCandidateDiscovery.UnsupportedDiscImageMessageResourceKey
                        : ArchiveNoConvertibleDiscImageKey
                };
            }

            IReadOnlyList<IArchiveEntry> entriesToExtract = extractArchiveBundle
                ? await BuildBundleEntriesAsync(primaryEntry, entries, cancellationToken).ConfigureAwait(false)
                : [primaryEntry];

            long totalBytes = Math.Max(1, entriesToExtract.Sum(entry => Math.Max(0, entry.Size)));
            long writtenBytes = 0;
            List<string> extractedFiles = new(entriesToExtract.Count);

            foreach (IArchiveEntry entry in entriesToExtract)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string entryKey = GetEntryKeyOrThrow(entry);
                string outputPath = BuildSafeExtractionPath(destinationDirectory, entryKey);
                currentOutputPath = outputPath;
                TrackMaterializedPath(materializedPaths, outputPath);

                string? directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await using Stream input = await entry
                    .OpenEntryStreamAsync(cancellationToken)
                    .ConfigureAwait(false);

                await using FileStream output = new(
                    outputPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                while (true)
                {
                    int read = await input
                        .ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)
                        .ConfigureAwait(false);

                    if (read == 0)
                    {
                        break;
                    }

                    await output
                        .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);

                    writtenBytes += read;

                    int percent = (int)Math.Clamp((writtenBytes * 100.0) / totalBytes, 0, 100);
                    progress?.Report(percent);
                }

                extractedFiles.Add(outputPath);
                currentOutputPath = null;
            }

            string extractedPath = BuildSafeExtractionPath(
                destinationDirectory,
                GetEntryKeyOrThrow(primaryEntry));

            if (extractArchiveBundle
                && !ArchiveCandidateDiscovery.TryValidateExtractedDescriptorDependencies(
                    extractedPath,
                    extractedFiles,
                    out string dependencyFailureMessage))
            {
                CleanupMaterializedExtraction(destinationDirectory, materializedPaths, currentOutputPath);

                return new ArchiveExtractionResult
                {
                    IsSuccess = false,
                    ExitCode = -1,
                    ExtractedFiles = extractedFiles,
                    Output = string.Join(Environment.NewLine, extractedFiles),
                    Message = dependencyFailureMessage
                };
            }

            progress?.Report(100);

            return new ArchiveExtractionResult
            {
                IsSuccess = true,
                RequiresPassword = false,
                ExitCode = 0,
                ExtractedPath = extractedPath,
                ExtractedFiles = extractedFiles,
                Output = string.Join(Environment.NewLine, extractedFiles),
                Message = string.Empty
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CleanupMaterializedExtraction(destinationDirectory, materializedPaths, currentOutputPath);

            return new ArchiveExtractionResult
            {
                IsSuccess = false,
                WasCancelled = true,
                ExitCode = ChdmanProcessRunner.CanceledExitCode,
                Message = UserCancelledKey
            };
        }
        catch (InvalidDataException ex)
        {
            CleanupMaterializedExtraction(destinationDirectory, materializedPaths, currentOutputPath);

            Logger.Warning(
                ex,
                "Archive extraction rejected descriptor dependencies. Archive={ArchivePath}, Destination={DestinationDirectory}",
                archivePath,
                destinationDirectory);

            return new ArchiveExtractionResult
            {
                IsSuccess = false,
                ExitCode = -1,
                Message = ArchiveCandidateDiscovery.DescriptorMissingDependenciesMessageResourceKey
            };
        }
        catch (IOException ex)
        {
            CleanupMaterializedExtraction(destinationDirectory, materializedPaths, currentOutputPath);

            Logger.Warning(
                ex,
                "Archive extraction failed due to I/O. Archive={ArchivePath}, Destination={DestinationDirectory}",
                archivePath,
                destinationDirectory);

            return new ArchiveExtractionResult
            {
                IsSuccess = false,
                ExitCode = -1,
                Message = ArchiveUnreadableKey
            };
        }
        catch (UnauthorizedAccessException ex)
        {
            CleanupMaterializedExtraction(destinationDirectory, materializedPaths, currentOutputPath);

            Logger.Warning(
                ex,
                "Archive extraction failed due to access. Archive={ArchivePath}, Destination={DestinationDirectory}",
                archivePath,
                destinationDirectory);

            return new ArchiveExtractionResult
            {
                IsSuccess = false,
                ExitCode = -1,
                Message = ArchiveUnreadableKey
            };
        }
        catch (Exception ex)
        {
            CleanupMaterializedExtraction(destinationDirectory, materializedPaths, currentOutputPath);

            Logger.Warning(
                ex,
                "Archive extraction failed. Archive={ArchivePath}, Destination={DestinationDirectory}",
                archivePath,
                destinationDirectory);

            return new ArchiveExtractionResult
            {
                IsSuccess = false,
                ExitCode = -1,
                Message = ArchiveUnreadableKey
            };
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<IReadOnlyList<IArchiveEntry>> BuildBundleEntriesAsync(
        IArchiveEntry primaryEntry,
        IReadOnlyList<IArchiveEntry> allEntries,
        CancellationToken cancellationToken)
    {
        string primaryKey = GetEntryKeyOrThrow(primaryEntry);
        string primaryExtension = Path.GetExtension(primaryKey).ToLowerInvariant();

        if (primaryExtension is not (".cue" or ".gdi" or ".toc"))
        {
            return [primaryEntry];
        }

        string primaryText = await ReadEntryTextAsync(primaryEntry, cancellationToken).ConfigureAwait(false);
        string primaryDirectory = ArchiveCandidateDiscovery.NormalizeDirectoryKey(Path.GetDirectoryName(primaryKey));

        HashSet<string> requiredTrackKeys = primaryExtension switch
        {
            ".cue" => ArchiveCandidateDiscovery.ParseCueReferencedKeys(primaryText, primaryDirectory),
            ".gdi" => ArchiveCandidateDiscovery.ParseGdiReferencedKeys(primaryText, primaryDirectory),
            ".toc" => ArchiveCandidateDiscovery.ParseTocReferencedKeys(primaryText, primaryDirectory),
            _ => []
        };

        if (requiredTrackKeys.Count == 0)
        {
            throw new InvalidDataException();
        }

        HashSet<string> keysToExtract = new(requiredTrackKeys, StringComparer.OrdinalIgnoreCase)
        {
            ArchiveCandidateDiscovery.NormalizeLookupKey(primaryKey)
        };

        if (primaryExtension == ".cue")
        {
            string? primaryDirectoryPrefix = string.IsNullOrWhiteSpace(primaryDirectory)
                ? null
                : primaryDirectory + "/";

            string sbiKey = (primaryDirectoryPrefix ?? string.Empty)
                + Path.GetFileNameWithoutExtension(primaryKey)
                + ".sbi";

            keysToExtract.Add(ArchiveCandidateDiscovery.NormalizeLookupKey(sbiKey));
        }

        List<IArchiveEntry> relatedEntries = allEntries
            .Where(entry => IsBundleEntryMatch(entry, keysToExtract))
            .GroupBy(entry => ArchiveCandidateDiscovery.NormalizeLookupKey(GetEntryKeyOrThrow(entry)))
            .Select(group => group.First())
            .ToList();

        HashSet<string> availableRequiredTracks = relatedEntries
            .Select(entry => ArchiveCandidateDiscovery.NormalizeLookupKey(GetEntryKeyOrThrow(entry)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (requiredTrackKeys.Any(required => !availableRequiredTracks.Contains(required)))
        {
            throw new InvalidDataException();
        }

        if (!relatedEntries.Any(entry => string.Equals(
                GetEntryKeyOrThrow(entry),
                primaryKey,
                StringComparison.OrdinalIgnoreCase)))
        {
            relatedEntries.Insert(0, primaryEntry);
        }

        return relatedEntries;
    }

    private static bool IsBundleEntryMatch(
        IArchiveEntry entry,
        HashSet<string> requiredKeys)
    {
        string normalizedKey = ArchiveCandidateDiscovery.NormalizeLookupKey(GetEntryKeyOrThrow(entry));

        return requiredKeys.Contains(normalizedKey);
    }

    private static async Task<string> ReadEntryTextAsync(
        IArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        if (entry.Size > MaxDescriptorTextBytes)
        {
            throw new InvalidDataException();
        }

        await using Stream stream = await entry
            .OpenEntryStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        using MemoryStream buffer = new();
        byte[] chunk = ArrayPool<byte>.Shared.Rent(8192);

        try
        {
            long totalRead = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int read = await stream
                    .ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                totalRead += read;
                if (totalRead > MaxDescriptorTextBytes)
                {
                    throw new InvalidDataException();
                }

                await buffer
                    .WriteAsync(chunk.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
    }

    private static bool TryValidateInputs(
        string archivePath,
        string destinationDirectory)
    {
        return !string.IsNullOrWhiteSpace(archivePath)
            && !string.IsNullOrWhiteSpace(destinationDirectory)
            && File.Exists(archivePath);
    }

    private static bool IsEncryptedEntry(IArchiveEntry entry)
    {
        try
        {
            return entry.IsEncrypted;
        }
        catch (Exception ex)
        {
            Logger.Debug(
                ex,
                "Archive encryption state could not be read. Entry={Entry}",
                entry.Key);

            return false;
        }
    }

    private static IArchiveEntry? FindPrimaryEntry(
        IEnumerable<IArchiveEntry> entries,
        IReadOnlyList<string> preferredExtensions)
    {
        foreach (string extension in preferredExtensions)
        {
            IArchiveEntry? match = entries.FirstOrDefault(entry => string.Equals(
                Path.GetExtension(entry.Key ?? string.Empty),
                extension,
                StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static void TrackMaterializedPath(
        List<string> materializedPaths,
        string outputPath)
    {
        if (materializedPaths.Contains(outputPath, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        materializedPaths.Add(outputPath);
    }

    private static void CleanupMaterializedExtraction(
        string destinationDirectory,
        IReadOnlyList<string> materializedPaths,
        string? currentOutputPath)
    {
        IEnumerable<string> candidates = materializedPaths;

        if (!string.IsNullOrWhiteSpace(currentOutputPath)
            && !materializedPaths.Contains(currentOutputPath, StringComparer.OrdinalIgnoreCase))
        {
            candidates = candidates.Concat([currentOutputPath]);
        }

        foreach (string path in candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => path.Length))
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException ex)
            {
                Logger.Debug(ex, "Archive cleanup could not delete extracted file. Path={Path}", path);
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.Debug(ex, "Archive cleanup access denied. Path={Path}", path);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Archive cleanup failed while deleting extracted file. Path={Path}", path);
            }

            DeleteEmptyParentDirectories(path, destinationDirectory);
        }
    }

    private static void DeleteEmptyParentDirectories(
        string filePath,
        string destinationDirectory)
    {
        string fullRoot;
        try
        {
            fullRoot = Path.GetFullPath(destinationDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (IsPathException(ex))
        {
            Logger.Debug(ex, "Archive cleanup could not resolve destination root. Destination={DestinationDirectory}", destinationDirectory);
            return;
        }

        string? currentDirectory;
        try
        {
            currentDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        }
        catch (Exception ex) when (IsPathException(ex))
        {
            Logger.Debug(ex, "Archive cleanup could not resolve extracted file directory. Path={Path}", filePath);
            return;
        }

        while (!string.IsNullOrWhiteSpace(currentDirectory)
               && IsSameOrChildDirectory(currentDirectory, fullRoot))
        {
            try
            {
                if (Directory.Exists(currentDirectory)
                    && !Directory.EnumerateFileSystemEntries(currentDirectory).Any())
                {
                    Directory.Delete(currentDirectory, recursive: false);

                    if (string.Equals(currentDirectory, fullRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    currentDirectory = Path.GetDirectoryName(currentDirectory);
                    continue;
                }
            }
            catch (IOException ex)
            {
                Logger.Debug(ex, "Archive cleanup could not delete empty directory. Directory={Directory}", currentDirectory);
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.Debug(ex, "Archive cleanup access denied for empty directory. Directory={Directory}", currentDirectory);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Archive cleanup failed while deleting empty directory. Directory={Directory}", currentDirectory);
            }

            break;
        }
    }

    private static string GetEntryKeyOrThrow(IArchiveEntry entry)
    {
        string? entryKey = entry.Key;

        if (string.IsNullOrWhiteSpace(entryKey))
        {
            throw new InvalidDataException();
        }

        return entryKey;
    }

    private static string BuildSafeExtractionPath(
        string root,
        string entryKey)
    {
        if (Path.IsPathRooted(entryKey))
        {
            throw new InvalidDataException();
        }

        string normalized = entryKey
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        string fullRoot = Path.GetFullPath(root);
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, normalized));

        if (!IsSameOrChildDirectory(fullPath, fullRoot))
        {
            throw new InvalidDataException();
        }

        return fullPath;
    }

    private static bool IsSameOrChildDirectory(
        string path,
        string root)
    {
        string fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(
                   fullRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(
                   fullRoot + Path.AltDirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathException(Exception ex)
    {
        return ex is ArgumentException
            or NotSupportedException
            or PathTooLongException;
    }
}