using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using Serilog;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace HakamiqChdTool.App.Services;

public sealed class ArchiveContentPreviewService
{
    private const long MaxDescriptorPreviewBytes = 4L * 1024L * 1024L;

    private const string PreviewUnreadableKey = "LocArchive_PreviewUnreadable";
    private const string PasswordProtectedKey = "LocArchive_PasswordProtected";
    private const string WillUnpackThenConvertKey = "LocArchive_WillUnpackThenConvert";
    private const string ContainsOnlyChdKey = "LocArchive_ContainsOnlyChd";
    private const string NoConvertibleDiscImageKey = "LocArchive_NoConvertibleDiscImage";

    private static readonly ILogger Logger = Log.ForContext<ArchiveContentPreviewService>();
    private readonly SevenZipArchiveInspector _sevenZipInspector = new();

    [SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "Signature is preserved for existing positional queue intake callers.")]
    public ArchiveContentPreviewResult PreviewForIntake(
        string archivePath,
        QueueIntakeSource intakeSource,
        CancellationToken cancellationToken = default,
        IDictionary<ArchiveInspectionCacheKey, ArchiveContentPreviewResult>? previewCache = null,
        IDictionary<ArchiveInspectionCacheKey, SevenZipProcessResult>? sevenZipListingCache = null)
    {
        if (!ArchivePreviewIntakePolicy.AllowsArchivePreview(intakeSource))
        {
            Logger.Debug(
                "Archive preview blocked by intake policy. Source={IntakeSource} Archive={Archive}",
                intakeSource,
                archivePath);

            return new ArchiveContentPreviewResult
            {
                MessageResourceKey = MainWindowMessages.ArchiveAwaitingPreviewAtStartup
            };
        }

        if (ArchiveInspectionCacheKey.TryCreate(archivePath, out ArchiveInspectionCacheKey cacheKey)
            && previewCache is not null
            && previewCache.TryGetValue(cacheKey, out ArchiveContentPreviewResult? cached))
        {
            return cached;
        }

        ArchiveContentPreviewResult result = PreviewUnchecked(archivePath, sevenZipListingCache, cancellationToken);
        if (previewCache is not null
            && ArchiveInspectionCacheKey.TryCreate(archivePath, out cacheKey)
            && !result.WasCancelled)
        {
            previewCache[cacheKey] = result;
        }

        return result;
    }

    private ArchiveContentPreviewResult PreviewUnchecked(
        string archivePath,
        IDictionary<ArchiveInspectionCacheKey, SevenZipProcessResult>? sevenZipListingCache,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            return new ArchiveContentPreviewResult
            {
                IsUnreadable = true,
                MessageResourceKey = PreviewUnreadableKey
            };
        }

        if (ShouldPreferSevenZipForPreview(archivePath))
        {
            if (!_sevenZipInspector.IsAvailable)
            {
                return new ArchiveContentPreviewResult
                {
                    IsUnreadable = true,
                    MessageResourceKey = PreviewUnreadableKey
                };
            }

            try
            {
                return _sevenZipInspector.PreviewAsync(archivePath, sevenZipListingCache, cancellationToken)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (OperationCanceledException ex)
            {
                Logger.Debug(ex, "7-Zip archive preview cancelled. Archive={Archive}", archivePath);
                return ArchiveContentPreviewResult.Cancelled();
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "7-Zip archive preview failed for 7z/rar intake. Archive={Archive}", archivePath);
                return new ArchiveContentPreviewResult
                {
                    IsUnreadable = true,
                    MessageResourceKey = PreviewUnreadableKey
                };
            }
        }

        ArchiveContentPreviewResult sharpPreview = PreviewWithSharpCompress(archivePath, cancellationToken);
        if (!sharpPreview.IsUnreadable || !_sevenZipInspector.IsAvailable)
        {
            return sharpPreview;
        }

        try
        {
            return _sevenZipInspector.PreviewAsync(archivePath, sevenZipListingCache, cancellationToken)
                .GetAwaiter()
                .GetResult();
        }
        catch (OperationCanceledException ex)
        {
            Logger.Debug(ex, "7-Zip archive preview fallback cancelled. Archive={Archive}", archivePath);
            return ArchiveContentPreviewResult.Cancelled();
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "7-Zip archive preview fallback failed. Archive={Archive}", archivePath);
            return sharpPreview;
        }
    }

    private static ArchiveContentPreviewResult PreviewWithSharpCompress(
        string archivePath,
        CancellationToken cancellationToken)
    {
        ReaderOptions readerOptions = new()
        {
            LookForHeader = true,
            ArchiveEncoding = new ArchiveEncoding()
        };

        try
        {
            using IArchive archive = ArchiveFactory.OpenArchive(new FileInfo(archivePath), readerOptions);

            List<IArchiveEntry> entries =
            [
                .. archive.Entries
                    .Where(entry => !entry.IsDirectory)
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            ];

            cancellationToken.ThrowIfCancellationRequested();

            if (entries.Any(IsEncryptedEntry))
            {
                return new ArchiveContentPreviewResult
                {
                    RequiresPassword = true,
                    MessageResourceKey = PasswordProtectedKey
                };
            }

            string[] entryPaths =
            [
                .. entries
                    .Select(entry => entry.Key ?? string.Empty)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
            ];

            if (entryPaths.Length == 0)
            {
                return new ArchiveContentPreviewResult
                {
                    MessageResourceKey = ArchiveCandidateDiscovery.EmptyArchiveMessageResourceKey
                };
            }

            int convertibleLeaderCount = ArchiveCandidateDiscovery.CountEffectiveConvertibleLeaderPaths(entryPaths);
            if (convertibleLeaderCount > 1)
            {
                return new ArchiveContentPreviewResult
                {
                    MessageResourceKey = ArchiveCandidateDiscovery.MultipleConvertibleImageSetsMessageResourceKey
                };
            }

            string[] convertibleLeaders = ArchiveCandidateDiscovery.GetEffectiveConvertibleLeaderExtensions(entryPaths);
            if (convertibleLeaderCount == 1 && convertibleLeaders.Length > 0)
            {
                string? leaderPath = ArchiveCandidateDiscovery.FindFirstEffectiveConvertibleLeaderPath(entryPaths);
                if (string.IsNullOrWhiteSpace(leaderPath))
                {
                    return new ArchiveContentPreviewResult
                    {
                        IsUnreadable = true,
                        MessageResourceKey = PreviewUnreadableKey
                    };
                }

                if (!TryValidateDescriptorDependencies(
                        entries,
                        leaderPath,
                        entryPaths,
                        cancellationToken,
                        out string descriptorFailureKey))
                {
                    return new ArchiveContentPreviewResult
                    {
                        MessageResourceKey = descriptorFailureKey
                    };
                }

                return new ArchiveContentPreviewResult
                {
                    CanUnpackThenConvert = true,
                    MessageResourceKey = WillUnpackThenConvertKey,
                    ConvertibleLeaderExtensions = convertibleLeaders
                };
            }

            if (ArchiveCandidateDiscovery.HasUnsupportedDiscImagePath(entryPaths))
            {
                return new ArchiveContentPreviewResult
                {
                    MessageResourceKey = ArchiveCandidateDiscovery.UnsupportedDiscImageMessageResourceKey
                };
            }

            bool containsOnlyChd = entries.Count > 0 && entries.All(entry => string.Equals(
                Path.GetExtension(entry.Key ?? string.Empty),
                ".chd",
                StringComparison.OrdinalIgnoreCase));

            return new ArchiveContentPreviewResult
            {
                ContainsOnlyChd = containsOnlyChd,
                MessageResourceKey = containsOnlyChd
                    ? ContainsOnlyChdKey
                    : NoConvertibleDiscImageKey
            };
        }
        catch (OperationCanceledException ex)
        {
            Logger.Debug(ex, "Archive preview cancelled. Archive={Archive}", archivePath);
            return ArchiveContentPreviewResult.Cancelled();
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Archive preview failed. Archive={Archive}", archivePath);

            return new ArchiveContentPreviewResult
            {
                IsUnreadable = true,
                MessageResourceKey = PreviewUnreadableKey
            };
        }
    }

    private static bool TryValidateDescriptorDependencies(
        IReadOnlyList<IArchiveEntry> entries,
        string leaderPath,
        IReadOnlyList<string> entryPaths,
        CancellationToken cancellationToken,
        out string failureMessageResourceKey)
    {
        failureMessageResourceKey = string.Empty;

        string extension = Path.GetExtension(leaderPath);
        if (!string.Equals(extension, ".cue", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".gdi", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".toc", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        IArchiveEntry? descriptorEntry = entries.FirstOrDefault(entry =>
            string.Equals(
                ArchiveCandidateDiscovery.NormalizeLookupKey(entry.Key),
                ArchiveCandidateDiscovery.NormalizeLookupKey(leaderPath),
                StringComparison.OrdinalIgnoreCase));

        if (descriptorEntry is null)
        {
            failureMessageResourceKey = ArchiveCandidateDiscovery.DescriptorMissingDependenciesMessageResourceKey;
            return false;
        }

        string descriptorText;
        try
        {
            descriptorText = ReadSmallTextEntry(descriptorEntry, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            failureMessageResourceKey = ArchiveCandidateDiscovery.DescriptorUnreadableMessageResourceKey;
            return false;
        }

        return ArchiveCandidateDiscovery.TryValidateDescriptorDependencies(
            leaderPath,
            descriptorText,
            entryPaths,
            out failureMessageResourceKey);
    }

    private static string ReadSmallTextEntry(
        IArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        if (entry.Size > MaxDescriptorPreviewBytes)
        {
            throw new InvalidDataException();
        }

        using Stream stream = entry.OpenEntryStream();
        using MemoryStream buffer = new();

        byte[] chunk = new byte[8192];
        long totalRead = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int read = stream.Read(chunk, 0, chunk.Length);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
            if (totalRead > MaxDescriptorPreviewBytes)
            {
                throw new InvalidDataException();
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static bool ShouldPreferSevenZipForPreview(string archivePath)
    {
        string extension = Path.GetExtension(archivePath);

        return extension.Equals(".7z", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".rar", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEncryptedEntry(IArchiveEntry entry)
    {
        try
        {
            return entry.IsEncrypted;
        }
        catch
        {
            return false;
        }
    }
}
