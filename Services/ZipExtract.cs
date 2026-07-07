using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public sealed class SevenZipArchiveExtractionService
{
    private const string NoConvertibleDiscImageMessageKey = "LocArchive_NoConvertibleDiscImage";
    private const string NoChdMessageKey = "LocArchive_NoChdFile";
    private const string SevenZipUnavailableMessageKey = "LocArchive_SevenZipUnavailable";
    private const string OperationCancelledMessageKey = "LocOperation_Cancelled";
    private const string PasswordRequiredMessageKey = "LocArchive_PasswordRequired";
    private const string SelectiveExtractFailedMessageKey = "LocArchive_SelectiveExtractFailed";
    private const string PreflightFailedMessageKey = "LocArchive_PreflightFailed";
    private const string MultipleConvertibleImagesMessageKey = "LocArchive_MultipleConvertibleImages";
    private const string SelectiveExtractSuccessMessageKey = "LocArchive_SelectiveExtractSuccess";
    private const string IoFailureMessageKey = "LocArchive_ExtractionIoFailure";
    private const string AccessFailureMessageKey = "LocArchive_ExtractionAccessFailure";
    private const string UnexpectedFailureMessageKey = "LocArchive_ExtractionUnexpectedFailure";
    private const string UnsafeExtractedPathMessageKey = "LocArchive_UnsafeExtractedPath";
    private const string InvalidArchivePathMessageKey = "LocArchive_InvalidArchivePath";
    private const string InvalidDestinationPathMessageKey = "LocArchive_InvalidDestinationPath";
    private const string ArchiveFileNotFoundMessageKey = "LocArchive_FileNotFound";

    private static readonly ILogger Logger = global::Serilog.Log.ForContext<SevenZipArchiveExtractionService>();

    private readonly SevenZipToolService _toolService = SevenZipToolService.Instance;

    public bool IsAvailable => _toolService.IsAvailable;

    public Task<ArchiveExtractionResult> ExtractFirstConvertibleDiscImageAsync(
        string archivePath,
        string destinationDirectory,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ExtractAndDiscoverAsync(
            archivePath,
            destinationDirectory,
            ArchiveCandidateDiscovery.FindFirstEffectiveConvertibleLeaderPath,
            NoConvertibleDiscImageMessageKey,
            validateSingleConvertibleLeader: true,
            progress,
            cancellationToken);
    }

    public Task<ArchiveExtractionResult> ExtractFirstChdAsync(
        string archivePath,
        string destinationDirectory,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ExtractAndDiscoverAsync(
            archivePath,
            destinationDirectory,
            ArchiveCandidateDiscovery.FindFirstChdPath,
            NoChdMessageKey,
            validateSingleConvertibleLeader: false,
            progress,
            cancellationToken);
    }

    public async Task<ArchiveIntegrityResult> TestArchiveAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        if (!_toolService.TryGetExecutablePath(out string sevenZipPath)
            || !TryNormalizeArchivePath(archivePath, out string fullArchivePath))
        {
            return new ArchiveIntegrityResult
            {
                IsValid = false,
                MessageResourceKey = "LocArchive_VerificationFailed"
            };
        }

        SevenZipProcessResult result = await SevenZipProcessRunner.RunAsync(
            sevenZipPath,
            ["t", "-y", "--", fullArchivePath],
            parseProgressPercent: true,
            progress: null,
            cancellationToken).ConfigureAwait(false);

        if (result.WasCancelled || cancellationToken.IsCancellationRequested)
        {
            return new ArchiveIntegrityResult
            {
                IsValid = false,
                WasCancelled = true,
                MessageResourceKey = "LocArchive_VerificationFailed"
            };
        }

        return new ArchiveIntegrityResult
        {
            IsValid = result.ExitCode == 0,
            MessageResourceKey = result.ExitCode == 0 ? string.Empty : "LocArchive_VerificationFailed"
        };
    }

    private async Task<ArchiveExtractionResult> ExtractAndDiscoverAsync(
        string archivePath,
        string destinationDirectory,
        Func<IEnumerable<string>, string?> candidateSelector,
        string noCandidateMessageKey,
        bool validateSingleConvertibleLeader,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ValidateInputs(archivePath, destinationDirectory, out string fullArchivePath, out string extractionRoot);

        if (!_toolService.TryGetExecutablePath(out string sevenZipPath))
        {
            return new ArchiveExtractionResult
            {
                IsSuccess = false,
                ExitCode = -1,
                Message = SevenZipUnavailableMessageKey
            };
        }

        SevenZipArchivePreflight preflight = await PreflightArchiveAsync(
            sevenZipPath,
            fullArchivePath,
            candidateSelector,
            noCandidateMessageKey,
            validateSingleConvertibleLeader,
            cancellationToken).ConfigureAwait(false);

        if (!preflight.IsSuccess)
        {
            return new ArchiveExtractionResult
            {
                IsSuccess = false,
                WasCancelled = preflight.WasCancelled,
                RequiresPassword = preflight.RequiresPassword,
                ExitCode = preflight.ExitCode,
                Output = preflight.Output,
                Error = preflight.Error,
                Message = preflight.Message
            };
        }

        EnsureSafeDestinationDirectory(extractionRoot);
        progress?.Report(0);

        try
        {
            SevenZipProcessResult result = await SevenZipProcessRunner.RunAsync(
                sevenZipPath,
                BuildSelectiveExtractArguments(extractionRoot, fullArchivePath, preflight.EntryArguments),
                parseProgressPercent: true,
                progress,
                cancellationToken).ConfigureAwait(false);

            if (result.WasCancelled || cancellationToken.IsCancellationRequested)
            {
                return new ArchiveExtractionResult
                {
                    IsSuccess = false,
                    WasCancelled = true,
                    ExitCode = ChdmanProcessRunner.CanceledExitCode,
                    Message = OperationCancelledMessageKey
                };
            }

            if (result.ExitCode != 0)
            {
                string output = result.CombinedOutput;
                bool requiresPassword = SevenZipArchiveInspector.LooksPasswordProtected(output);

                Logger.Warning(
                    "7-Zip selective extraction failed. Archive={Archive}, Destination={Destination}, ExitCode={ExitCode}, Output={Output}",
                    fullArchivePath,
                    extractionRoot,
                    result.ExitCode,
                    output);

                return new ArchiveExtractionResult
                {
                    IsSuccess = false,
                    RequiresPassword = requiresPassword,
                    ExitCode = result.ExitCode,
                    Output = result.StandardOutput,
                    Error = result.StandardError,
                    Message = requiresPassword ? PasswordRequiredMessageKey : SelectiveExtractFailedMessageKey
                };
            }

            List<string> extractedFiles = EnumerateSafeExtractedFiles(extractionRoot);

            string? extractedPath = BuildExtractedPathFromEntryKey(extractionRoot, preflight.CandidateEntryPath);
            if (!string.IsNullOrWhiteSpace(extractedPath) && !File.Exists(extractedPath))
            {
                extractedPath = null;
            }

            extractedPath ??= candidateSelector(extractedFiles);
            if (!string.IsNullOrWhiteSpace(extractedPath))
            {
                extractedPath = EnsurePathInsideExtractionRoot(extractedPath, extractionRoot);
            }

            if (string.IsNullOrWhiteSpace(extractedPath))
            {
                Logger.Warning(
                    "7-Zip selective extraction completed but no expected candidate was found. Archive={Archive}, Destination={Destination}, ExtractedCount={Count}",
                    fullArchivePath,
                    extractionRoot,
                    extractedFiles.Count);

                return new ArchiveExtractionResult
                {
                    IsSuccess = false,
                    ExitCode = -1,
                    ExtractedFiles = extractedFiles,
                    Output = result.StandardOutput,
                    Error = result.StandardError,
                    Message = noCandidateMessageKey
                };
            }

            if (validateSingleConvertibleLeader
                && !ArchiveCandidateDiscovery.TryValidateExtractedDescriptorDependencies(
                    extractedPath,
                    extractedFiles,
                    out string dependencyFailureMessage))
            {
                return new ArchiveExtractionResult
                {
                    IsSuccess = false,
                    ExitCode = -1,
                    ExtractedFiles = extractedFiles,
                    Output = result.StandardOutput,
                    Error = result.StandardError,
                    Message = dependencyFailureMessage
                };
            }

            progress?.Report(100);

            return new ArchiveExtractionResult
            {
                IsSuccess = true,
                ExitCode = 0,
                ExtractedPath = extractedPath,
                ExtractedFiles = extractedFiles,
                Output = result.StandardOutput,
                Error = result.StandardError,
                Message = SelectiveExtractSuccessMessageKey
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ArchiveExtractionResult
            {
                IsSuccess = false,
                WasCancelled = true,
                ExitCode = ChdmanProcessRunner.CanceledExitCode,
                Message = OperationCancelledMessageKey
            };
        }
        catch (IOException ex)
        {
            Logger.Warning(ex, "7-Zip selective extraction failed due to I/O error. Archive={Archive}, Destination={Destination}", fullArchivePath, extractionRoot);

            return new ArchiveExtractionResult
            {
                IsSuccess = false,
                ExitCode = -1,
                Message = IoFailureMessageKey
            };
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warning(ex, "7-Zip selective extraction failed due to access permissions. Archive={Archive}, Destination={Destination}", fullArchivePath, extractionRoot);

            return new ArchiveExtractionResult
            {
                IsSuccess = false,
                ExitCode = -1,
                Message = AccessFailureMessageKey
            };
        }
        catch (Exception ex) when (IsExpectedExtractionException(ex))
        {
            Logger.Warning(ex, "7-Zip selective extraction failed unexpectedly. Archive={Archive}, Destination={Destination}", fullArchivePath, extractionRoot);

            return new ArchiveExtractionResult
            {
                IsSuccess = false,
                ExitCode = -1,
                Message = UnexpectedFailureMessageKey
            };
        }
    }

    private sealed record SevenZipArchivePreflight(
        bool IsSuccess,
        bool WasCancelled,
        bool RequiresPassword,
        int ExitCode,
        string Output,
        string Error,
        string Message,
        string? CandidateEntryPath,
        string[] EntryArguments);

    private static async Task<SevenZipArchivePreflight> PreflightArchiveAsync(
        string sevenZipPath,
        string archivePath,
        Func<IEnumerable<string>, string?> candidateSelector,
        string noCandidateMessageKey,
        bool validateSingleConvertibleLeader,
        CancellationToken cancellationToken)
    {
        SevenZipProcessResult listResult = await SevenZipProcessRunner.RunAsync(
            sevenZipPath,
            ["l", "-slt", "-ba", "--", archivePath],
            parseProgressPercent: false,
            progress: null,
            cancellationToken).ConfigureAwait(false);

        if (listResult.WasCancelled || cancellationToken.IsCancellationRequested)
        {
            return new SevenZipArchivePreflight(
                false,
                true,
                false,
                ChdmanProcessRunner.CanceledExitCode,
                listResult.StandardOutput,
                listResult.StandardError,
                OperationCancelledMessageKey,
                null,
                []);
        }

        if (listResult.ExitCode != 0)
        {
            string output = listResult.CombinedOutput;
            bool requiresPassword = SevenZipArchiveInspector.LooksPasswordProtected(output);

            Logger.Warning(
                "7-Zip archive preflight failed. Archive={Archive}, ExitCode={ExitCode}, Output={Output}",
                archivePath,
                listResult.ExitCode,
                output);

            return new SevenZipArchivePreflight(
                false,
                false,
                requiresPassword,
                listResult.ExitCode,
                listResult.StandardOutput,
                listResult.StandardError,
                requiresPassword ? PasswordRequiredMessageKey : PreflightFailedMessageKey,
                null,
                []);
        }

        List<string> entryPaths =
        [
            .. SevenZipArchiveInspector.ParseSevenZipListPaths(listResult.StandardOutput)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Where(path => !LooksLikeArchiveDirectory(path))
                .Select(NormalizeArchiveEntryArgument)
                .Where(IsSafeArchiveEntryArgument)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];

        if (entryPaths.Count == 0)
        {
            return new SevenZipArchivePreflight(
                false,
                false,
                false,
                -1,
                listResult.StandardOutput,
                listResult.StandardError,
                ArchiveCandidateDiscovery.EmptyArchiveMessageResourceKey,
                null,
                []);
        }

        if (validateSingleConvertibleLeader && ArchiveCandidateDiscovery.HasMultipleEffectiveConvertibleLeaderPaths(entryPaths))
        {
            return new SevenZipArchivePreflight(
                false,
                false,
                false,
                -1,
                listResult.StandardOutput,
                listResult.StandardError,
                MultipleConvertibleImagesMessageKey,
                null,
                []);
        }

        string? candidateEntryPath = candidateSelector(entryPaths);
        if (string.IsNullOrWhiteSpace(candidateEntryPath))
        {
            return new SevenZipArchivePreflight(
                false,
                false,
                false,
                -1,
                listResult.StandardOutput,
                listResult.StandardError,
                ArchiveCandidateDiscovery.HasUnsupportedDiscImagePath(entryPaths)
                    ? ArchiveCandidateDiscovery.UnsupportedDiscImageMessageResourceKey
                    : noCandidateMessageKey,
                null,
                []);
        }

        candidateEntryPath = NormalizeArchiveEntryArgument(candidateEntryPath);
        if (!IsSafeArchiveEntryArgument(candidateEntryPath))
        {
            return new SevenZipArchivePreflight(
                false,
                false,
                false,
                -1,
                listResult.StandardOutput,
                listResult.StandardError,
                UnsafeExtractedPathMessageKey,
                null,
                []);
        }

        ArchiveDescriptorDependencyValidationResult? dependencyResult = null;
        if (validateSingleConvertibleLeader && ArchiveCandidateDiscovery.IsDescriptorLeaderPath(candidateEntryPath))
        {
            SevenZipArchiveInspector.SevenZipDescriptorTextResult descriptor =
                await SevenZipArchiveInspector.ReadDescriptorTextAsync(
                    sevenZipPath,
                    archivePath,
                    candidateEntryPath,
                    cancellationToken).ConfigureAwait(false);

            if (descriptor.WasCancelled)
            {
                return new SevenZipArchivePreflight(
                    false,
                    true,
                    false,
                    ChdmanProcessRunner.CanceledExitCode,
                    listResult.StandardOutput,
                    listResult.StandardError,
                    OperationCancelledMessageKey,
                    null,
                    []);
            }

            if (!descriptor.IsSuccess)
            {
                return new SevenZipArchivePreflight(
                    false,
                    false,
                    descriptor.RequiresPassword,
                    -1,
                    listResult.StandardOutput,
                    listResult.StandardError,
                    descriptor.MessageResourceKey,
                    null,
                    []);
            }

            dependencyResult = ArchiveCandidateDiscovery.AnalyzeDescriptorDependencies(
                candidateEntryPath,
                descriptor.Text,
                entryPaths);

            if (!dependencyResult.IsValid)
            {
                return new SevenZipArchivePreflight(
                    false,
                    false,
                    false,
                    -1,
                    listResult.StandardOutput,
                    listResult.StandardError,
                    dependencyResult.MessageResourceKey,
                    null,
                    []);
            }
        }

        string[] entryArguments = BuildPreflightEntryArguments(
            entryPaths,
            candidateEntryPath,
            validateSingleConvertibleLeader,
            dependencyResult?.RequiredKeys);

        if (entryArguments.Length == 0)
        {
            return new SevenZipArchivePreflight(
                false,
                false,
                false,
                -1,
                listResult.StandardOutput,
                listResult.StandardError,
                noCandidateMessageKey,
                null,
                []);
        }

        Logger.Information(
            "7-Zip archive preflight selected entries. Archive={Archive}, Candidate={Candidate}, EntryCount={EntryCount}, TotalEntries={TotalEntries}",
            archivePath,
            candidateEntryPath,
            entryArguments.Length,
            entryPaths.Count);

        return new SevenZipArchivePreflight(
            true,
            false,
            false,
            0,
            listResult.StandardOutput,
            listResult.StandardError,
            string.Empty,
            candidateEntryPath,
            entryArguments);
    }

    private static string[] BuildPreflightEntryArguments(
        IReadOnlyList<string> entryPaths,
        string candidateEntryPath,
        bool validateSingleConvertibleLeader,
        IReadOnlySet<string>? descriptorRequiredKeys)
    {
        string candidateKey = ArchiveCandidateDiscovery.NormalizeLookupKey(candidateEntryPath);
        string extension = Path.GetExtension(candidateEntryPath).ToLowerInvariant();

        if (!validateSingleConvertibleLeader || extension is not (".cue" or ".gdi" or ".toc"))
        {
            return
            [
                .. entryPaths
                    .Where(path => string.Equals(
                        ArchiveCandidateDiscovery.NormalizeLookupKey(path),
                        candidateKey,
                        StringComparison.OrdinalIgnoreCase))
                    .DefaultIfEmpty(candidateEntryPath)
                    .Where(IsSafeArchiveEntryArgument)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            ];
        }

        HashSet<string> keysToExtract = new(StringComparer.OrdinalIgnoreCase)
        {
            candidateKey
        };

        if (descriptorRequiredKeys is not null)
        {
            foreach (string requiredKey in descriptorRequiredKeys.Where(key => !string.IsNullOrWhiteSpace(key)))
            {
                keysToExtract.Add(requiredKey);
            }
        }

        if (extension == ".cue")
        {
            string candidateDirectory = ArchiveCandidateDiscovery.NormalizeDirectoryKey(Path.GetDirectoryName(candidateEntryPath));
            string candidateDirectoryPrefix = string.IsNullOrWhiteSpace(candidateDirectory)
                ? string.Empty
                : candidateDirectory + "/";

            string sbiKey = candidateDirectoryPrefix
                + Path.GetFileNameWithoutExtension(candidateEntryPath)
                + ".sbi";

            string normalizedSbiKey = ArchiveCandidateDiscovery.NormalizeLookupKey(sbiKey);
            if (!string.IsNullOrWhiteSpace(normalizedSbiKey))
            {
                keysToExtract.Add(normalizedSbiKey);
            }
        }

        return
        [
            .. entryPaths
                .Where(path => keysToExtract.Contains(ArchiveCandidateDiscovery.NormalizeLookupKey(path)))
                .Where(IsSafeArchiveEntryArgument)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static string[] BuildSelectiveExtractArguments(
        string extractionRoot,
        string archivePath,
        string[] entryArguments)
    {
        return
        [
            "x",
            "-y",
            "-bb1",
            "-bsp1",
            "-bso1",
            "-bse1",
            "-o" + extractionRoot,
            "--",
            archivePath,
            .. entryArguments
        ];
    }

    private static string? BuildExtractedPathFromEntryKey(string extractionRoot, string? entryKey)
    {
        if (string.IsNullOrWhiteSpace(entryKey))
        {
            return null;
        }

        string safeRelative = NormalizeArchiveEntryArgument(entryKey);
        if (!IsSafeArchiveEntryArgument(safeRelative))
        {
            return null;
        }

        return EnsurePathInsideExtractionRoot(Path.Combine(extractionRoot, safeRelative), extractionRoot);
    }

    private static string NormalizeArchiveEntryArgument(string? value)
    {
        return (value ?? string.Empty)
            .Replace('\\', '/')
            .Trim()
            .TrimStart('/');
    }

    private static bool IsSafeArchiveEntryArgument(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = NormalizeArchiveEntryArgument(value);

        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Contains('\0')
            || normalized.StartsWith('@')
            || Path.IsPathRooted(normalized)
            || normalized.Contains(':'))
        {
            return false;
        }

        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length > 0
            && segments.All(segment => !string.Equals(segment, ".", StringComparison.Ordinal)
                && !string.Equals(segment, "..", StringComparison.Ordinal));
    }

    private static bool LooksLikeArchiveDirectory(string path) =>
        path.EndsWith('/')
        || path.EndsWith('\\');

    private static List<string> EnumerateSafeExtractedFiles(string extractionRoot)
    {
        string root = Path.GetFullPath(extractionRoot);
        List<string> files = [];

        if (!Directory.Exists(root))
        {
            return files;
        }

        Stack<string> pending = new();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string currentDirectory = pending.Pop();
            string safeCurrentDirectory = EnsurePathInsideExtractionRoot(currentDirectory, root);

            if (IsReparsePoint(safeCurrentDirectory))
            {
                throw new InvalidOperationException(UnsafeExtractedPathMessageKey);
            }

            string[] currentFiles = Directory.GetFiles(safeCurrentDirectory, "*", SearchOption.TopDirectoryOnly);
            foreach (string file in currentFiles)
            {
                string safeFile = EnsurePathInsideExtractionRoot(file, root);

                if (IsReparsePoint(safeFile))
                {
                    throw new InvalidOperationException(UnsafeExtractedPathMessageKey);
                }

                files.Add(safeFile);
            }

            string[] directories = Directory.GetDirectories(safeCurrentDirectory, "*", SearchOption.TopDirectoryOnly);
            foreach (string directory in directories)
            {
                string safeDirectory = EnsurePathInsideExtractionRoot(directory, root);

                if (IsReparsePoint(safeDirectory))
                {
                    throw new InvalidOperationException(UnsafeExtractedPathMessageKey);
                }

                pending.Push(safeDirectory);
            }
        }

        return files;
    }

    private static string EnsurePathInsideExtractionRoot(string path, string extractionRoot)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetFullPath(extractionRoot);

        if (!IsSamePathOrChild(fullPath, root)
            || HasReparsePointInExistingPath(fullPath, root))
        {
            Logger.Warning("7-Zip extraction produced a path outside the extraction root or through a reparse point. Root={Root}, Path={Path}", root, fullPath);
            throw new InvalidOperationException(UnsafeExtractedPathMessageKey);
        }

        return fullPath;
    }

    private static void ValidateInputs(
        string archivePath,
        string destinationDirectory,
        out string fullArchivePath,
        out string fullDestinationDirectory)
    {
        fullArchivePath = ResolveArchivePathOrThrow(archivePath);
        fullDestinationDirectory = ResolveDestinationDirectoryOrThrow(destinationDirectory);
    }

    private static string ResolveArchivePathOrThrow(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new ArgumentException(InvalidArchivePathMessageKey, nameof(archivePath));
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(archivePath.Trim());
        }
        catch (Exception ex) when (IsExpectedExtractionException(ex))
        {
            throw new ArgumentException(InvalidArchivePathMessageKey, nameof(archivePath), ex);
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(ArchiveFileNotFoundMessageKey, fullPath);
        }

        ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(archivePath));

        if (HasReparsePointInExistingPathFromVolumeRoot(fullPath))
        {
            throw new InvalidOperationException(InvalidArchivePathMessageKey);
        }

        return fullPath;
    }

    private static bool TryNormalizeArchivePath(string archivePath, out string fullArchivePath)
    {
        fullArchivePath = string.Empty;

        try
        {
            fullArchivePath = ResolveArchivePathOrThrow(archivePath);
            return true;
        }
        catch (Exception ex) when (IsExpectedExtractionException(ex) || ex is FileNotFoundException)
        {
            return false;
        }
    }

    private static string ResolveDestinationDirectoryOrThrow(string destinationDirectory)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException(InvalidDestinationPathMessageKey, nameof(destinationDirectory));
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(destinationDirectory.Trim());
        }
        catch (Exception ex) when (IsExpectedExtractionException(ex))
        {
            throw new ArgumentException(InvalidDestinationPathMessageKey, nameof(destinationDirectory), ex);
        }

        ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(destinationDirectory));

        if (IsUnsafeRoot(fullPath) || HasReparsePointInExistingPathFromVolumeRoot(fullPath))
        {
            throw new InvalidOperationException(InvalidDestinationPathMessageKey);
        }

        return fullPath;
    }

    private static void EnsureSafeDestinationDirectory(string extractionRoot)
    {
        string fullPath = Path.GetFullPath(extractionRoot);

        if (IsUnsafeRoot(fullPath)
            || HasReparsePointInExistingPathFromVolumeRoot(fullPath))
        {
            throw new InvalidOperationException(InvalidDestinationPathMessageKey);
        }

        Directory.CreateDirectory(fullPath);

        if (!Directory.Exists(fullPath)
            || HasReparsePointInExistingPathFromVolumeRoot(fullPath))
        {
            throw new InvalidOperationException(InvalidDestinationPathMessageKey);
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
        catch (Exception ex) when (IsExpectedExtractionException(ex))
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
        catch (Exception ex) when (IsExpectedExtractionException(ex))
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
        catch (Exception ex) when (IsExpectedExtractionException(ex))
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
        catch (Exception ex) when (IsExpectedExtractionException(ex))
        {
            return true;
        }
    }

    private static bool IsSamePathOrChild(string candidatePath, string rootPath)
    {
        string candidate = TrimDirectorySeparators(Path.GetFullPath(candidatePath));
        string root = TrimDirectorySeparators(Path.GetFullPath(rootPath));

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(EnsureDirectorySeparatorSuffix(root), StringComparison.OrdinalIgnoreCase);
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

    private static bool IsExpectedExtractionException(Exception ex) =>
        ex is ArgumentException
        or InvalidOperationException
        or NotSupportedException
        or PathTooLongException
        or IOException
        or UnauthorizedAccessException
        or System.Security.SecurityException;
}