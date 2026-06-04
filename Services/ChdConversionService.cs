using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public sealed class ChdConversionService
{
    private const string UserCancelledMessageKey = "LocStatus_UserCancelled";
    private const string ConversionSuccessMessageKey = "LocConversion_Success";
    private const string ConversionFailedMessageKey = "LocConversion_Failed";
    private const string ExtractionSuccessMessageKey = "LocExtraction_Success";
    private const string ExtractionFailedMessageKey = "LocExtraction_Failed";
    private const string InvalidChdmanPathMessageKey = "LocConversion_InvalidChdmanPath";
    private const string InvalidInputPathMessageKey = "LocConversion_InvalidInputPath";
    private const string InvalidOutputPathMessageKey = "LocConversion_InvalidOutputPath";
    private const string ChdmanNotFoundMessageKey = "LocConversion_ChdmanNotFound";
    private const string InputFileNotFoundMessageKey = "LocConversion_InputFileNotFound";
    private const string OutputDirectoryMissingMessageKey = "LocConversion_OutputDirectoryMissing";
    private const string InvalidChdPathMessageKey = "LocExtraction_InvalidChdPath";
    private const string InvalidCueOutputPathMessageKey = "LocExtraction_InvalidCueOutputPath";
    private const string BinOutputDirectoryMissingMessageKey = "LocExtraction_BinOutputDirectoryMissing";
    private const string ExtractionKindRequiresChdInputMessageKey = "LocExtraction_KindRequiresChdInput";
    private const string InvalidCompressionSettingMessageKey = "LocConversion_InvalidCompressionSetting";
    private const string InvalidDvdHunkSizeMessageKey = "LocConversion_InvalidDvdHunkSize";
    private const string InvalidCdHunkSizeMessageKey = "LocConversion_InvalidCdHunkSize";
    private const string InvalidCueBinDependencyMessageKey = "LocChdmanContract_InvalidCueBinDependency";

    private static readonly Regex CueFileReferenceRegex = new(
        "^\\s*FILE\\s+(?:\"(?<q>[^\"]+)\"|(?<u>\\S+))",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    public async Task<ChdConversionResult> ConvertToChdAsync(
        string chdmanPath,
        string inputPath,
        string outputPath,
        int maxProcessorCount = 0,
        bool enableAutoResourceLimiter = true,
        int reservedLogicalCores = 2,
        string? compressionCodecs = null,
        int hunkSizeBytes = 0,
        IProgress<int>? progress = null,
        Action<int>? onProcessStarted = null,
        CancellationToken cancellationToken = default,
        ChdmanExtractionKind extractionKind = ChdmanExtractionKind.None,
        IsoCreateCommandOverride isoCreateCommandOverride = IsoCreateCommandOverride.Auto,
        IProgress<PerformanceSample>? performanceProgress = null,
        bool computeInputSha1 = false,
        long? expectedOutputBytes = null,
        bool allowOverwriteOutput = false,
        bool enableDiskSpaceGuard = true)
    {
        if (string.IsNullOrWhiteSpace(chdmanPath))
        {
            throw new ArgumentException(InvalidChdmanPathMessageKey, nameof(chdmanPath));
        }

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException(InvalidInputPathMessageKey, nameof(inputPath));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException(InvalidOutputPathMessageKey, nameof(outputPath));
        }

        if (!File.Exists(chdmanPath))
        {
            throw new FileNotFoundException(ChdmanNotFoundMessageKey, chdmanPath);
        }

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException(InputFileNotFoundMessageKey, inputPath);
        }

        ConversionPathValidator.ThrowIfUnsafeForChdman(chdmanPath, nameof(chdmanPath));
        ConversionPathValidator.ThrowIfUnsafeForChdman(inputPath, nameof(inputPath));
        ConversionPathValidator.ThrowIfUnsafeForChdman(outputPath, nameof(outputPath));

        string resolvedInputPath = NormalizePathForCli(inputPath);
        string resolvedOutputPath = NormalizePathForCli(outputPath);
        string inputExtension = Path.GetExtension(resolvedInputPath).ToLowerInvariant();

        (string command, IsoChdmanCreateDiagnostics? isoDiagnostics) =
            ResolveTwoWayCommandWithOptionalIsoDiagnostics(
                inputExtension,
                extractionKind,
                resolvedInputPath,
                isoCreateCommandOverride);

        string logsDirectory = BuildLogsDirectory();
        string logPath = Path.Combine(
            logsDirectory,
            $"convert_{DateTime.Now:yyyyMMdd_HHmmss}_{SanitizeFileName(Path.GetFileNameWithoutExtension(resolvedInputPath))}.log");

        if (isoDiagnostics.HasValue)
        {
            IsoChdmanCreateDiagnostics diagnostics = isoDiagnostics.Value;
            Log.Information(
                "ISO chdman create command: Platform={Platform}, Confidence={Confidence}, Reason={Reason}, SizeBytes={SizeBytes}, AutoSuggestedCommand={AutoSuggested}, Override={Override}, Command={Command}",
                diagnostics.PlatformName,
                diagnostics.ConfidenceScore,
                diagnostics.DetectionReason,
                diagnostics.FileLengthBytes,
                diagnostics.AutoSuggestedCommand,
                diagnostics.OverrideMode,
                diagnostics.Command);
        }

        if (!TryValidateDescriptorDependenciesBeforeChdman(resolvedInputPath, command, out string descriptorFailureMessageKey))
        {
            Log.Warning(
                "CHD descriptor preflight rejected input before chdman. Input={InputPath}; Command={Command}; MessageKey={MessageKey}",
                resolvedInputPath,
                command,
                descriptorFailureMessageKey);

            return new ChdConversionResult
            {
                IsSuccess = false,
                WasCancelled = false,
                ExitCode = 1,
                InputPath = inputPath,
                OutputPath = outputPath,
                CommandLine = string.Empty,
                Output = string.Empty,
                Error = string.Empty,
                Message = descriptorFailureMessageKey,
                LogPath = string.Empty
            };
        }

        string diskPreflightMessageKey;
        string diskPreflightOperationKey;
        DiskPreflightMode diskPreflightMode = command.StartsWith("extract", StringComparison.OrdinalIgnoreCase)
            ? DiskPreflightMode.ExtractFromChd
            : DiskPreflightMode.CreateChd;

        if (enableDiskSpaceGuard)
        {
            DiskPreflightResult diskPreflight = DiskSpacePreflightService.CheckOrThrow(
                resolvedInputPath,
                resolvedOutputPath,
                command,
                expectedOutputBytes);

            diskPreflightMessageKey = diskPreflight.MessageKey;
            diskPreflightOperationKey = diskPreflight.OperationKey;

            Log.Information(
                "Disk preflight passed. Root={Root}, InputBytes={InputBytes}, EstimatedRequiredBytes={EstimatedRequiredBytes}, AvailableFreeBytes={AvailableFreeBytes}, MessageKey={MessageKey}, OperationKey={OperationKey}",
                diskPreflight.TargetRoot,
                diskPreflight.InputBytes,
                diskPreflight.EstimatedRequiredBytes,
                diskPreflight.AvailableFreeBytes,
                diskPreflight.MessageKey,
                diskPreflight.OperationKey);
        }
        else
        {
            diskPreflightMessageKey = "DiskPreflightDisabled";
            diskPreflightOperationKey = DiskSpacePreflightService.DescribeOperationKey(command, diskPreflightMode);

            Log.Information(
                "Disk preflight skipped because EnableDiskSpaceGuard is disabled. Input={InputPath}, Output={OutputPath}, Command={Command}, OperationKey={OperationKey}",
                resolvedInputPath,
                resolvedOutputPath,
                command,
                diskPreflightOperationKey);
        }

        FileHashResult? inputSha1 = null;
        if (computeInputSha1)
        {
            inputSha1 = await FileHashService.ComputeAsync(resolvedInputPath, FileHashAlgorithm.SHA1, cancellationToken).ConfigureAwait(false);
            Log.Information("Input SHA1 computed before chdman. Path={Path}, SHA1={SHA1}, Bytes={Bytes}", inputSha1.Path, inputSha1.Hex, inputSha1.BytesRead);
        }

        string? outputDirectory = Path.GetDirectoryName(resolvedOutputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException(OutputDirectoryMissingMessageKey);
        }

        Directory.CreateDirectory(outputDirectory);

        bool isExtractCommand = IsExtractCommand(command);
        string resolvedCompression = string.Empty;
        int resolvedHunkSizeBytes = 0;
        string hunkPolicyNote = string.Empty;

        if (!isExtractCommand)
        {
            resolvedCompression = ResolveCompressionSetting(compressionCodecs, command);
            resolvedHunkSizeBytes = ResolveHunkSizeSetting(hunkSizeBytes, command, resolvedInputPath, out hunkPolicyNote);
        }

        if (!string.IsNullOrWhiteSpace(hunkPolicyNote))
        {
            Log.Information(
                "CHD hunk-size policy. Command={Command}, Input={InputPath}, Requested={RequestedHunkSetting}, Resolved={ResolvedHunkSize}, Note={Note}",
                command,
                resolvedInputPath,
                hunkSizeBytes,
                resolvedHunkSizeBytes,
                hunkPolicyNote);
        }

        var arguments = new List<string> { command, "-i", resolvedInputPath, "-o", resolvedOutputPath };
        if (extractionKind == ChdmanExtractionKind.ExtractCd)
        {
            arguments.Add("-ob");
            arguments.Add(BuildExtractCdBinOutputPath(resolvedOutputPath));
        }

        if (allowOverwriteOutput && (isExtractCommand || IsCreateCommand(command)))
        {
            arguments.Add("-f");
        }

        int availableLogicalProcessors = ProcessorTopologyService.GetAvailableLogicalProcessorCount();
        int normalizedProcessorLimit = ProcessorTopologyService.ResolveChdmanProcessorCount(
            maxProcessorCount,
            enableAutoResourceLimiter,
            reservedLogicalCores);
        int passedProcessorLimit = isExtractCommand ? 0 : normalizedProcessorLimit;

        if (passedProcessorLimit > 0)
        {
            arguments.Add("--numprocessors");
            arguments.Add(passedProcessorLimit.ToString());
        }

        if (!isExtractCommand)
        {
            if (!string.IsNullOrWhiteSpace(resolvedCompression))
            {
                arguments.Add("-c");
                arguments.Add(resolvedCompression);
            }

            if (resolvedHunkSizeBytes > 0)
            {
                arguments.Add("-hs");
                arguments.Add(resolvedHunkSizeBytes.ToString());
            }
        }

        string displayCommandLine = ChdmanCliRunner.FormatCommandLineForDisplay(chdmanPath, arguments);

        Log.Information(
            "CHD conversion starting. Input={InputPath}, Output={OutputPath}, Command={Command}, RequestedProcessors={RequestedProcessors}, PassedProcessors={PassedProcessors}, AutoLimiter={AutoLimiter}, ReservedLogicalCores={ReservedLogicalCores}, AvailableLogicalProcessors={AvailableLogicalProcessors}",
            resolvedInputPath,
            resolvedOutputPath,
            command,
            maxProcessorCount,
            passedProcessorLimit,
            enableAutoResourceLimiter,
            reservedLogicalCores,
            availableLogicalProcessors);
        Log.Information("CHDMAN CMD: {Args}", displayCommandLine);

        ChdmanCliRunner.Result run;
        string? resultMessageKeyOverride = null;
        try
        {
            run = await ChdmanCliRunner.ExecuteAsync(
                executablePath: chdmanPath,
                arguments: arguments,
                parseProgressPercent: progress is not null,
                progress: progress,
                onProcessStarted: onProcessStarted,
                cancellationToken: cancellationToken,
                exclusiveFileAccessPath: resolvedInputPath,
                monitoredOutputPath: resolvedOutputPath,
                performanceProgress: performanceProgress);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryDeleteIncompleteOutputs(resolvedOutputPath, isExtractCommand, "operation cancelled before result was returned");
            Log.Debug("CHD conversion cancelled. Input: {InputPath}", inputPath);

            return new ChdConversionResult
            {
                IsSuccess = false,
                WasCancelled = true,
                ExitCode = ChdmanProcessRunner.CanceledExitCode,
                InputPath = inputPath,
                OutputPath = outputPath,
                CommandLine = displayCommandLine,
                Message = UserCancelledMessageKey,
                LogPath = logPath
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CHD conversion threw. Input: {InputPath}", inputPath);
            throw;
        }

        if (!run.WasCancelled
            && run.ExitCode != 0
            && IsCreateCdHunkSizeMultipleError(run, out int incompatibleHunkSize, out int requiredSectorSize))
        {
            const string hunkFailureTechnicalMessage = "CreateCD hunk size is not aligned with the media sector size.";

            if (TryBuildCreateCdHunkRetrySize(hunkSizeBytes, requiredSectorSize, resolvedHunkSizeBytes, out int retryHunkSizeBytes))
            {
                TryDeleteIncompleteOutputs(resolvedOutputPath, isExtractCommand, "createcd hunk-size retry");
                ReplaceOrAddHunkSizeArgument(arguments, retryHunkSizeBytes);
                resolvedHunkSizeBytes = retryHunkSizeBytes;
                displayCommandLine = ChdmanCliRunner.FormatCommandLineForDisplay(chdmanPath, arguments);

                Log.Warning(
                    "Retrying createcd with media-aligned hunk size after chdman rejected HunkSize={RejectedHunkSize} for SectorSize={SectorSize}. RetryHunkSize={RetryHunkSize}. Input={InputPath}",
                    incompatibleHunkSize,
                    requiredSectorSize,
                    retryHunkSizeBytes,
                    resolvedInputPath);

                try
                {
                    run = await ChdmanCliRunner.ExecuteAsync(
                        executablePath: chdmanPath,
                        arguments: arguments,
                        parseProgressPercent: progress is not null,
                        progress: progress,
                        onProcessStarted: onProcessStarted,
                        cancellationToken: cancellationToken,
                        exclusiveFileAccessPath: resolvedInputPath,
                        monitoredOutputPath: resolvedOutputPath,
                        performanceProgress: performanceProgress).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    TryDeleteIncompleteOutputs(resolvedOutputPath, isExtractCommand, "createcd hunk-size retry cancelled");
                    Log.Debug("CHD conversion cancelled during createcd hunk-size retry. Input: {InputPath}", inputPath);

                    return new ChdConversionResult
                    {
                        IsSuccess = false,
                        WasCancelled = true,
                        ExitCode = ChdmanProcessRunner.CanceledExitCode,
                        InputPath = inputPath,
                        OutputPath = outputPath,
                        CommandLine = displayCommandLine,
                        Output = run.StandardOutput,
                        Error = run.StandardError,
                        Message = UserCancelledMessageKey,
                        LogPath = logPath
                    };
                }
            }
            else if (hunkSizeBytes > 0)
            {
                resultMessageKeyOverride = InvalidCdHunkSizeMessageKey;
                run = new ChdmanCliRunner.Result
                {
                    ExitCode = run.ExitCode,
                    WasCancelled = false,
                    StandardOutput = run.StandardOutput,
                    StandardError = string.IsNullOrWhiteSpace(run.StandardError)
                        ? hunkFailureTechnicalMessage
                        : hunkFailureTechnicalMessage + Environment.NewLine + run.StandardError
                };
            }
        }

        if (run.WasCancelled)
        {
            TryDeleteIncompleteOutputs(resolvedOutputPath, isExtractCommand, "cancelled");
            Log.Debug("CHD conversion cancelled. Input: {InputPath}", inputPath);

            return new ChdConversionResult
            {
                IsSuccess = false,
                WasCancelled = true,
                ExitCode = run.ExitCode,
                InputPath = inputPath,
                OutputPath = outputPath,
                CommandLine = displayCommandLine,
                Output = run.StandardOutput,
                Error = run.StandardError,
                Message = UserCancelledMessageKey,
                LogPath = logPath
            };
        }

        if (!run.WasCancelled
            && run.ExitCode != 0
            && extractionKind == ChdmanExtractionKind.ExtractCd
            && IsExtractCdSplitbinPatternRequired(run))
        {
            TryDeleteIncompleteOutputs(resolvedOutputPath, isExtractCommand, "extractcd splitbin retry");
            ReplaceExtractCdBinOutputArgument(arguments, BuildSplitBinExtractCdBinOutputPath(resolvedOutputPath));
            displayCommandLine = ChdmanCliRunner.FormatCommandLineForDisplay(chdmanPath, arguments);

            Log.Warning(
                "Retrying extractcd with track-number output pattern after chdman required splitbin naming. Input={InputPath}; Output={OutputPath}",
                resolvedInputPath,
                resolvedOutputPath);
            Log.Information("CHDMAN CMD: {Args}", displayCommandLine);

            try
            {
                run = await ChdmanCliRunner.ExecuteAsync(
                    executablePath: chdmanPath,
                    arguments: arguments,
                    parseProgressPercent: progress is not null,
                    progress: progress,
                    onProcessStarted: onProcessStarted,
                    cancellationToken: cancellationToken,
                    exclusiveFileAccessPath: resolvedInputPath,
                    monitoredOutputPath: resolvedOutputPath,
                    performanceProgress: performanceProgress).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryDeleteIncompleteOutputs(resolvedOutputPath, isExtractCommand, "extractcd splitbin retry cancelled");
                Log.Debug("CHD conversion cancelled during extractcd splitbin retry. Input: {InputPath}", inputPath);

                return new ChdConversionResult
                {
                    IsSuccess = false,
                    WasCancelled = true,
                    ExitCode = ChdmanProcessRunner.CanceledExitCode,
                    InputPath = inputPath,
                    OutputPath = outputPath,
                    CommandLine = displayCommandLine,
                    Output = run.StandardOutput,
                    Error = run.StandardError,
                    Message = UserCancelledMessageKey,
                    LogPath = logPath
                };
            }
        }

        if (run.WasCancelled)
        {
            TryDeleteIncompleteOutputs(resolvedOutputPath, isExtractCommand, "cancelled");
            Log.Debug("CHD conversion cancelled. Input: {InputPath}", inputPath);

            return new ChdConversionResult
            {
                IsSuccess = false,
                WasCancelled = true,
                ExitCode = run.ExitCode,
                InputPath = inputPath,
                OutputPath = outputPath,
                CommandLine = displayCommandLine,
                Output = run.StandardOutput,
                Error = run.StandardError,
                Message = UserCancelledMessageKey,
                LogPath = logPath
            };
        }

        string output = run.StandardOutput;
        string error = run.StandardError;

        bool extractCdCueContractValid = true;
        if (run.ExitCode == 0 && extractionKind == ChdmanExtractionKind.ExtractCd)
        {
            extractCdCueContractValid = TryNormalizeExtractedCueBinOutput(resolvedOutputPath);
            if (!extractCdCueContractValid)
            {
                resultMessageKeyOverride = InvalidCueBinDependencyMessageKey;
                error = string.Empty;
                Log.Warning(
                    "extractcd output failed CUE/BIN contract validation after chdman succeeded. Cue={CuePath}",
                    resolvedOutputPath);
            }
        }

        bool success = run.ExitCode == 0 && extractCdCueContractValid && VerifyOutputExists(resolvedOutputPath, isExtractCommand);

        if (success)
        {
            progress?.Report(100);
            Log.Information("chdman finished successfully. Command: {Command}, Input: {InputPath}", command, inputPath);
        }
        else
        {
            TryDeleteIncompleteOutputs(resolvedOutputPath, isExtractCommand, "failed");
            Log.Error(
                "chdman failed. Command: {Command}, Input: {InputPath}, ExitCode: {ExitCode}, StdErr: {StdErr}",
                command,
                inputPath,
                run.ExitCode,
                error);
        }

        var logBuilder = new StringBuilder();
        logBuilder.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        logBuilder.AppendLine($"Command: {command}");
        logBuilder.AppendLine($"Input: {inputPath}");
        logBuilder.AppendLine($"Output: {outputPath}");
        logBuilder.AppendLine($"ExitCode: {run.ExitCode}");
        logBuilder.AppendLine($"Compression: {(string.IsNullOrWhiteSpace(resolvedCompression) ? "default" : resolvedCompression)}");
        logBuilder.AppendLine($"HunkSize: {(resolvedHunkSizeBytes > 0 ? resolvedHunkSizeBytes.ToString() : "default")}");
        logBuilder.AppendLine($"AvailableLogicalProcessors: {availableLogicalProcessors}");
        logBuilder.AppendLine($"RequestedProcessors: {(maxProcessorCount > 0 ? maxProcessorCount.ToString() : "auto")}");
        logBuilder.AppendLine($"AutoResourceLimiter: {enableAutoResourceLimiter}");
        logBuilder.AppendLine($"ReservedLogicalCores: {reservedLogicalCores}");
        logBuilder.AppendLine($"PassedProcessors: {(passedProcessorLimit > 0 ? passedProcessorLimit.ToString() : "default")}");
        logBuilder.AppendLine($"DiskPreflightMessageKey: {diskPreflightMessageKey}");
        logBuilder.AppendLine($"DiskPreflightOperationKey: {diskPreflightOperationKey}");
        if (inputSha1 is not null)
        {
            logBuilder.AppendLine($"InputSHA1: {inputSha1.Hex}");
        }
        logBuilder.AppendLine($"Success: {success}");
        logBuilder.AppendLine();

        if (!string.IsNullOrWhiteSpace(output))
        {
            logBuilder.AppendLine("=== STDOUT ===");
            logBuilder.AppendLine(output);
            logBuilder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            logBuilder.AppendLine("=== STDERR ===");
            logBuilder.AppendLine(error);
            logBuilder.AppendLine();
        }

        await File.WriteAllTextAsync(logPath, logBuilder.ToString(), CancellationToken.None);

        return new ChdConversionResult
        {
            IsSuccess = success,
            WasCancelled = run.WasCancelled,
            ExitCode = run.ExitCode,
            InputPath = inputPath,
            OutputPath = outputPath,
            CommandLine = displayCommandLine,
            Output = output,
            Error = error,
            Message = success
                ? (isExtractCommand ? ExtractionSuccessMessageKey : ConversionSuccessMessageKey)
                : resultMessageKeyOverride ?? (isExtractCommand ? ExtractionFailedMessageKey : ConversionFailedMessageKey),
            LogPath = logPath
        };
    }

    public static string BuildExtractOutputPathReplacingChdExtension(string chdPath, string newExtensionWithDot)
    {
        if (string.IsNullOrWhiteSpace(chdPath))
        {
            throw new ArgumentException(InvalidChdPathMessageKey, nameof(chdPath));
        }

        string withoutDot = newExtensionWithDot.StartsWith('.')
            ? newExtensionWithDot[1..]
            : newExtensionWithDot;

        return Path.ChangeExtension(chdPath, withoutDot);
    }

    public static string BuildExtractCdBinOutputPath(string cueOutputPath)
    {
        if (string.IsNullOrWhiteSpace(cueOutputPath))
        {
            throw new ArgumentException(InvalidCueOutputPathMessageKey, nameof(cueOutputPath));
        }

        string? directory = Path.GetDirectoryName(cueOutputPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(BinOutputDirectoryMissingMessageKey);
        }

        string stem = Path.GetFileNameWithoutExtension(cueOutputPath);
        stem = string.IsNullOrWhiteSpace(stem) ? "track" : stem;
        return Path.Combine(directory, $"{stem}.bin");
    }

    private static string BuildSingleBinExtractCdBinOutputPath(string cueOutputPath)
    {
        string? directory = Path.GetDirectoryName(cueOutputPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(BinOutputDirectoryMissingMessageKey);
        }

        string stem = Path.GetFileNameWithoutExtension(cueOutputPath);
        stem = string.IsNullOrWhiteSpace(stem) ? "track" : stem;
        return Path.Combine(directory, $"{stem}.bin");
    }

    private static string BuildSplitBinExtractCdBinOutputPath(string cueOutputPath)
    {
        if (string.IsNullOrWhiteSpace(cueOutputPath))
        {
            throw new ArgumentException(InvalidCueOutputPathMessageKey, nameof(cueOutputPath));
        }

        string? directory = Path.GetDirectoryName(cueOutputPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(BinOutputDirectoryMissingMessageKey);
        }

        string stem = Path.GetFileNameWithoutExtension(cueOutputPath);
        stem = string.IsNullOrWhiteSpace(stem) ? "track" : stem;
        return Path.Combine(directory, $"{stem} (Track %t).bin");
    }

    public string BuildCommand(
        string inputPath,
        ChdmanExtractionKind extractionKind = ChdmanExtractionKind.None,
        IsoCreateCommandOverride isoCreateCommandOverride = IsoCreateCommandOverride.Auto)
    {
        string ext = Path.GetExtension(inputPath).ToLowerInvariant();
        return ResolveTwoWayCommandWithOptionalIsoDiagnostics(ext, extractionKind, inputPath, isoCreateCommandOverride).Command;
    }

    private static (string Command, IsoChdmanCreateDiagnostics? IsoDiagnostics) ResolveTwoWayCommandWithOptionalIsoDiagnostics(
        string inputExtension,
        ChdmanExtractionKind extractionKind,
        string? fullInputPathForIsoClassification,
        IsoCreateCommandOverride isoCreateCommandOverride)
    {
        bool isChdInput = string.Equals(inputExtension, ".chd", StringComparison.OrdinalIgnoreCase);

        if (isChdInput)
        {
            ChdWorkflowProfilePlan extractionPlan = ChdWorkflowProfilePlanner.PlanExtractionByKind(extractionKind);
            if (!extractionPlan.IsSupported || string.IsNullOrWhiteSpace(extractionPlan.Command))
            {
                throw new InvalidOperationException(extractionPlan.FailureMessage);
            }

            return (extractionPlan.Command, null);
        }

        if (extractionKind != ChdmanExtractionKind.None)
        {
            throw new InvalidOperationException(ExtractionKindRequiresChdInputMessageKey);
        }

        ChdWorkflowProfilePlan createPlan = ChdWorkflowProfilePlanner.PlanCreateFromSource(
            fullInputPathForIsoClassification ?? string.Empty,
            isoCreateCommandOverride,
            ChdMediaContainerKind.DirectFile);

        if (!createPlan.IsSupported || string.IsNullOrWhiteSpace(createPlan.Command))
        {
            throw new NotSupportedException(createPlan.FailureMessage);
        }

        return (createPlan.Command, createPlan.IsoDiagnostics);
    }

    private static bool IsCreateCommand(string command) =>
        string.Equals(command, "createcd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "createdvd", StringComparison.OrdinalIgnoreCase);

    private static bool IsExtractCommand(string command) =>
        string.Equals(command, "extractcd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "extractdvd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "extracthd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "extractraw", StringComparison.OrdinalIgnoreCase);

    private static bool IsExtractCdSplitbinPatternRequired(ChdmanCliRunner.Result run)
    {
        string text = string.Concat(run.StandardError, Environment.NewLine, run.StandardOutput);
        return text.Contains("track number variable (%t) must be specified", StringComparison.OrdinalIgnoreCase)
            || text.Contains("--splitbin", StringComparison.OrdinalIgnoreCase)
                && text.Contains("%t", StringComparison.OrdinalIgnoreCase);
    }

    private static void ReplaceExtractCdBinOutputArgument(List<string> arguments, string binOutputPath)
    {
        int optionIndex = arguments.FindIndex(static arg => string.Equals(arg, "-ob", StringComparison.OrdinalIgnoreCase));
        if (optionIndex >= 0 && optionIndex + 1 < arguments.Count)
        {
            arguments[optionIndex + 1] = binOutputPath;
            return;
        }

        arguments.Add("-ob");
        arguments.Add(binOutputPath);
    }

    private static bool ContainsTrackToken(string value) =>
        !string.IsNullOrEmpty(value)
        && value.Contains("%t", StringComparison.OrdinalIgnoreCase);

    private static void TryDeleteIncompleteOutputs(string outputPath, bool isExtractCommand, string reason)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        string fullOutputPath;
        try
        {
            fullOutputPath = Path.GetFullPath(outputPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Log.Warning(ex, "Could not resolve incomplete output path for cleanup. Output={OutputPath}, Reason={Reason}", outputPath, reason);
            return;
        }

        IReadOnlyList<string> knownCompanions = isExtractCommand
            ? ResolveKnownExtractionCompanions(fullOutputPath)
            : Array.Empty<string>();

        TryDeleteIncompleteFile(fullOutputPath, reason);
        TryDeleteIncompleteFile(Path.ChangeExtension(fullOutputPath, ".sbi"), reason);

        foreach (string companion in knownCompanions)
        {
            TryDeleteIncompleteFile(companion, reason);
        }
    }

    private static bool TryNormalizeExtractedCueBinOutput(string cueOutputPath)
    {
        if (string.IsNullOrWhiteSpace(cueOutputPath)
            || !string.Equals(Path.GetExtension(cueOutputPath), ".cue", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(cueOutputPath))
        {
            return false;
        }

        if (!TryRepairLiteralTrackTokenCueOutput(cueOutputPath))
        {
            return false;
        }

        if (VerifyCueBinDependenciesStrict(cueOutputPath))
        {
            return true;
        }

        string binOutputPath = BuildSingleBinExtractCdBinOutputPath(cueOutputPath);
        if (!File.Exists(binOutputPath))
        {
            return false;
        }

        try
        {
            FileInfo binInfo = new(binOutputPath);
            if (binInfo.Length <= 0)
            {
                return false;
            }

            string[] lines = File.ReadAllLines(cueOutputPath, Encoding.UTF8);
            string binFileName = Path.GetFileName(binOutputPath);
            bool foundFileStatement = false;
            bool changed = false;

            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                string trimmed = line.TrimStart();
                if (!trimmed.StartsWith("FILE", StringComparison.OrdinalIgnoreCase)
                    || trimmed.Length > 4 && !char.IsWhiteSpace(trimmed[4]))
                {
                    continue;
                }

                string leadingWhitespace = line[..(line.Length - line.TrimStart().Length)];
                string replacement = $"{leadingWhitespace}FILE \"{binFileName}\" BINARY";
                foundFileStatement = true;

                if (!string.Equals(line, replacement, StringComparison.Ordinal))
                {
                    lines[index] = replacement;
                    changed = true;
                }
            }

            if (!foundFileStatement)
            {
                List<string> updated = new(lines);
                int insertionIndex = FindFirstCueTrackLineIndex(updated);
                updated.Insert(insertionIndex < 0 ? 0 : insertionIndex, $"FILE \"{binFileName}\" BINARY");
                lines = updated.ToArray();
                changed = true;
            }

            if (changed)
            {
                File.WriteAllLines(cueOutputPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                Log.Information(
                    "Normalized extractcd CUE/BIN output contract. Cue={CuePath}; Bin={BinPath}",
                    cueOutputPath,
                    binOutputPath);
            }

            return VerifyCueBinDependenciesStrict(cueOutputPath);
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException
                                  or InvalidOperationException)
        {
            Log.Warning(ex, "Could not normalize extractcd CUE/BIN output contract. Cue={CuePath}", cueOutputPath);
            return false;
        }
    }

    private static bool TryRepairLiteralTrackTokenCueOutput(string cueOutputPath)
    {
        try
        {
            string? cueDirectory = Path.GetDirectoryName(Path.GetFullPath(cueOutputPath));
            if (string.IsNullOrWhiteSpace(cueDirectory))
            {
                return false;
            }

            string[] lines = File.ReadAllLines(cueOutputPath, Encoding.UTF8);
            var tokenLineIndexes = new List<int>();
            var tokenSourcePaths = new List<string>();

            for (int index = 0; index < lines.Length; index++)
            {
                if (!TryReadCueFileStatementStrict(lines[index], out string referencedFileName, out bool hasFileStatement))
                {
                    if (hasFileStatement)
                    {
                        return false;
                    }

                    continue;
                }

                if (!ContainsTrackToken(referencedFileName))
                {
                    continue;
                }

                if (!TryResolveCompanionPathWithinDirectory(cueDirectory, referencedFileName, out string? sourcePath)
                    || string.IsNullOrWhiteSpace(sourcePath)
                    || !File.Exists(sourcePath)
                    || new FileInfo(sourcePath).Length <= 0)
                {
                    return false;
                }

                tokenLineIndexes.Add(index);
                tokenSourcePaths.Add(Path.GetFullPath(sourcePath));
            }

            if (tokenLineIndexes.Count == 0)
            {
                return true;
            }

            string[] uniqueTokenSources =
            [
                .. tokenSourcePaths.Distinct(StringComparer.OrdinalIgnoreCase)
            ];

            if (uniqueTokenSources.Length != 1)
            {
                return false;
            }

            string singleBinPath = BuildSingleBinExtractCdBinOutputPath(cueOutputPath);
            string singleBinFullPath = Path.GetFullPath(singleBinPath);
            string tokenSourcePath = uniqueTokenSources[0];

            if (!string.Equals(tokenSourcePath, singleBinFullPath, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(singleBinFullPath))
                {
                    return false;
                }

                File.Move(tokenSourcePath, singleBinFullPath);
            }

            string binFileName = Path.GetFileName(singleBinFullPath);
            foreach (int lineIndex in tokenLineIndexes)
            {
                string line = lines[lineIndex];
                string leadingWhitespace = line[..(line.Length - line.TrimStart().Length)];
                lines[lineIndex] = $"{leadingWhitespace}FILE \"{binFileName}\" BINARY";
            }

            File.WriteAllLines(cueOutputPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Log.Information(
                "Repaired literal extractcd track token in single-bin CUE output. Cue={CuePath}; Bin={BinPath}",
                cueOutputPath,
                singleBinFullPath);

            return true;
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException
                                  or InvalidOperationException)
        {
            Log.Warning(ex, "Could not repair literal extractcd track token. Cue={CuePath}", cueOutputPath);
            return false;
        }
    }

    private static int FindFirstCueTrackLineIndex(IReadOnlyList<string> lines)
    {
        for (int index = 0; index < lines.Count; index++)
        {
            if (lines[index].TrimStart().StartsWith("TRACK", StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static IReadOnlyList<string> ResolveKnownExtractionCompanions(string outputPath)
    {
        var result = new List<string>();
        string? directory = Path.GetDirectoryName(outputPath);
        string stem = Path.GetFileNameWithoutExtension(outputPath);
        string extension = Path.GetExtension(outputPath);

        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(stem))
        {
            return result;
        }

        if (string.Equals(extension, ".cue", StringComparison.OrdinalIgnoreCase))
        {
            result.Add(BuildSingleBinExtractCdBinOutputPath(outputPath));

            foreach (string trackOutput in EnumerateExtractCdTrackPatternOutputs(directory, stem))
            {
                result.Add(trackOutput);
            }

            foreach (string referenced in TryReadCueReferencedFiles(outputPath))
            {
                if (TryResolveCompanionPathWithinDirectory(directory, referenced, out string? companion)
                    && !string.IsNullOrWhiteSpace(companion))
                {
                    result.Add(companion);
                }
            }
        }
        else if (string.Equals(extension, ".gdi", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string referenced in TryReadGdiReferencedFiles(outputPath))
            {
                if (TryResolveCompanionPathWithinDirectory(directory, referenced, out string? companion)
                    && !string.IsNullOrWhiteSpace(companion))
                {
                    result.Add(companion);
                }
            }
        }

        return DeduplicatePaths(result);
    }

    private static IEnumerable<string> EnumerateExtractCdTrackPatternOutputs(string directory, string stem)
    {
        if (string.IsNullOrWhiteSpace(directory)
            || string.IsNullOrWhiteSpace(stem)
            || !Directory.Exists(directory))
        {
            yield break;
        }

        string pattern = $"{stem} (Track *).bin";
        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException)
        {
            yield break;
        }

        foreach (string candidate in candidates)
        {
            yield return candidate;
        }
    }

    private static IEnumerable<string> TryReadCueReferencedFiles(string cuePath)
    {
        if (!File.Exists(cuePath))
        {
            yield break;
        }

        string text;
        try
        {
            text = File.ReadAllText(cuePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Debug(ex, "Could not read incomplete CUE file for companion cleanup. CuePath={CuePath}", cuePath);
            yield break;
        }

        foreach (Match match in CueFileReferenceRegex.Matches(text))
        {
            string value = match.Groups["q"].Success
                ? match.Groups["q"].Value
                : match.Groups["u"].Value;

            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value.Trim();
            }
        }
    }

    private static IEnumerable<string> TryReadGdiReferencedFiles(string gdiPath)
    {
        if (!File.Exists(gdiPath))
        {
            yield break;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(gdiPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Debug(ex, "Could not read incomplete GDI file for companion cleanup. GdiPath={GdiPath}", gdiPath);
            yield break;
        }

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 5 && !string.IsNullOrWhiteSpace(parts[4]))
            {
                yield return parts[4].Trim();
            }
        }
    }

    private static bool TryResolveCompanionPathWithinDirectory(
        string directory,
        string referencedFileName,
        out string? companionPath)
    {
        companionPath = null;

        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(referencedFileName))
        {
            return false;
        }

        if (Path.IsPathRooted(referencedFileName))
        {
            return false;
        }

        try
        {
            string fullDirectory = EnsureTrailingDirectorySeparator(Path.GetFullPath(directory));
            string combined = Path.GetFullPath(Path.Combine(fullDirectory, referencedFileName));

            if (!combined.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            companionPath = combined;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Log.Debug(ex, "Rejected invalid companion output path. Directory={Directory}, Reference={Reference}", directory, referencedFileName);
            return false;
        }
    }

    private static IReadOnlyList<string> DeduplicatePaths(IEnumerable<string> paths)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                Log.Debug(ex, "Rejected invalid cleanup path candidate. Path={Path}", path);
                continue;
            }

            if (seen.Add(fullPath))
            {
                result.Add(fullPath);
            }
        }

        return result;
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        return Path.EndsInDirectorySeparator(path)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static void TryDeleteIncompleteFile(string? path, string reason)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            string fullPath = Path.GetFullPath(path);

            if (!File.Exists(fullPath))
            {
                return;
            }

            const int maxAttempts = 5;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    File.Delete(fullPath);
                    Log.Information("Deleted incomplete chdman output. Path={Path}, Reason={Reason}", fullPath, reason);
                    return;
                }
                catch (IOException) when (attempt < maxAttempts)
                {
                    Thread.Sleep(250);
                }
                catch (UnauthorizedAccessException) when (attempt < maxAttempts)
                {
                    Thread.Sleep(250);
                }
            }

            if (File.Exists(fullPath))
            {
                Log.Warning("Incomplete output still exists after retry cleanup. Path={Path}, Reason={Reason}", fullPath, reason);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not delete incomplete chdman output. Path={Path}, Reason={Reason}", path, reason);
        }
    }

    private static bool VerifyOutputExists(string outputPath, bool isExtractCommand)
    {
        if (!File.Exists(outputPath))
        {
            return false;
        }

        if (!isExtractCommand)
        {
            return true;
        }

        try
        {
            FileInfo primary = new(outputPath);
            if (primary.Length <= 0)
            {
                return false;
            }

            string ext = primary.Extension.ToLowerInvariant();
            return ext switch
            {
                ".cue" => VerifyCueBundle(primary.FullName),
                ".gdi" => VerifyGdiBundle(primary.FullName),
                _ => true
            };
        }
        catch
        {
            return false;
        }
    }

    private static bool VerifyCueBundle(string cuePath)
    {
        string directory = Path.GetDirectoryName(cuePath) ?? string.Empty;
        bool foundReferencedFile = false;

        foreach (string referenced in TryReadCueReferencedFiles(cuePath))
        {
            if (!TryResolveCompanionPathWithinDirectory(directory, referenced, out string? candidate)
                || string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            foundReferencedFile = true;

            if (!File.Exists(candidate))
            {
                return false;
            }

            FileInfo sidecar = new(candidate);
            if (sidecar.Length <= 0)
            {
                return false;
            }
        }

        return foundReferencedFile;
    }

    private static bool VerifyGdiBundle(string gdiPath)
    {
        string directory = Path.GetDirectoryName(gdiPath) ?? string.Empty;

        string[] lines;
        try
        {
            lines = File.ReadAllLines(gdiPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Debug(ex, "Could not read GDI file for verification. GdiPath={GdiPath}", gdiPath);
            return false;
        }

        if (lines.Length < 2)
        {
            return false;
        }

        bool foundReferencedTrack = false;

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 || string.IsNullOrWhiteSpace(parts[4]))
            {
                return false;
            }

            string referenced = parts[4].Trim();

            if (!TryResolveCompanionPathWithinDirectory(directory, referenced, out string? candidate)
                || string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            foundReferencedTrack = true;

            if (!File.Exists(candidate))
            {
                return false;
            }

            FileInfo sidecar = new(candidate);
            if (sidecar.Length <= 0)
            {
                return false;
            }
        }

        return foundReferencedTrack;
    }

    private static bool TryValidateDescriptorDependenciesBeforeChdman(
        string inputPath,
        string command,
        out string failureMessageKey)
    {
        failureMessageKey = string.Empty;

        try
        {
            if (string.Equals(command, "createcd", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetExtension(inputPath), ".cue", StringComparison.OrdinalIgnoreCase)
                && !VerifyCueBinDependenciesStrict(inputPath))
            {
                failureMessageKey = InvalidCueBinDependencyMessageKey;
                return false;
            }

            ChdWorkflowProfilePlanner.ValidateConversionInputOrThrow(inputPath, command);
            return true;
        }
        catch (InvalidDataException)
        {
            failureMessageKey = InvalidCueBinDependencyMessageKey;
            return false;
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException
                                  or InvalidOperationException
                                  or System.Security.SecurityException)
        {
            failureMessageKey = InvalidCueBinDependencyMessageKey;
            Log.Debug(ex, "Descriptor dependency preflight failed. Input={InputPath}; Command={Command}", inputPath, command);
            return false;
        }
    }

    private static async Task WritePreflightFailureLogAsync(
        string logPath,
        string command,
        string inputPath,
        string outputPath,
        string messageKey,
        CancellationToken cancellationToken)
    {
        var logBuilder = new StringBuilder();
        logBuilder.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        logBuilder.AppendLine($"Command: {command}");
        logBuilder.AppendLine($"Input: {inputPath}");
        logBuilder.AppendLine($"Output: {outputPath}");
        logBuilder.AppendLine("ExitCode: 1");
        logBuilder.AppendLine($"PreflightMessageKey: {messageKey}");
        logBuilder.AppendLine("Success: False");
        logBuilder.AppendLine();
        logBuilder.AppendLine("=== PREFLIGHT ===");
        logBuilder.AppendLine(messageKey);
        logBuilder.AppendLine();

        await File.WriteAllTextAsync(logPath, logBuilder.ToString(), cancellationToken).ConfigureAwait(false);
    }

    private static bool VerifyCueBinDependenciesStrict(string cuePath)
    {
        if (string.IsNullOrWhiteSpace(cuePath)
            || !File.Exists(cuePath)
            || !string.Equals(Path.GetExtension(cuePath), ".cue", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string fullCuePath;
        string? cueDirectory;

        try
        {
            fullCuePath = Path.GetFullPath(cuePath);
            cueDirectory = Path.GetDirectoryName(fullCuePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(cueDirectory))
        {
            return false;
        }

        string safeCueDirectory = EnsureTrailingDirectorySeparator(Path.GetFullPath(cueDirectory));
        bool foundFileStatement = false;

        foreach (string line in File.ReadLines(fullCuePath, Encoding.UTF8))
        {
            if (!TryReadCueFileStatementStrict(line, out string referencedFileName, out bool hasFileStatement))
            {
                if (hasFileStatement)
                {
                    return false;
                }

                continue;
            }

            foundFileStatement = true;

            if (referencedFileName.IndexOf('\0') >= 0
                || ContainsTrackToken(referencedFileName)
                || Path.IsPathRooted(referencedFileName)
                || referencedFileName.Contains(':', StringComparison.Ordinal)
                || referencedFileName.Contains('/')
                || referencedFileName.Contains('\\')
                || ContainsParentTraversalSegment(referencedFileName))
            {
                return false;
            }

            string candidatePath = Path.GetFullPath(Path.Combine(safeCueDirectory, referencedFileName));
            if (!candidatePath.StartsWith(safeCueDirectory, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(candidatePath)
                || new FileInfo(candidatePath).Length <= 0)
            {
                return false;
            }
        }

        return foundFileStatement;
    }

    private static bool TryReadCueFileStatementStrict(
        string line,
        out string referencedFileName,
        out bool hasFileStatement)
    {
        referencedFileName = string.Empty;
        hasFileStatement = false;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        string trimmed = line.TrimStart();
        if (!trimmed.StartsWith("FILE", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trimmed.Length > 4 && !char.IsWhiteSpace(trimmed[4]))
        {
            return false;
        }

        hasFileStatement = true;

        Match match = Regex.Match(
            trimmed,
            "^FILE\\s+(?:\"(?<quoted>[^\"]*)\"|(?<plain>\\S+))\\s+\\S+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

        if (!match.Success)
        {
            return false;
        }

        referencedFileName = match.Groups["quoted"].Success
            ? match.Groups["quoted"].Value.Trim()
            : match.Groups["plain"].Value.Trim();

        return !string.IsNullOrWhiteSpace(referencedFileName);
    }

    private static bool ContainsParentTraversalSegment(string path)
    {
        string[] segments = path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\'],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string segment in segments)
        {
            if (string.Equals(segment, "..", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveCompressionSetting(string? compressionCodecs, string command)
    {
        if (string.IsNullOrWhiteSpace(compressionCodecs))
        {
            return string.Empty;
        }

        string value = compressionCodecs.Trim();
        bool isCd = string.Equals(command, "createcd", StringComparison.OrdinalIgnoreCase);

        if (!value.StartsWith("preset:", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateExplicitCompressionCodecs(value, isCd);
        }

        string preset = value[7..].ToLowerInvariant();

        return (preset, isCd) switch
        {
            ("default", _) => string.Empty,
            ("fast", true) => "cdzs,cdfl",
            ("balanced", true) => "cdzs,cdzl,cdfl",
            ("max", true) => "cdlz,cdzl,cdfl",
            ("fast", false) => "zstd,flac",
            ("balanced", false) => "zstd,zlib,huff,flac",
            ("max", false) => "lzma,zlib,huff,flac",
            _ => string.Empty
        };
    }

    private static string ValidateExplicitCompressionCodecs(string value, bool isCd)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
        {
            return "none";
        }

        HashSet<string> allowed = isCd
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cdlz", "cdzl", "cdzs", "cdfl" }
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "lzma", "zlib", "zstd", "huff", "flac" };

        string[] parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Length > 4)
        {
            throw new InvalidOperationException(InvalidCompressionSettingMessageKey);
        }

        foreach (string codec in parts)
        {
            if (!allowed.Contains(codec))
            {
                throw new InvalidOperationException(InvalidCompressionSettingMessageKey);
            }
        }

        return string.Join(',', parts);
    }

    private static int ResolveHunkSizeSetting(int hunkSizeBytes, string command, string inputPath, out string policyNote)
    {
        policyNote = string.Empty;
        bool isCd = string.Equals(command, "createcd", StringComparison.OrdinalIgnoreCase);

        if (!isCd)
        {
            const int dvdSectorUnitBytes = 2048;
            int resolved = hunkSizeBytes switch
            {
                0 => 0,
                -1 => 16384,
                -2 => 32768,
                -3 => 65536,
                > 0 => hunkSizeBytes,
                _ => 0
            };

            if (resolved <= 0)
            {
                policyNote = hunkSizeBytes == 0
                    ? "createdvd hunk size left to MAME default."
                    : "createdvd hunk preset omitted because the requested value was not recognized.";
                return 0;
            }

            if (resolved % dvdSectorUnitBytes != 0)
            {
                throw new InvalidOperationException(InvalidDvdHunkSizeMessageKey);
            }

            policyNote = hunkSizeBytes > 0
                ? "createdvd explicit hunk size passed after sector-unit validation."
                : "createdvd hunk preset resolved to a 2048-byte aligned value.";
            return resolved;
        }

        if (hunkSizeBytes == 0)
        {
            policyNote = "createcd hunk size left to chdman default.";
            return 0;
        }

        if (TryResolveCreateCdPresetSectorCount(hunkSizeBytes, out int sectorsPerHunk))
        {
            if (TryResolveCdSectorUnitSize(inputPath, out int sectorSize)
                && TryBuildValidHunkSize(sectorSize, sectorsPerHunk, out int resolved))
            {
                policyNote = $"createcd hunk preset resolved from detected sector size {sectorSize} bytes.";
                return resolved;
            }

            policyNote = "createcd hunk preset omitted because the CD sector size could not be proven before execution; chdman will choose a safe default.";
            return 0;
        }

        if (hunkSizeBytes > 0)
        {
            if (!TryResolveCdSectorUnitSize(inputPath, out int sectorSize))
            {
                policyNote = "createcd explicit hunk size omitted because the CD sector size could not be proven before execution; chdman will choose a safe default.";
                return 0;
            }

            if (hunkSizeBytes % sectorSize != 0)
            {
                throw new InvalidOperationException(InvalidCdHunkSizeMessageKey);
            }

            policyNote = "createcd explicit hunk size passed after sector-size validation.";
            return hunkSizeBytes;
        }

        return 0;
    }

    private static bool TryResolveCreateCdPresetSectorCount(int hunkSizeBytes, out int sectorsPerHunk)
    {
        sectorsPerHunk = hunkSizeBytes switch
        {
            -1 => 8,
            -2 => 16,
            -3 => 32,
            _ => 0
        };

        return sectorsPerHunk > 0;
    }

    private static bool TryBuildCreateCdHunkRetrySize(
        int requestedHunkSetting,
        int requiredSectorSize,
        int previousHunkSizeBytes,
        out int retryHunkSizeBytes)
    {
        retryHunkSizeBytes = 0;
        if (!TryResolveCreateCdPresetSectorCount(requestedHunkSetting, out int sectorsPerHunk))
        {
            return false;
        }

        if (!TryBuildValidHunkSize(requiredSectorSize, sectorsPerHunk, out int candidate))
        {
            return false;
        }

        if (candidate == previousHunkSizeBytes)
        {
            return false;
        }

        retryHunkSizeBytes = candidate;
        return true;
    }

    private static bool TryBuildValidHunkSize(int sectorSize, int sectorsPerHunk, out int hunkSizeBytes)
    {
        hunkSizeBytes = 0;
        if (sectorSize <= 0 || sectorsPerHunk <= 0)
        {
            return false;
        }

        long candidate = (long)sectorSize * sectorsPerHunk;
        if (candidate < 16 || candidate > 1_048_576)
        {
            return false;
        }

        hunkSizeBytes = (int)candidate;
        return hunkSizeBytes % sectorSize == 0;
    }

    private static bool TryResolveCdSectorUnitSize(string inputPath, out int sectorSize)
    {
        sectorSize = 0;
        string extension = Path.GetExtension(inputPath).ToLowerInvariant();

        try
        {
            HashSet<int> sizes = extension switch
            {
                ".gdi" => ParseGdiSectorSizes(inputPath),
                ".cue" => ParseCueSectorSizes(inputPath),
                _ => new HashSet<int>()
            };

            if (sizes.Count == 0)
            {
                return false;
            }

            long lcm = 1;
            foreach (int size in sizes)
            {
                lcm = LeastCommonMultiple(lcm, size);
                if (lcm <= 0 || lcm > 1_048_576)
                {
                    return false;
                }
            }

            sectorSize = (int)lcm;
            return sectorSize > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Debug(ex, "Could not resolve CD sector size before createcd. Input={InputPath}", inputPath);
            return false;
        }
    }

    private static HashSet<int> ParseGdiSectorSizes(string gdiPath)
    {
        var sizes = new HashSet<int>();
        foreach (string rawLine in File.ReadLines(gdiPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || !char.IsDigit(line[0]))
            {
                continue;
            }

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4 && int.TryParse(parts[3], out int sectorSize) && IsPlausibleCdSectorSize(sectorSize))
            {
                sizes.Add(sectorSize);
            }
        }

        return sizes;
    }

    private static HashSet<int> ParseCueSectorSizes(string cuePath)
    {
        var sizes = new HashSet<int>();
        foreach (string rawLine in File.ReadLines(cuePath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            foreach (Match match in Regex.Matches(line, @"/(\d{3,5})"))
            {
                if (int.TryParse(match.Groups[1].Value, out int sectorSize) && IsPlausibleCdSectorSize(sectorSize))
                {
                    sizes.Add(sectorSize);
                }
            }

            if (line.StartsWith("TRACK ", StringComparison.OrdinalIgnoreCase)
                && line.IndexOf("AUDIO", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                sizes.Add(2352);
            }
        }

        return sizes;
    }

    private static bool IsPlausibleCdSectorSize(int sectorSize) => sectorSize is >= 2048 and <= 2448;

    private static long GreatestCommonDivisor(long left, long right)
    {
        left = Math.Abs(left);
        right = Math.Abs(right);
        while (right != 0)
        {
            long temp = left % right;
            left = right;
            right = temp;
        }

        return left == 0 ? 1 : left;
    }

    private static long LeastCommonMultiple(long left, long right)
    {
        if (left <= 0 || right <= 0)
        {
            return 0;
        }

        return checked((left / GreatestCommonDivisor(left, right)) * right);
    }

    private static bool IsCreateCdHunkSizeMultipleError(ChdmanCliRunner.Result run, out int rejectedHunkSize, out int requiredSectorSize)
    {
        string text = string.Concat(run.StandardOutput, Environment.NewLine, run.StandardError);
        return TryParseHunkSizeMultipleError(text, out rejectedHunkSize, out requiredSectorSize);
    }

    private static bool TryParseHunkSizeMultipleError(string text, out int rejectedHunkSize, out int requiredSectorSize)
    {
        rejectedHunkSize = 0;
        requiredSectorSize = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        Match match = Regex.Match(
            text,
            @"Hunk size\s+(?<hunk>\d+)\s+bytes\s+is\s+not\s+a\s+whole\s+multiple\s+of\s+(?<sector>\d+)",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return false;
        }

        return int.TryParse(match.Groups["hunk"].Value, out rejectedHunkSize)
               && int.TryParse(match.Groups["sector"].Value, out requiredSectorSize)
               && rejectedHunkSize > 0
               && requiredSectorSize > 0;
    }

    private static void ReplaceOrAddHunkSizeArgument(List<string> arguments, int hunkSizeBytes)
    {
        for (int i = 0; i < arguments.Count; i++)
        {
            if (!string.Equals(arguments[i], "-hs", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(arguments[i], "--hunksize", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 < arguments.Count)
            {
                arguments[i + 1] = hunkSizeBytes.ToString();
                return;
            }
        }

        arguments.Add("-hs");
        arguments.Add(hunkSizeBytes.ToString());
    }

    private static string BuildLogsDirectory()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HakamiqChdTool",
            "Logs");

        Directory.CreateDirectory(path);
        return path;
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(value) ? "file" : value;
    }

    private static string NormalizePathForCli(string path) => FilePathExclusiveGate.NormalizePathForExclusiveLock(path);
}
