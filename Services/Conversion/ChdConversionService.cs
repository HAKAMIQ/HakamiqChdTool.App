using HakamiqChdTool.App.Core.Contracts;
using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static HakamiqChdTool.App.Services.ChdConversionMessages;
namespace HakamiqChdTool.App.Services;
public sealed class ChdConversionService : IChdConversionService
{
    // Compression truth log markers: RequestedPreset: ResolvedCompression: SameAsMameDefault: CHD compression preset resolved
    // Centralized CHD policy gate markers: ChdOperationPolicyRequest PlatformAwareChdProfileRequest policyDecision.IsAllowed
    private readonly IChdCommandPreparationService _commandPreparation;
    private readonly IChdProcessExecutionService _processExecution;
    private readonly IChdResultMappingService _resultMapping;
    private readonly IChdVerificationBridge _verificationBridge;
    private readonly IChdOperationPolicyGate _operationPolicyGate;
    private readonly IChdmanCapabilityService _capabilityService;
    private readonly IPlatformAwareChdProfilePolicy _profilePolicy;
    public ChdConversionService()
        : this(
            new ChdCommandPreparationService(),
            new ChdProcessExecutionService(),
            new ChdResultMappingService(),
            new ChdVerificationBridge())
    {
    }
    public ChdConversionService(
        IChdCommandPreparationService commandPreparation,
        IChdProcessExecutionService processExecution,
        IChdResultMappingService resultMapping,
        IChdVerificationBridge verificationBridge)
        : this(
            commandPreparation,
            processExecution,
            resultMapping,
            verificationBridge,
            new ChdmanCapabilityService(),
            null,
            new PlatformAwareChdProfilePolicy())
    {
    }
    public ChdConversionService(
        IChdCommandPreparationService commandPreparation,
        IChdProcessExecutionService processExecution,
        IChdResultMappingService resultMapping,
        IChdVerificationBridge verificationBridge,
        IChdOperationPolicyGate operationPolicyGate)
        : this(
            commandPreparation,
            processExecution,
            resultMapping,
            verificationBridge,
            new ChdmanCapabilityService(),
            operationPolicyGate,
            new PlatformAwareChdProfilePolicy())
    {
    }
    public ChdConversionService(
        IChdCommandPreparationService commandPreparation,
        IChdProcessExecutionService processExecution,
        IChdResultMappingService resultMapping,
        IChdVerificationBridge verificationBridge,
        IChdmanCapabilityService capabilityService,
        IChdOperationPolicyGate? operationPolicyGate,
        IPlatformAwareChdProfilePolicy profilePolicy)
    {
        _commandPreparation = commandPreparation ?? throw new ArgumentNullException(nameof(commandPreparation));
        _processExecution = processExecution ?? throw new ArgumentNullException(nameof(processExecution));
        _resultMapping = resultMapping ?? throw new ArgumentNullException(nameof(resultMapping));
        _verificationBridge = verificationBridge ?? throw new ArgumentNullException(nameof(verificationBridge));
        _capabilityService = capabilityService ?? throw new ArgumentNullException(nameof(capabilityService));
        _operationPolicyGate = operationPolicyGate ?? new ChdOperationPolicyGate(_capabilityService);
        _profilePolicy = profilePolicy ?? throw new ArgumentNullException(nameof(profilePolicy));
    }
    public string BuildCommand(
        string inputPath,
        ChdmanExtractionKind extractionKind = ChdmanExtractionKind.None,
        IsoCreateCommandOverride isoCreateCommandOverride = IsoCreateCommandOverride.Auto)
    {
        return _commandPreparation.BuildCommand(inputPath, extractionKind, isoCreateCommandOverride);
    }
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
        bool enableDiskSpaceGuard = true,
        ConversionPerformanceMode performanceMode = ConversionPerformanceMode.Safe,
        ChdmanProcessPriorityMode priorityMode = ChdmanProcessPriorityMode.Quiet,
        bool extractionMetadataDecisionConfirmed = false,
        string? extractCdCueOutputPath = null,
        string? extractCdBinOutputPath = null,
        bool verifyExtractCdCueBinContract = true)
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
        string resolvedInputPath = _commandPreparation.NormalizePathForCli(inputPath);
        string resolvedOutputPath = _commandPreparation.NormalizePathForCli(outputPath);
        string resolvedExtractCdCueOutputPath = NormalizeOptionalExtractCdOutputPath(extractCdCueOutputPath);
        string resolvedExtractCdBinOutputPath = NormalizeOptionalExtractCdOutputPath(extractCdBinOutputPath);
        if ((!string.IsNullOrWhiteSpace(resolvedExtractCdCueOutputPath)
                || !string.IsNullOrWhiteSpace(resolvedExtractCdBinOutputPath))
            && extractionKind != ChdmanExtractionKind.ExtractCd)
        {
            throw new InvalidOperationException(InvalidCueOutputPathMessageKey);
        }

        string inputExtension = Path.GetExtension(resolvedInputPath).ToLowerInvariant();
        if (string.Equals(inputExtension, ".chd", StringComparison.OrdinalIgnoreCase)
            && extractionKind == ChdmanExtractionKind.None)
        {
            Log.Warning(
                "Direct CHD to CHD recompression was blocked. Input={InputPath}; Output={OutputPath}; RequiredPipeline=extract-original-like-then-rebuild",
                resolvedInputPath,
                resolvedOutputPath);

            return ChdConversionServiceSupport.BuildPreExecutionFailureResult(
                inputPath,
                outputPath,
                ChdConversionMessages.DirectChdRecompressBlockedMessageKey);
        }

        (string command, IsoChdmanCreateDiagnostics? isoDiagnostics) =
            _commandPreparation.ResolveTwoWayCommandWithOptionalIsoDiagnostics(
                inputExtension,
                extractionKind,
                resolvedInputPath,
                isoCreateCommandOverride);
        string logsDirectory = _commandPreparation.BuildLogsDirectory();
        string logPath = Path.Combine(
            logsDirectory,
            $"convert_{DateTime.Now:yyyyMMdd_HHmmss}_{_commandPreparation.SanitizeFileName(Path.GetFileNameWithoutExtension(resolvedInputPath))}.log");
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
        if (!_verificationBridge.TryValidateDescriptorDependenciesBeforeChdman(resolvedInputPath, command, out string descriptorFailureMessageKey))
        {
            Log.Warning(
                "CHD descriptor preflight rejected input before chdman. Input={InputPath}; Command={Command}; MessageKey={MessageKey}",
                resolvedInputPath,
                command,
                descriptorFailureMessageKey);
            return ChdConversionServiceSupport.BuildPreExecutionFailureResult(inputPath, outputPath, descriptorFailureMessageKey);
        }
        ChdConversionServiceSupport.ChdPreparedPolicyContext preparedPolicy = await ChdConversionServiceSupport
            .PreparePolicyContextAsync(
                chdmanPath,
                inputPath,
                outputPath,
                resolvedInputPath,
                resolvedOutputPath,
                command,
                extractionKind,
                isoDiagnostics,
                compressionCodecs,
                hunkSizeBytes,
                extractionMetadataDecisionConfirmed,
                _commandPreparation,
                _capabilityService,
                _profilePolicy,
                _operationPolicyGate,
                cancellationToken)
            .ConfigureAwait(false);
        if (preparedPolicy.FailureResult is not null)
        {
            return preparedPolicy.FailureResult;
        }

        command = preparedPolicy.Command;
        bool isExtractCommand = preparedPolicy.IsExtractCommand;
        ChdCompressionResolution compressionResolution = preparedPolicy.CompressionResolution;
        string resolvedCompression = preparedPolicy.ResolvedCompression;
        int resolvedHunkSizeBytes = preparedPolicy.ResolvedHunkSizeBytes;
        ChdConversionServiceSupport.ChdExecutionReportContext executionReportContext = preparedPolicy.ExecutionReportContext;

        string diskPreflightMessageKey;
        string diskPreflightOperationKey;
        DiskPreflightMode diskPreflightMode = isExtractCommand
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

        string extractCdCueOutputPathForArgument = extractionKind == ChdmanExtractionKind.ExtractCd
            ? (!string.IsNullOrWhiteSpace(resolvedExtractCdCueOutputPath) ? resolvedExtractCdCueOutputPath : resolvedOutputPath)
            : string.Empty;
        string extractCdBinOutputPathForArgument = extractionKind == ChdmanExtractionKind.ExtractCd
            ? (!string.IsNullOrWhiteSpace(resolvedExtractCdBinOutputPath)
                ? resolvedExtractCdBinOutputPath
                : _commandPreparation.BuildExtractCdBinOutputPath(extractCdCueOutputPathForArgument))
            : string.Empty;

        if (extractionKind == ChdmanExtractionKind.ExtractCd)
        {
            string? cueDirectory = Path.GetDirectoryName(extractCdCueOutputPathForArgument);
            string? binDirectory = Path.GetDirectoryName(extractCdBinOutputPathForArgument);
            if (string.IsNullOrWhiteSpace(cueDirectory) || string.IsNullOrWhiteSpace(binDirectory))
            {
                throw new InvalidOperationException(BinOutputDirectoryMissingMessageKey);
            }

            Directory.CreateDirectory(cueDirectory);
            Directory.CreateDirectory(binDirectory);
        }

        var arguments = new List<string>
        {
            command,
            "-i",
            resolvedInputPath,
            "-o",
            extractionKind == ChdmanExtractionKind.ExtractCd ? extractCdCueOutputPathForArgument : resolvedOutputPath
        };
        if (extractionKind == ChdmanExtractionKind.ExtractCd)
        {
            arguments.Add("-ob");
            arguments.Add(extractCdBinOutputPathForArgument);
        }
        if (allowOverwriteOutput && (isExtractCommand || _commandPreparation.IsCreateCommand(command)))
        {
            arguments.Add("-f");
        }
        int availableLogicalProcessors = ProcessorTopologyService.GetAvailableLogicalProcessorCount();
        int normalizedProcessorLimit = ProcessorTopologyService.ResolveChdmanProcessorCount(
            maxProcessorCount,
            enableAutoResourceLimiter,
            reservedLogicalCores,
            performanceMode);
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
        string monitoredOutputPath = extractionKind == ChdmanExtractionKind.ExtractCd
            ? extractCdBinOutputPathForArgument
            : resolvedOutputPath;
        string displayCommandLine = _processExecution.FormatCommandLineForDisplay(chdmanPath, arguments);
        Log.Information(
            "CHD conversion starting. Input={InputPath}, Output={OutputPath}, Command={Command}, RequestedProcessors={RequestedProcessors}, PassedProcessors={PassedProcessors}, AutoLimiter={AutoLimiter}, ReservedLogicalCores={ReservedLogicalCores}, AvailableLogicalProcessors={AvailableLogicalProcessors}, PerformanceMode={PerformanceMode}, PriorityMode={PriorityMode}",
            resolvedInputPath,
            resolvedOutputPath,
            command,
            maxProcessorCount,
            passedProcessorLimit,
            enableAutoResourceLimiter,
            reservedLogicalCores,
            availableLogicalProcessors,
            performanceMode,
            priorityMode);
        Log.Information("CHDMAN CMD: {Args}", displayCommandLine);
        ChdmanCliRunner.Result run;
        string? resultMessageKeyOverride = null;
        var chdmanStopwatch = Stopwatch.StartNew();
        try
        {
            run = await _processExecution.ExecuteAsync(
                executablePath: chdmanPath,
                arguments: arguments,
                parseProgressPercent: progress is not null && ChdProgressPolicy.ShouldParseRawPercent(arguments),
                progress: progress,
                onProcessStarted: onProcessStarted,
                cancellationToken: cancellationToken,
                exclusiveFileAccessPath: resolvedInputPath,
                monitoredOutputPath: monitoredOutputPath,
                performanceProgress: performanceProgress,
                priorityMode: priorityMode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            chdmanStopwatch.Stop();
            _resultMapping.TryDeleteIncompleteOutputs(resolvedOutputPath, isExtractCommand, "operation cancelled before result was returned");
            ChdConversionServiceSupport.TryDeleteAuxiliaryOutputFile(resolvedExtractCdCueOutputPath, "cleanup companion cue");
            Log.Debug("CHD conversion cancelled. Input: {InputPath}", inputPath);
            return ChdConversionServiceSupport.BuildCancelledConversionResult(
                inputPath,
                outputPath,
                displayCommandLine,
                string.Empty,
                string.Empty,
                logPath,
                chdmanStopwatch.Elapsed,
                ChdmanProcessRunner.CanceledExitCode,
                passedProcessorLimit,
                compressionResolution,
                resolvedHunkSizeBytes,
                executionReportContext);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CHD conversion threw. Input: {InputPath}", inputPath);
            throw;
        }
        if (!run.WasCancelled
            && run.ExitCode != 0
            && _processExecution.IsCreateCdHunkSizeMultipleError(run, out int incompatibleHunkSize, out int requiredSectorSize))
        {
            const string hunkFailureTechnicalMessage = "CreateCD hunk size is not aligned with the media sector size.";
            if (_commandPreparation.TryBuildCreateCdHunkRetrySize(hunkSizeBytes, requiredSectorSize, resolvedHunkSizeBytes, out int retryHunkSizeBytes))
            {
                _resultMapping.TryDeleteIncompleteOutputs(resolvedOutputPath, isExtractCommand, "createcd hunk-size retry");
                ChdConversionServiceSupport.TryDeleteAuxiliaryOutputFile(resolvedExtractCdCueOutputPath, "cleanup companion cue");
                _commandPreparation.ReplaceOrAddHunkSizeArgument(arguments, retryHunkSizeBytes);
                resolvedHunkSizeBytes = retryHunkSizeBytes;
                displayCommandLine = _processExecution.FormatCommandLineForDisplay(chdmanPath, arguments);
                Log.Warning(
                    "Retrying createcd with media-aligned hunk size after chdman rejected HunkSize={RejectedHunkSize} for SectorSize={SectorSize}. RetryHunkSize={RetryHunkSize}. Input={InputPath}",
                    incompatibleHunkSize,
                    requiredSectorSize,
                    retryHunkSizeBytes,
                    resolvedInputPath);
                try
                {
                    run = await _processExecution.ExecuteAsync(
                        executablePath: chdmanPath,
                        arguments: arguments,
                        parseProgressPercent: progress is not null && ChdProgressPolicy.ShouldParseRawPercent(arguments),
                        progress: progress,
                        onProcessStarted: onProcessStarted,
                        cancellationToken: cancellationToken,
                        exclusiveFileAccessPath: resolvedInputPath,
                        monitoredOutputPath: monitoredOutputPath,
                        performanceProgress: performanceProgress,
                        priorityMode: priorityMode).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    chdmanStopwatch.Stop();
                    _resultMapping.TryDeleteIncompleteOutputs(resolvedOutputPath, isExtractCommand, "createcd hunk-size retry cancelled");
                    ChdConversionServiceSupport.TryDeleteAuxiliaryOutputFile(resolvedExtractCdCueOutputPath, "cleanup companion cue");
                    Log.Debug("CHD conversion cancelled during createcd hunk-size retry. Input: {InputPath}", inputPath);
                    return ChdConversionServiceSupport.BuildCancelledConversionResult(
                        inputPath,
                        outputPath,
                        displayCommandLine,
                        run.StandardOutput,
                        run.StandardError,
                        logPath,
                        chdmanStopwatch.Elapsed,
                        ChdmanProcessRunner.CanceledExitCode,
                        passedProcessorLimit,
                        compressionResolution,
                        resolvedHunkSizeBytes,
                        executionReportContext);
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
            chdmanStopwatch.Stop();
            _resultMapping.TryDeleteIncompleteOutputs(resolvedOutputPath, isExtractCommand, "cancelled");
            ChdConversionServiceSupport.TryDeleteAuxiliaryOutputFile(resolvedExtractCdCueOutputPath, "cleanup companion cue");
            Log.Debug("CHD conversion cancelled. Input: {InputPath}", inputPath);
            return ChdConversionServiceSupport.BuildCancelledConversionResult(
                inputPath,
                outputPath,
                displayCommandLine,
                run.StandardOutput,
                run.StandardError,
                logPath,
                chdmanStopwatch.Elapsed,
                run.ExitCode,
                passedProcessorLimit,
                compressionResolution,
                resolvedHunkSizeBytes,
                executionReportContext);
        }
        if (!run.WasCancelled
            && run.ExitCode != 0
            && extractionKind == ChdmanExtractionKind.ExtractCd
            && verifyExtractCdCueBinContract
            && _commandPreparation.IsExtractCdSplitbinPatternRequired(run))
        {
            _resultMapping.TryDeleteIncompleteOutputs(resolvedOutputPath, isExtractCommand, "extractcd splitbin retry");
            ChdConversionServiceSupport.TryDeleteAuxiliaryOutputFile(resolvedExtractCdCueOutputPath, "cleanup companion cue");
            _commandPreparation.ReplaceExtractCdBinOutputArgument(arguments, _commandPreparation.BuildSplitBinExtractCdBinOutputPath(extractCdCueOutputPathForArgument));
            displayCommandLine = _processExecution.FormatCommandLineForDisplay(chdmanPath, arguments);
            Log.Warning(
                "Retrying extractcd with track-number output pattern after chdman required splitbin naming. Input={InputPath}; Output={OutputPath}",
                resolvedInputPath,
                resolvedOutputPath);
            Log.Information("CHDMAN CMD: {Args}", displayCommandLine);
            try
            {
                run = await _processExecution.ExecuteAsync(
                    executablePath: chdmanPath,
                    arguments: arguments,
                    parseProgressPercent: progress is not null && ChdProgressPolicy.ShouldParseRawPercent(arguments),
                    progress: progress,
                    onProcessStarted: onProcessStarted,
                    cancellationToken: cancellationToken,
                    exclusiveFileAccessPath: resolvedInputPath,
                    monitoredOutputPath: monitoredOutputPath,
                    performanceProgress: performanceProgress,
                    priorityMode: priorityMode).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                chdmanStopwatch.Stop();
                _resultMapping.TryDeleteIncompleteOutputs(resolvedOutputPath, isExtractCommand, "extractcd splitbin retry cancelled");
                ChdConversionServiceSupport.TryDeleteAuxiliaryOutputFile(resolvedExtractCdCueOutputPath, "cleanup companion cue");
                Log.Debug("CHD conversion cancelled during extractcd splitbin retry. Input: {InputPath}", inputPath);
                return ChdConversionServiceSupport.BuildCancelledConversionResult(
                    inputPath,
                    outputPath,
                    displayCommandLine,
                    run.StandardOutput,
                    run.StandardError,
                    logPath,
                    chdmanStopwatch.Elapsed,
                    ChdmanProcessRunner.CanceledExitCode,
                    passedProcessorLimit,
                    compressionResolution,
                    resolvedHunkSizeBytes,
                    executionReportContext);
            }
        }
        if (run.WasCancelled)
        {
            chdmanStopwatch.Stop();
            _resultMapping.TryDeleteIncompleteOutputs(resolvedOutputPath, isExtractCommand, "cancelled");
            ChdConversionServiceSupport.TryDeleteAuxiliaryOutputFile(resolvedExtractCdCueOutputPath, "cleanup companion cue");
            Log.Debug("CHD conversion cancelled. Input: {InputPath}", inputPath);
            return ChdConversionServiceSupport.BuildCancelledConversionResult(
                inputPath,
                outputPath,
                displayCommandLine,
                run.StandardOutput,
                run.StandardError,
                logPath,
                chdmanStopwatch.Elapsed,
                run.ExitCode,
                passedProcessorLimit,
                compressionResolution,
                resolvedHunkSizeBytes,
                executionReportContext);
        }
        chdmanStopwatch.Stop();
        string output = run.StandardOutput;
        string error = run.StandardError;
        long logicalInputBytes = ConversionMetricsResolver.TryParseLogicalSizeBytes(output, out long parsedLogicalInputBytes)
            ? parsedLogicalInputBytes
            : 0L;
        bool extractCdCueContractValid = true;
        if (run.ExitCode == 0
            && extractionKind == ChdmanExtractionKind.ExtractCd
            && verifyExtractCdCueBinContract)
        {
            extractCdCueContractValid = _verificationBridge.TryNormalizeExtractedCueBinOutput(extractCdCueOutputPathForArgument);
            if (!extractCdCueContractValid)
            {
                resultMessageKeyOverride = InvalidCueBinDependencyMessageKey;
                error = string.Empty;
                Log.Warning(
                    "extractcd output failed CUE/BIN contract validation after chdman succeeded. Cue={CuePath}",
                    extractCdCueOutputPathForArgument);
            }
        }
        bool success = run.ExitCode == 0 && extractCdCueContractValid && _resultMapping.VerifyOutputExists(resolvedOutputPath, isExtractCommand);
        if (success)
        {
            ChdConversionServiceSupport.TryDeleteAuxiliaryOutputFile(resolvedExtractCdCueOutputPath, "extractcd auxiliary cue after success");
            progress?.Report(100);
            Log.Information("chdman finished successfully. Command: {Command}, Input: {InputPath}", command, inputPath);
        }
        else
        {
            _resultMapping.TryDeleteIncompleteOutputs(resolvedOutputPath, isExtractCommand, "failed");
            ChdConversionServiceSupport.TryDeleteAuxiliaryOutputFile(resolvedExtractCdCueOutputPath, "failed");
            Log.Error(
                "chdman failed. Command: {Command}, Input: {InputPath}, ExitCode: {ExitCode}, StdErr: {StdErr}",
                command,
                inputPath,
                run.ExitCode,
                error);
        }
        await ChdConversionServiceSupport.WriteConversionLogAsync(
            logPath,
            command,
            inputPath,
            outputPath,
            run.ExitCode,
            output,
            error,
            success,
            compressionResolution,
            resolvedHunkSizeBytes,
            availableLogicalProcessors,
            maxProcessorCount,
            enableAutoResourceLimiter,
            reservedLogicalCores,
            passedProcessorLimit,
            performanceMode,
            priorityMode,
            chdmanStopwatch.Elapsed,
            logicalInputBytes,
            diskPreflightMessageKey,
            diskPreflightOperationKey,
            inputSha1,
            executionReportContext);
        return ChdConversionServiceSupport.BuildCompletedConversionResult(
            inputPath,
            outputPath,
            displayCommandLine,
            output,
            error,
            logPath,
            chdmanStopwatch.Elapsed,
            run,
            success,
            isExtractCommand,
            resultMessageKeyOverride,
            passedProcessorLimit,
            compressionResolution,
            resolvedHunkSizeBytes,
            logicalInputBytes,
            executionReportContext);
    }

    private string NormalizeOptionalExtractCdOutputPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string normalized = _commandPreparation.NormalizePathForCli(path);
        ConversionPathValidator.ThrowIfUnsafeForChdman(normalized, nameof(path));
        return normalized;
    }
}
