using HakamiqChdTool.App.Core.Disc;
using HakamiqChdTool.App.Models;
using Serilog;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace HakamiqChdTool.App.Services;

public sealed class RedumpV2Engine
{
    private const int BufferSize = 1024 * 1024;
    private const int TotalSteps = 6;

    private const string StatusVerifiedKey = "LocDeepHash_StatusVerified";
    private const string StatusVerifiedCompleteKey = "LocDeepHash_StatusVerifiedComplete";
    private const string StatusVerifiedNormalizedKey = "LocDeepHash_StatusVerifiedNormalized";
    private const string StatusIncompleteKey = "LocDeepHash_StatusIncomplete";
    private const string StatusModifiedKey = "LocDeepHash_StatusModified";
    private const string StatusNoDatabaseKey = "LocDeepHash_StatusNoDatabase";
    private const string StatusUnsupportedKey = "LocDeepHash_StatusUnsupported";
    private const string StatusErrorKey = "LocDeepHash_StatusError";
    private const string StatusInputReadFailureKey = "LocDeepHash_StatusInputReadFailure";

    private const string TipVerifiedHeaderKey = "LocDeepHash_TipVerifiedHeader";
    private const string TipPartialMatchKey = "LocDeepHash_TipPartialMatch";
    private const string TipNoRedumpMatchKey = "LocDeepHash_TipNoRedumpMatch";
    private const string TipNoDatabaseKey = "LocDeepHash_TipNoDatabase";
    private const string TipNoTrackFilesKey = "LocDeepHash_TipNoTrackFiles";
    private const string TipHashFailedKey = "LocDeepHash_TipHashFailed";
    private const string TipInputReadFailureKey = "LocDeepHash_TipInputReadCrcOrIoFailure";
    private const string TipUnsupportedExtensionKey = "LocDeepHash_TipUnsupportedExtension";
    private const string TipConflictingMatchesKey = "LocDeepHash_TipConflictingMatches";

    private const string StepClassifyKey = "LocRedumpV2_StepClassifySource";
    private const string StepNormalizeKey = "LocRedumpV2_StepNormalize";
    private const string StepHashKey = "LocRedumpV2_StepHash";
    private const string StepMatchKey = "LocRedumpV2_StepMatch";
    private const string StepCleanupKey = "LocRedumpV2_StepCleanup";
    private const string StepReturnKey = "LocRedumpV2_StepReturnResult";
    private const string NormalizeCsoKey = "LocRedumpV2_NormalizeCsoIso";
    private const string NormalizeChdCdKey = "LocRedumpV2_NormalizeChdCd";
    private const string NormalizeChdDvdKey = "LocRedumpV2_NormalizeChdDvd";
    private const string NormalizeArchiveKey = "LocRedumpV2_NormalizeArchive";
    private const string NormalizeDolphinUnavailableKey = "LocRedumpV2_NormalizeDolphinUnavailable";
    private const string Ps3JbIrdOnlyKey = "LocRedumpV2_Ps3JbIrdOnly";
    private const string Ps3DecryptedNotOriginalKey = "LocRedumpV2_Ps3DecryptedNotOriginal";
    private const string NormalizedDetailKey = "LocRedumpV2_DetailNormalizedFormat";
    private const string ChdUnknownKey = "LocWorkflow_UnknownChdExtraction";

    private static readonly HashSet<string> HashableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cue", ".gdi", ".iso", ".gcm", ".bin", ".img", ".raw"
    };

    public static RedumpV2Engine Default { get; } = new();

    private readonly RedumpV2Classifier _classifier;
    private readonly ChdInfoService _chdInfo;
    private readonly ArchiveExtractionService _archiveExtraction;
    private readonly ICsoPreprocessor _csoPreprocessor;

    public RedumpV2Engine()
        : this(new RedumpV2Classifier(), new ChdInfoService(), new ArchiveExtractionService(), new CsoPreprocessor())
    {
    }

    public RedumpV2Engine(
        RedumpV2Classifier classifier,
        ChdInfoService chdInfo,
        ArchiveExtractionService archiveExtraction,
        ICsoPreprocessor csoPreprocessor)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _chdInfo = chdInfo ?? throw new ArgumentNullException(nameof(chdInfo));
        _archiveExtraction = archiveExtraction ?? throw new ArgumentNullException(nameof(archiveExtraction));
        _csoPreprocessor = csoPreprocessor ?? throw new ArgumentNullException(nameof(csoPreprocessor));
    }

    public async Task<RedumpV2ScanResult> ScanAsync(
        string inputPath,
        RedumpSqliteManager? redumpDatabase,
        RedumpV2ScanOptions options,
        IProgress<ProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentNullException.ThrowIfNull(options);

        Guid operationId = Guid.NewGuid();
        string fullPath = Path.GetFullPath(inputPath.Trim());
        string itemName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var cleanupRoots = new List<string>();
        bool suppressFinalProgress = false;

        RedumpSourceClassification classification = new(
            fullPath,
            RedumpSourceFormat.Unknown,
            IsDirectory: Directory.Exists(fullPath),
            itemName,
            SourceBytes: 0);

        try
        {
            Report(progress, operationId, ProgressOperationType.RedumpScan, itemName, 1, 0, 0, 0, StepClassifyKey);
            classification = _classifier.Classify(fullPath, cancellationToken);
            itemName = string.IsNullOrWhiteSpace(classification.DisplayName) ? itemName : classification.DisplayName;

            if (classification.SourceFormat == RedumpSourceFormat.Unknown)
            {
                return BuildTerminalResult(
                    RedumpV2ResultState.Unsupported,
                    fullPath,
                    classification.SourceFormat,
                    RedumpNormalizedFormat.Unsupported,
                    usedTemporaryNormalization: false,
                    requiredTempSpaceBytes: 0,
                    StatusUnsupportedKey,
                    TipUnsupportedExtensionKey,
                    [Path.GetExtension(fullPath), fullPath]);
            }

            if (classification.SourceFormat == RedumpSourceFormat.Ps3JbFolder)
            {
                return BuildTerminalResult(
                    RedumpV2ResultState.Unsupported,
                    fullPath,
                    classification.SourceFormat,
                    RedumpNormalizedFormat.IrdOnly,
                    usedTemporaryNormalization: false,
                    requiredTempSpaceBytes: 0,
                    StatusUnsupportedKey,
                    Ps3JbIrdOnlyKey);
            }

            if (classification.SourceFormat == RedumpSourceFormat.DecryptedPs3Iso)
            {
                return BuildTerminalResult(
                    RedumpV2ResultState.Unsupported,
                    fullPath,
                    classification.SourceFormat,
                    RedumpNormalizedFormat.Unsupported,
                    usedTemporaryNormalization: false,
                    requiredTempSpaceBytes: 0,
                    StatusUnsupportedKey,
                    Ps3DecryptedNotOriginalKey);
            }

            if (redumpDatabase is null || !redumpDatabase.HasAnyRows())
            {
                return BuildTerminalResult(
                    RedumpV2ResultState.NoDatabase,
                    fullPath,
                    classification.SourceFormat,
                    RedumpNormalizedFormat.Unsupported,
                    usedTemporaryNormalization: false,
                    requiredTempSpaceBytes: 0,
                    StatusNoDatabaseKey,
                    TipNoDatabaseKey);
            }

            Report(progress, operationId, ProgressOperationType.RedumpScan, itemName, 2, 0, 0, 0, StepNormalizeKey);
            NormalizationOutcome normalization = await NormalizeAsync(
                    classification,
                    options,
                    cleanupRoots,
                    operationId,
                    itemName,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!normalization.IsSuccess)
            {
                return BuildTerminalResult(
                    normalization.State,
                    fullPath,
                    classification.SourceFormat,
                    normalization.Format,
                    normalization.UsedTemporaryNormalization,
                    normalization.RequiredTempSpaceBytes,
                    normalization.StatusMessageKey,
                    normalization.DetailMessageKey,
                    normalization.DetailArgs);
            }

            Report(progress, operationId, ProgressOperationType.Hashing, itemName, 3, 0, 0, 0, StepHashKey);
            IReadOnlyList<string> filesToHash = ResolveFilesToHash(normalization.CandidatePath);
            if (filesToHash.Count == 0)
            {
                return BuildTerminalResult(
                    RedumpV2ResultState.Error,
                    fullPath,
                    classification.SourceFormat,
                    normalization.Format,
                    normalization.UsedTemporaryNormalization,
                    normalization.RequiredTempSpaceBytes,
                    StatusErrorKey,
                    TipNoTrackFilesKey);
            }

            IReadOnlyList<DeepHashFileDigest> hashed = await HashAllFilesAsync(
                    filesToHash,
                    operationId,
                    itemName,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);

            Report(progress, operationId, ProgressOperationType.RedumpScan, itemName, 4, 0, 0, 0, StepMatchKey);
            return MatchAndAggregate(
                fullPath,
                classification.SourceFormat,
                normalization,
                hashed,
                redumpDatabase);
        }
        catch (OperationCanceledException)
        {
            suppressFinalProgress = true;
            throw;
        }
        catch (Exception ex) when (IsInputReadFailureException(ex))
        {
            Log.Warning(ex, "Redump V2 input read failure. Path={Path}; FailureCode={FailureCode}", fullPath, DeepHashAnalyzer.InputReadCrcOrIoFailureCode);
            return BuildTerminalResult(
                RedumpV2ResultState.Error,
                fullPath,
                classification.SourceFormat,
                RedumpNormalizedFormat.Unsupported,
                usedTemporaryNormalization: cleanupRoots.Count > 0,
                requiredTempSpaceBytes: 0,
                StatusInputReadFailureKey,
                TipInputReadFailureKey,
                failureCode: DeepHashAnalyzer.InputReadCrcOrIoFailureCode);
        }
        catch (Exception ex) when (ex is CryptographicException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException
                                  or InvalidDataException)
        {
            Log.Warning(ex, "Redump V2 scan failed. Path={Path}", fullPath);
            return BuildTerminalResult(
                RedumpV2ResultState.Error,
                fullPath,
                classification.SourceFormat,
                RedumpNormalizedFormat.Unsupported,
                usedTemporaryNormalization: cleanupRoots.Count > 0,
                requiredTempSpaceBytes: 0,
                StatusErrorKey,
                TipHashFailedKey);
        }
        finally
        {
            if (cleanupRoots.Count > 0)
            {
                Report(progress, operationId, ProgressOperationType.RedumpScan, itemName, 5, 0, 0, 0, StepCleanupKey);
                CleanupTemporaryRoots(cleanupRoots);
            }

            if (!suppressFinalProgress && !cancellationToken.IsCancellationRequested)
            {
                Report(progress, operationId, ProgressOperationType.RedumpScan, itemName, 6, 0, 0, 100, StepReturnKey);
            }
        }
    }

    private async Task<NormalizationOutcome> NormalizeAsync(
        RedumpSourceClassification classification,
        RedumpV2ScanOptions options,
        List<string> cleanupRoots,
        Guid operationId,
        string itemName,
        IProgress<ProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return classification.SourceFormat switch
        {
            RedumpSourceFormat.Iso => NormalizationOutcome.Success(
                classification.InputPath,
                RedumpNormalizedFormat.Iso,
                usedTemporaryNormalization: false,
                requiredTempSpaceBytes: 0),

            RedumpSourceFormat.BinCue => NormalizationOutcome.Success(
                ResolveBinCueLeaderPath(classification.InputPath),
                RedumpNormalizedFormat.CueBin,
                usedTemporaryNormalization: false,
                requiredTempSpaceBytes: 0),

            RedumpSourceFormat.Gdi => NormalizationOutcome.Success(
                classification.InputPath,
                RedumpNormalizedFormat.Gdi,
                usedTemporaryNormalization: false,
                requiredTempSpaceBytes: 0),

            RedumpSourceFormat.Cso => await NormalizeCsoAsync(
                    classification,
                    cleanupRoots,
                    operationId,
                    itemName,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false),

            RedumpSourceFormat.Chd => await NormalizeChdAsync(
                    classification,
                    options,
                    cleanupRoots,
                    operationId,
                    itemName,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false),

            RedumpSourceFormat.Archive => await NormalizeArchiveAsync(
                    classification,
                    options,
                    cleanupRoots,
                    operationId,
                    itemName,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false),

            RedumpSourceFormat.Rvz or RedumpSourceFormat.Wbfs or RedumpSourceFormat.Nkit => NormalizationOutcome.Failure(
                RedumpV2ResultState.Unsupported,
                RedumpNormalizedFormat.RawIsoGcm,
                usedTemporaryNormalization: false,
                requiredTempSpaceBytes: 0,
                StatusUnsupportedKey,
                NormalizeDolphinUnavailableKey),

            _ => NormalizationOutcome.Failure(
                RedumpV2ResultState.Unsupported,
                RedumpNormalizedFormat.Unsupported,
                usedTemporaryNormalization: false,
                requiredTempSpaceBytes: 0,
                StatusUnsupportedKey,
                TipUnsupportedExtensionKey,
                [Path.GetExtension(classification.InputPath), classification.InputPath])
        };
    }

    private async Task<NormalizationOutcome> NormalizeCsoAsync(
        RedumpSourceClassification classification,
        List<string> cleanupRoots,
        Guid operationId,
        string itemName,
        IProgress<ProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        string root = CreateTempRoot(operationId, "cso");
        cleanupRoots.Add(root);
        string temporaryIsoPath = Path.Combine(root, "normalized.iso");

        Report(
            progress,
            operationId,
            ProgressOperationType.TemporaryNormalization,
            itemName,
            2,
            0,
            classification.SourceBytes,
            0,
            NormalizeCsoKey);

        CsoPreprocessResult result = await _csoPreprocessor
            .PreprocessAsync(
                classification.InputPath,
                temporaryIsoPath,
                cancellationToken,
                messageKey =>
                {
                    Report(
                        progress,
                        operationId,
                        ProgressOperationType.TemporaryNormalization,
                        itemName,
                        2,
                        0,
                        classification.SourceBytes,
                        percent: 0,
                        messageKey);

                    return Task.CompletedTask;
                })
            .ConfigureAwait(false);

        long requiredTempSpaceBytes = result.EstimatedIsoBytes
            ?? result.PreparedIsoBytes
            ?? TryGetFileSize(temporaryIsoPath);

        if (!result.IsSuccess)
        {
            RedumpV2ResultState state = result.Status == CsoPreprocessStatus.Unsupported
                ? RedumpV2ResultState.Unsupported
                : RedumpV2ResultState.Failed;

            return NormalizationOutcome.Failure(
                state,
                RedumpNormalizedFormat.Iso,
                usedTemporaryNormalization: true,
                requiredTempSpaceBytes,
                state == RedumpV2ResultState.Unsupported ? StatusUnsupportedKey : StatusErrorKey,
                result.MessageKey);
        }

        return NormalizationOutcome.Success(
            result.PreparedIsoPath,
            RedumpNormalizedFormat.Iso,
            usedTemporaryNormalization: true,
            requiredTempSpaceBytes);
    }

    private async Task<NormalizationOutcome> NormalizeChdAsync(
        RedumpSourceClassification classification,
        RedumpV2ScanOptions options,
        List<string> cleanupRoots,
        Guid operationId,
        string itemName,
        IProgress<ProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ChdmanPath) || !File.Exists(options.ChdmanPath))
        {
            return NormalizationOutcome.Failure(
                RedumpV2ResultState.Failed,
                RedumpNormalizedFormat.Unsupported,
                usedTemporaryNormalization: false,
                requiredTempSpaceBytes: 0,
                StatusErrorKey,
                "LocConversion_ChdmanNotFound");
        }

        ChdInfoResult info = await _chdInfo
            .ReadInfoAsync(options.ChdmanPath, classification.InputPath, null, cancellationToken)
            .ConfigureAwait(false);

        if (!info.IsSuccess)
        {
            return NormalizationOutcome.Failure(
                RedumpV2ResultState.Failed,
                RedumpNormalizedFormat.Unsupported,
                usedTemporaryNormalization: false,
                requiredTempSpaceBytes: 0,
                StatusErrorKey,
                string.IsNullOrWhiteSpace(info.Message) ? ChdUnknownKey : info.Message);
        }

        bool isCd = IsCdChd(info.MediaType);
        bool isDvd = IsDvdChd(info.MediaType, info.LogicalBytes);
        if (!isCd && !isDvd)
        {
            return NormalizationOutcome.Failure(
                RedumpV2ResultState.Unsupported,
                RedumpNormalizedFormat.Unsupported,
                usedTemporaryNormalization: false,
                requiredTempSpaceBytes: info.LogicalBytes ?? 0,
                StatusUnsupportedKey,
                ChdUnknownKey);
        }

        string root = CreateTempRoot(operationId, "chd");
        cleanupRoots.Add(root);
        long requiredTempSpaceBytes = info.LogicalBytes ?? classification.SourceBytes;

        if (isCd)
        {
            string cuePath = Path.Combine(root, "normalized.cue");
            string binPath = Path.Combine(root, "normalized.bin");

            Report(progress, operationId, ProgressOperationType.TemporaryNormalization, itemName, 2, 0, requiredTempSpaceBytes, 0, NormalizeChdCdKey);
            bool ok = await RunChdmanExtractionAsync(
                    options.ChdmanPath,
                    classification.InputPath,
                    ["extractcd", "-i", classification.InputPath, "-o", cuePath, "-ob", binPath, "-f"],
                    binPath,
                    operationId,
                    itemName,
                    requiredTempSpaceBytes,
                    NormalizeChdCdKey,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);

            return ok && File.Exists(cuePath)
                ? NormalizationOutcome.Success(cuePath, RedumpNormalizedFormat.CueBin, usedTemporaryNormalization: true, requiredTempSpaceBytes)
                : NormalizationOutcome.Failure(RedumpV2ResultState.Failed, RedumpNormalizedFormat.CueBin, usedTemporaryNormalization: true, requiredTempSpaceBytes, StatusErrorKey, NormalizeChdCdKey);
        }

        string isoPath = Path.Combine(root, "normalized.iso");

        Report(progress, operationId, ProgressOperationType.TemporaryNormalization, itemName, 2, 0, requiredTempSpaceBytes, 0, NormalizeChdDvdKey);
        bool extracted = await RunChdmanExtractionAsync(
                options.ChdmanPath,
                classification.InputPath,
                ["extractdvd", "-i", classification.InputPath, "-o", isoPath, "-f"],
                isoPath,
                operationId,
                itemName,
                requiredTempSpaceBytes,
                NormalizeChdDvdKey,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        return extracted && File.Exists(isoPath)
            ? NormalizationOutcome.Success(isoPath, RedumpNormalizedFormat.Iso, usedTemporaryNormalization: true, requiredTempSpaceBytes)
            : NormalizationOutcome.Failure(RedumpV2ResultState.Failed, RedumpNormalizedFormat.Iso, usedTemporaryNormalization: true, requiredTempSpaceBytes, StatusErrorKey, NormalizeChdDvdKey);
    }

    private async Task<NormalizationOutcome> NormalizeArchiveAsync(
        RedumpSourceClassification classification,
        RedumpV2ScanOptions options,
        List<string> cleanupRoots,
        Guid operationId,
        string itemName,
        IProgress<ProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        string root = CreateTempRoot(operationId, "archive");
        cleanupRoots.Add(root);

        Report(progress, operationId, ProgressOperationType.TemporaryNormalization, itemName, 2, 0, classification.SourceBytes, 0, NormalizeArchiveKey);

        var archiveProgress = new Progress<int>(percent =>
        {
            Report(
                progress,
                operationId,
                ProgressOperationType.TemporaryNormalization,
                itemName,
                2,
                0,
                classification.SourceBytes,
                percent,
                NormalizeArchiveKey);
        });

        ArchiveExtractionResult extraction = await _archiveExtraction
            .ExtractFirstSupportedDiscFileAsync(classification.InputPath, root, archiveProgress, cancellationToken)
            .ConfigureAwait(false);

        if (!extraction.IsSuccess || string.IsNullOrWhiteSpace(extraction.ExtractedPath))
        {
            return NormalizationOutcome.Failure(
                extraction.WasCancelled ? RedumpV2ResultState.Failed : RedumpV2ResultState.Unsupported,
                RedumpNormalizedFormat.Unsupported,
                usedTemporaryNormalization: true,
                requiredTempSpaceBytes: classification.SourceBytes,
                extraction.WasCancelled ? StatusErrorKey : StatusUnsupportedKey,
                string.IsNullOrWhiteSpace(extraction.Message) ? NormalizeArchiveKey : extraction.Message);
        }

        RedumpSourceClassification extractedClassification = _classifier.Classify(extraction.ExtractedPath, cancellationToken);
        NormalizationOutcome inner = await NormalizeAsync(
                extractedClassification,
                options,
                cleanupRoots,
                operationId,
                itemName,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        long extractedBytes = Math.Max(classification.SourceBytes, TryGetFileSize(extraction.ExtractedPath));
        long totalRequiredTempBytes = checked(extractedBytes + Math.Max(0, inner.RequiredTempSpaceBytes));

        if (!inner.IsSuccess)
        {
            return inner with
            {
                UsedTemporaryNormalization = true,
                RequiredTempSpaceBytes = totalRequiredTempBytes
            };
        }

        return inner with
        {
            UsedTemporaryNormalization = true,
            RequiredTempSpaceBytes = totalRequiredTempBytes
        };
    }

    private static async Task<bool> RunChdmanExtractionAsync(
        string chdmanPath,
        string inputPath,
        IReadOnlyList<string> arguments,
        string monitoredOutputPath,
        Guid operationId,
        string itemName,
        long totalBytes,
        string messageKey,
        IProgress<ProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        IProgress<int> percentProgress = new Progress<int>(percent =>
        {
            Report(
                progress,
                operationId,
                ProgressOperationType.TemporaryNormalization,
                itemName,
                2,
                0,
                totalBytes,
                percent,
                messageKey);
        });

        ChdmanCliRunner.Result result = await ChdmanCliRunner
            .ExecuteAsync(
                chdmanPath,
                arguments,
                parseProgressPercent: true,
                progress: percentProgress,
                onProcessStarted: null,
                cancellationToken: cancellationToken,
                exclusiveFileAccessPath: inputPath,
                monitoredOutputPath: monitoredOutputPath,
                priorityMode: ChdmanProcessPriorityMode.Quiet)
            .ConfigureAwait(false);

        if (result.WasCancelled || cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }

        return result.ExitCode == 0;
    }

    private static async Task<IReadOnlyList<DeepHashFileDigest>> HashAllFilesAsync(
        IReadOnlyList<string> files,
        Guid operationId,
        string itemName,
        IProgress<ProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        long totalBytes = files.Sum(TryGetFileSize);
        long currentBytes = 0;
        var result = new List<DeepHashFileDigest>(files.Count);
        var stopwatch = Stopwatch.StartNew();

        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileInfo info = new(file);
            long baseBytes = currentBytes;

            (string md5, string sha1, string crc32) = await Task
                .Run(
                    () => ComputeHashesSequential(
                        file,
                        totalBytes,
                        value =>
                        {
                            long absolute = baseBytes + value;
                            double percent = totalBytes <= 0 ? 0 : Math.Clamp(absolute * 100.0 / totalBytes, 0, 100);
                            double speed = stopwatch.Elapsed.TotalSeconds <= 0 ? 0 : absolute / stopwatch.Elapsed.TotalSeconds;
                            TimeSpan? eta = speed > 0 && totalBytes > absolute
                                ? TimeSpan.FromSeconds((totalBytes - absolute) / speed)
                                : null;

                            Report(
                                progress,
                                operationId,
                                ProgressOperationType.Hashing,
                                itemName,
                                3,
                                absolute,
                                totalBytes,
                                percent,
                                StepHashKey,
                                speed,
                                eta);
                        },
                        cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            currentBytes = baseBytes + info.Length;
            result.Add(new DeepHashFileDigest(file, info.Length, md5, sha1, crc32));
        }

        Report(progress, operationId, ProgressOperationType.Hashing, itemName, 3, totalBytes, totalBytes, 100, StepHashKey);
        return result;
    }

    private static (string Md5Lower, string Sha1Lower, string Crc32Lower) ComputeHashesSequential(
        string filePath,
        long totalBytes,
        Action<long> progress,
        CancellationToken cancellationToken)
    {
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buffer = new byte[BufferSize];
        uint crc32 = uint.MaxValue;
        long fileReadBytes = 0;
        long nextReportBytes = BufferSize * 16L;

        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: BufferSize,
            FileOptions.SequentialScan);

        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ReadOnlySpan<byte> span = buffer.AsSpan(0, read);
            md5.AppendData(span);
            sha1.AppendData(span);
            crc32 = UpdateCrc32(crc32, span);
            fileReadBytes += read;

            if (fileReadBytes >= nextReportBytes || totalBytes <= BufferSize)
            {
                progress(fileReadBytes);
                nextReportBytes = fileReadBytes + BufferSize * 16L;
            }
        }

        progress(fileReadBytes);

        string md5Hex = Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant();
        string sha1Hex = Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant();
        string crc32Hex = (crc32 ^ uint.MaxValue).ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
        return (md5Hex, sha1Hex, crc32Hex);
    }

    private static RedumpV2ScanResult MatchAndAggregate(
        string originalPath,
        RedumpSourceFormat sourceFormat,
        NormalizationOutcome normalization,
        IReadOnlyList<DeepHashFileDigest> hashed,
        RedumpSqliteManager redumpDatabase)
    {
        var matches = new List<DeepHashMatch>();
        var misses = new List<string>();

        foreach (DeepHashFileDigest file in hashed)
        {
            if (redumpDatabase.TryMatchHash(file.Md5, file.Sha1, file.Crc32, file.SizeBytes, out RedumpRomHit hit))
            {
                matches.Add(ToMatch(file, hit));
            }
            else
            {
                misses.Add(Path.GetFileName(file.Path));
            }
        }

        if (matches.Count == hashed.Count && matches.Count > 0)
        {
            if (!MatchesBelongToOneDisc(matches))
            {
                return BuildTerminalResult(
                    RedumpV2ResultState.Failed,
                    originalPath,
                    sourceFormat,
                    normalization.Format,
                    normalization.UsedTemporaryNormalization,
                    normalization.RequiredTempSpaceBytes,
                    "LocDeepHash_StatusConflictingMatch",
                    TipConflictingMatchesKey,
                    hashedFiles: hashed,
                    matches: matches);
            }

            DeepHashMatch first = matches[0];
            bool normalized = normalization.UsedTemporaryNormalization;
            string statusKey = normalized
                ? StatusVerifiedNormalizedKey
                : matches.Count == 1 ? StatusVerifiedKey : StatusVerifiedCompleteKey;

            string detailKey = normalized ? NormalizedDetailKey : TipVerifiedHeaderKey;
            IReadOnlyList<object?> detailArgs = normalized
                ? [sourceFormat.ToString(), normalization.Format.ToString(), FormatBytes(normalization.RequiredTempSpaceBytes), RedumpV2ResultState.VerifiedNormalized.ToString()]
                : [];

            return BuildTerminalResult(
                normalized ? RedumpV2ResultState.VerifiedNormalized : RedumpV2ResultState.Verified,
                originalPath,
                sourceFormat,
                normalization.Format,
                normalization.UsedTemporaryNormalization,
                normalization.RequiredTempSpaceBytes,
                statusKey,
                detailKey,
                detailArgs,
                hashed,
                matches,
                [],
                BuildSuggestedStandardFileName(first.RomName, originalPath),
                first.SystemName,
                first.GameName,
                matches.Count,
                hashed.Count);
        }

        if (matches.Count > 0)
        {
            return BuildTerminalResult(
                RedumpV2ResultState.Failed,
                originalPath,
                sourceFormat,
                normalization.Format,
                normalization.UsedTemporaryNormalization,
                normalization.RequiredTempSpaceBytes,
                StatusIncompleteKey,
                TipPartialMatchKey,
                [matches.Count, hashed.Count],
                hashed,
                matches,
                misses);
        }

        return BuildTerminalResult(
            RedumpV2ResultState.NoRedumpMatch,
            originalPath,
            sourceFormat,
            normalization.Format,
            normalization.UsedTemporaryNormalization,
            normalization.RequiredTempSpaceBytes,
            StatusModifiedKey,
            TipNoRedumpMatchKey,
            hashedFiles: hashed,
            unmatchedFileNames: misses);
    }

    private static RedumpV2ScanResult BuildTerminalResult(
        RedumpV2ResultState state,
        string originalPath,
        RedumpSourceFormat sourceFormat,
        RedumpNormalizedFormat normalizedFormat,
        bool usedTemporaryNormalization,
        long requiredTempSpaceBytes,
        string statusMessageKey,
        string detailMessageKey,
        IReadOnlyList<object?>? detailArgs = null,
        IReadOnlyList<DeepHashFileDigest>? hashedFiles = null,
        IReadOnlyList<DeepHashMatch>? matches = null,
        IReadOnlyList<string>? unmatchedFileNames = null,
        string suggestedStandardName = "",
        string matchedSystemName = "",
        string matchedGameName = "",
        int? matchedFileCount = null,
        int? hashedFileCount = null,
        string failureCode = "")
    {
        IReadOnlyList<DeepHashFileDigest> resolvedHashedFiles = hashedFiles ?? [];
        IReadOnlyList<DeepHashMatch> resolvedMatches = matches ?? [];

        return new RedumpV2ScanResult(
            state,
            originalPath,
            sourceFormat,
            normalizedFormat,
            usedTemporaryNormalization,
            requiredTempSpaceBytes,
            statusMessageKey,
            detailMessageKey,
            detailArgs ?? [],
            resolvedHashedFiles,
            resolvedMatches,
            unmatchedFileNames ?? [],
            suggestedStandardName,
            matchedSystemName,
            matchedGameName,
            matchedFileCount ?? resolvedMatches.Count,
            hashedFileCount ?? resolvedHashedFiles.Count,
            failureCode);
    }

    private static IReadOnlyList<string> ResolveFilesToHash(string fullProbePath)
    {
        string extension = Path.GetExtension(fullProbePath);
        string normalized = Path.GetFullPath(fullProbePath);

        if (!HashableExtensions.Contains(extension))
        {
            return [];
        }

        return extension.ToLowerInvariant() switch
        {
            ".cue" => ResolveCueBinFiles(normalized),
            ".gdi" => ResolveGdiTrackFiles(normalized),
            ".iso" or ".gcm" or ".bin" or ".img" or ".raw" => [normalized],
            _ => []
        };
    }

    private static string ResolveBinCueLeaderPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string extension = Path.GetExtension(fullPath);
        if (string.Equals(extension, ".cue", StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        if (!string.Equals(extension, ".bin", StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return fullPath;
        }

        foreach (string cuePath in Directory.EnumerateFiles(directory, "*.cue", SearchOption.TopDirectoryOnly))
        {
            try
            {
                IReadOnlyList<string> cueFiles = ResolveCueBinFiles(cuePath);
                if (cueFiles.Any(file => string.Equals(Path.GetFullPath(file), fullPath, StringComparison.OrdinalIgnoreCase)))
                {
                    return cuePath;
                }
            }
            catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or ArgumentException
                                      or NotSupportedException
                                      or PathTooLongException
                                      or InvalidDataException)
            {
                Log.Debug(ex, "Could not inspect sibling CUE for Redump V2 BIN leader resolution. Cue={CuePath}", cuePath);
            }
        }

        return fullPath;
    }

    private static IReadOnlyList<string> ResolveCueBinFiles(string cuePath)
    {
        string? directory = Path.GetDirectoryName(cuePath);
        if (string.IsNullOrEmpty(directory))
        {
            return [];
        }

        string baseDirectory = Path.GetFullPath(directory);
        var names = new List<string>();

        foreach (string line in File.ReadLines(cuePath, Encoding.UTF8))
        {
            if (CueSheetFileStatementReader.TryRead(line, out string name, out _))
            {
                names.Add(name);
            }
        }

        return ResolveDescriptorFileNames(baseDirectory, names);
    }

    private static IReadOnlyList<string> ResolveGdiTrackFiles(string gdiPath)
    {
        string? directory = Path.GetDirectoryName(gdiPath);
        if (string.IsNullOrEmpty(directory))
        {
            return [];
        }

        string baseDirectory = Path.GetFullPath(directory);
        var names = new List<string>();
        bool skippedHeader = false;

        foreach (string line in File.ReadLines(gdiPath, Encoding.UTF8))
        {
            if (!skippedHeader)
            {
                skippedHeader = true;
                continue;
            }

            if (TryExtractGdiTrackFileName(line, out string name))
            {
                names.Add(name);
            }
        }

        return ResolveDescriptorFileNames(baseDirectory, names);
    }

    private static bool TryExtractGdiTrackFileName(string line, out string fileName)
    {
        fileName = string.Empty;

        ReadOnlySpan<char> span = line.AsSpan().Trim();
        if (span.Length == 0 || span[0] == '#')
        {
            return false;
        }

        for (int field = 0; field < 4; field++)
        {
            if (!TryConsumeToken(ref span, out _))
            {
                return false;
            }
        }

        span = span.TrimStart();
        if (span.Length == 0)
        {
            return false;
        }

        if (span[0] == '"')
        {
            int closingQuote = span[1..].IndexOf('"');
            if (closingQuote < 0)
            {
                return false;
            }

            fileName = span.Slice(1, closingQuote).ToString().Trim();
            return fileName.Length > 0;
        }

        if (!TryConsumeToken(ref span, out ReadOnlySpan<char> token))
        {
            return false;
        }

        fileName = token.Trim('"').ToString().Trim();
        return fileName.Length > 0;
    }

    private static bool TryConsumeToken(ref ReadOnlySpan<char> span, out ReadOnlySpan<char> token)
    {
        span = span.TrimStart();
        token = default;

        if (span.Length == 0)
        {
            return false;
        }

        int separator = IndexOfWhiteSpace(span);
        if (separator < 0)
        {
            token = span;
            span = [];
            return token.Length > 0;
        }

        token = span[..separator];
        span = span[(separator + 1)..];
        return token.Length > 0;
    }

    private static IReadOnlyList<string> ResolveDescriptorFileNames(string baseDirectory, IReadOnlyList<string> names)
    {
        var resolved = new List<string>();
        var missing = new List<string>();

        foreach (string relativePath in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string combined = Path.GetFullPath(Path.Combine(baseDirectory, relativePath));
            if (!IsUnderDirectory(baseDirectory, combined))
            {
                missing.Add(relativePath);
                continue;
            }

            if (File.Exists(combined))
            {
                resolved.Add(combined);
            }
            else
            {
                missing.Add(relativePath);
            }
        }

        if (missing.Count > 0)
        {
            throw new FileNotFoundException("Disc descriptor references missing files.");
        }

        return resolved;
    }

    private static bool IsUnderDirectory(string baseDirectory, string candidate)
    {
        string root = Path.GetFullPath(baseDirectory);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        string path = Path.GetFullPath(candidate);
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static DeepHashMatch ToMatch(DeepHashFileDigest file, RedumpRomHit hit) => new(
        file.Path,
        file.SizeBytes,
        file.Md5,
        file.Sha1,
        file.Crc32,
        hit.SystemName,
        hit.GameName,
        hit.RomName,
        hit.MatchSource,
        hit.Crc ?? string.Empty,
        hit.Region ?? string.Empty,
        hit.Version ?? string.Empty);

    private static bool MatchesBelongToOneDisc(IEnumerable<DeepHashMatch> matches)
    {
        string? system = null;
        string? game = null;

        foreach (DeepHashMatch match in matches)
        {
            if (system is null)
            {
                system = match.SystemName;
                game = match.GameName;
                continue;
            }

            if (!string.Equals(system, match.SystemName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(game, match.GameName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static uint UpdateCrc32(uint crc, ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<uint> table = Crc32Table;

        foreach (byte value in data)
        {
            crc = (crc >> 8) ^ table[(int)((crc ^ value) & 0xFFu)];
        }

        return crc;
    }

    private static uint[] Crc32Table { get; } = BuildCrc32Table();

    private static uint[] BuildCrc32Table()
    {
        uint[] table = new uint[256];

        for (uint index = 0; index < table.Length; index++)
        {
            uint value = index;

            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) == 1
                    ? (value >> 1) ^ 0xEDB88320u
                    : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }

    private static bool IsCdChd(string? mediaType) =>
        !string.IsNullOrWhiteSpace(mediaType)
        && (mediaType.Contains("CD", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("GD", StringComparison.OrdinalIgnoreCase));

    private static bool IsDvdChd(string? mediaType, long? logicalBytes)
    {
        if (!string.IsNullOrWhiteSpace(mediaType)
            && (mediaType.Contains("DVD", StringComparison.OrdinalIgnoreCase)
                || mediaType.Contains("UMD", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        const long likelyDvdThreshold = 900L * 1024L * 1024L;
        return logicalBytes.GetValueOrDefault() >= likelyDvdThreshold;
    }

    private static string CreateTempRoot(Guid operationId, string segment)
    {
        string root = AppPaths.CombineProcessTemp("RedumpV2", operationId.ToString("N"), segment);
        Directory.CreateDirectory(root);
        return root;
    }

    private static void CleanupTemporaryRoots(IReadOnlyList<string> roots)
    {
        foreach (string root in roots
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static path => path.Length))
        {
            try
            {
                string fullPath = Path.GetFullPath(root);
                if (AppPaths.IsPathUnderProcessTempRoot(fullPath) && Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or ArgumentException
                                      or NotSupportedException
                                      or PathTooLongException
                                      or System.Security.SecurityException)
            {
                Log.Debug(ex, "Redump V2 temp cleanup failed. Root={Root}", root);
            }
        }
    }

    private static string BuildSuggestedStandardFileName(string redumpRomName, string originalPath)
    {
        string safeName = SanitizeFileName(redumpRomName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            return string.Empty;
        }

        if (Path.HasExtension(safeName))
        {
            return safeName;
        }

        string extension = Path.GetExtension(originalPath);
        return string.IsNullOrWhiteSpace(extension)
            ? safeName
            : safeName + extension;
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (char character in value.Trim())
        {
            builder.Append(invalid.Contains(character) ? ' ' : character);
        }

        string collapsed = System.Text.RegularExpressions.Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
        return collapsed.TrimEnd('.', ' ');
    }

    private static long TryGetFileSize(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException)
        {
            return 0;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unitIndex = 0;
        while (value >= 1024d && unitIndex < units.Length - 1)
        {
            value /= 1024d;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{bytes} {units[unitIndex]}"
            : $"{value:0.##} {units[unitIndex]}";
    }

    private static int IndexOfWhiteSpace(ReadOnlySpan<char> span)
    {
        for (int i = 0; i < span.Length; i++)
        {
            if (char.IsWhiteSpace(span[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsInputReadFailureException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException;

    private static void Report(
        IProgress<ProgressEvent>? progress,
        Guid operationId,
        ProgressOperationType operationType,
        string itemName,
        int currentStep,
        long currentBytes,
        long totalBytes,
        double percent,
        string messageKey,
        double speedBytesPerSecond = 0,
        TimeSpan? eta = null)
    {
        progress?.Report(new ProgressEvent
        {
            OperationId = operationId,
            OperationType = operationType,
            ItemName = itemName,
            CurrentStep = currentStep,
            TotalSteps = TotalSteps,
            CurrentBytes = Math.Max(0, currentBytes),
            TotalBytes = Math.Max(0, totalBytes),
            Percent = Math.Clamp(percent, 0, 100),
            SpeedBytesPerSecond = Math.Max(0, speedBytesPerSecond),
            Eta = eta,
            MessageKey = messageKey,
            CanCancel = true
        });
    }

    private sealed record NormalizationOutcome(
        bool IsSuccess,
        string CandidatePath,
        RedumpNormalizedFormat Format,
        bool UsedTemporaryNormalization,
        long RequiredTempSpaceBytes,
        RedumpV2ResultState State,
        string StatusMessageKey,
        string DetailMessageKey,
        IReadOnlyList<object?> DetailArgs)
    {
        public static NormalizationOutcome Success(
            string candidatePath,
            RedumpNormalizedFormat format,
            bool usedTemporaryNormalization,
            long requiredTempSpaceBytes) => new(
                true,
                candidatePath,
                format,
                usedTemporaryNormalization,
                requiredTempSpaceBytes,
                usedTemporaryNormalization ? RedumpV2ResultState.VerifiedNormalized : RedumpV2ResultState.Verified,
                string.Empty,
                string.Empty,
                []);

        public static NormalizationOutcome Failure(
            RedumpV2ResultState state,
            RedumpNormalizedFormat format,
            bool usedTemporaryNormalization,
            long requiredTempSpaceBytes,
            string statusMessageKey,
            string detailMessageKey,
            IReadOnlyList<object?>? detailArgs = null) => new(
                false,
                string.Empty,
                format,
                usedTemporaryNormalization,
                requiredTempSpaceBytes,
                state,
                statusMessageKey,
                detailMessageKey,
                detailArgs ?? []);
    }
}