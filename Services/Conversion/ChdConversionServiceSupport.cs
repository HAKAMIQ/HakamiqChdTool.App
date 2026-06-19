using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Core.Chd.Commands;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static HakamiqChdTool.App.Services.ChdConversionMessages;

namespace HakamiqChdTool.App.Services;

internal static class ChdConversionServiceSupport
{
    internal sealed record ChdExecutionReportContext(
        string RequestedProfile,
        string ResolvedCommand,
        string ResolvedCompression,
        int ResolvedHunkSize,
        string EffectiveCompression,
        int EffectiveHunkSize,
        bool SameAsMameDefault,
        string CompatibilityNotes,
        string ChdmanVersion);

    internal sealed record ChdPreparedPolicyContext(
        string Command,
        bool IsExtractCommand,
        ChdCompressionResolution CompressionResolution,
        string ResolvedCompression,
        int ResolvedHunkSizeBytes,
        ChdExecutionReportContext ExecutionReportContext,
        ChdConversionResult? FailureResult);

    internal sealed record ChdInputPreparationReport(
        string OriginalInputPath,
        string PreparedInputPath,
        string PreparationTool,
        string PreparationToolVersion,
        string PreparationCommand,
        int PreparationExitCode,
        long? PreparedOutputBytes,
        bool TemporaryIsoDeleted,
        bool SourcePreserved);

    internal static async Task<ChdPreparedPolicyContext> PreparePolicyContextAsync(
        string chdmanPath,
        string inputPath,
        string outputPath,
        string resolvedInputPath,
        string resolvedOutputPath,
        string command,
        ChdmanExtractionKind extractionKind,
        IsoChdmanCreateDiagnostics? isoDiagnostics,
        string? compressionCodecs,
        int hunkSizeBytes,
        bool extractionMetadataDecisionConfirmed,
        IChdCommandPreparationService commandPreparation,
        IChdmanCapabilityService capabilityService,
        IPlatformAwareChdProfilePolicy profilePolicy,
        IChdOperationPolicyGate operationPolicyGate,
        CancellationToken cancellationToken)
    {
        bool isExtractCommand = commandPreparation.IsExtractCommand(command);
        ChdmanCapabilitySnapshot chdmanCapabilities = await capabilityService.InspectAsync(chdmanPath, cancellationToken).ConfigureAwait(false);
        ChdProfileMediaKind profileMediaKind = ResolveProfileMediaKind(resolvedInputPath, command, extractionKind, isoDiagnostics);
        ChdMediaFormatKind profileInputFormat = ResolveProfileInputFormat(resolvedInputPath, command, extractionKind);
        string profilePlatform = ResolveProfilePlatform(resolvedInputPath, isoDiagnostics);

        PlatformAwareChdProfileDecision profileDecision = profilePolicy.Resolve(new PlatformAwareChdProfileRequest(
            profileMediaKind,
            profilePlatform,
            profileInputFormat,
            TargetEmulatorProfile.Auto,
            ChdProfileUserGoal.Auto,
            chdmanCapabilities,
            command,
            resolvedInputPath,
            compressionCodecs,
            hunkSizeBytes));

        if (!profileDecision.HasCommand)
        {
            Log.Warning(
                "CHD profile policy blocked operation before command preparation. Input={InputPath}; Output={OutputPath}; InputFormat={InputFormat}; MediaKind={MediaKind}; Platform={Platform}; ReasonCode={ReasonCode}; WarningCode={WarningCode}",
                resolvedInputPath,
                resolvedOutputPath,
                profileInputFormat,
                profileMediaKind,
                profilePlatform,
                profileDecision.ReasonCode,
                profileDecision.CompatibilityWarningCode);

            ChdExecutionReportContext blockedReportContext = BuildExecutionReportContext(
                command,
                profileDecision,
                ChdCompressionResolution.NotApplicable,
                0,
                chdmanCapabilities);

            ChdConversionResult failure = BuildPreExecutionFailureResult(
                inputPath,
                outputPath,
                string.IsNullOrWhiteSpace(profileDecision.CompatibilityWarningCode)
                    ? PlatformAwareChdProfilePolicy.UnknownIsoMediaKindRequiredMessageKey
                    : profileDecision.CompatibilityWarningCode,
                reportContext: blockedReportContext);

            return new ChdPreparedPolicyContext(
                command,
                isExtractCommand,
                ChdCompressionResolution.NotApplicable,
                string.Empty,
                0,
                blockedReportContext,
                failure);
        }

        LogSelectedProfile(command, resolvedInputPath, profileMediaKind, profileInputFormat, profilePlatform, profileDecision);

        command = profileDecision.Command;
        isExtractCommand = commandPreparation.IsExtractCommand(command);

        ChdCompressionResolution compressionResolution = ChdCompressionResolution.NotApplicable;
        string resolvedCompression = string.Empty;
        int resolvedHunkSizeBytes = 0;
        string hunkPolicyNote = string.Empty;

        if (!isExtractCommand)
        {
            compressionResolution = commandPreparation.ResolveCompressionSettingWithTruth(profileDecision.Compression, command);
            resolvedCompression = compressionResolution.ResolvedCompression;
            resolvedHunkSizeBytes = commandPreparation.ResolveHunkSizeSetting(profileDecision.HunkSize, command, resolvedInputPath, out hunkPolicyNote);
        }

        LogCompressionTruth(command, resolvedInputPath, compressionResolution, profileDecision.CompressionPolicyName);
        LogHunkPolicy(command, resolvedInputPath, hunkSizeBytes, profileDecision.HunkSize, resolvedHunkSizeBytes, hunkPolicyNote, profileDecision.HunkPolicyName);

        ChdExecutionReportContext executionReportContext = BuildExecutionReportContext(
            command,
            profileDecision,
            compressionResolution,
            resolvedHunkSizeBytes,
            chdmanCapabilities);

        ChdOperationPolicyDecision policyDecision = await operationPolicyGate.EvaluateAsync(new ChdOperationPolicyRequest(
            chdmanPath,
            resolvedInputPath,
            command,
            extractionKind,
            isoDiagnostics,
            resolvedCompression,
            resolvedHunkSizeBytes,
            extractionMetadataDecisionConfirmed,
            chdmanCapabilities,
            profileMediaKind,
            profileInputFormat,
            profileDecision), cancellationToken).ConfigureAwait(false);

        if (policyDecision.IsAllowed)
        {
            return new ChdPreparedPolicyContext(
                command,
                isExtractCommand,
                compressionResolution,
                resolvedCompression,
                resolvedHunkSizeBytes,
                executionReportContext,
                null);
        }

        Log.Warning(
            "CHD operation blocked by centralized policy gate before chdman execution. Input={InputPath}; Output={OutputPath}; Command={Command}; MessageKey={MessageKey}",
            resolvedInputPath,
            resolvedOutputPath,
            command,
            policyDecision.MessageKey);

        ChdConversionResult policyFailure = BuildPreExecutionFailureResult(
            inputPath,
            outputPath,
            policyDecision.MessageKey,
            compressionResolution,
            resolvedHunkSizeBytes,
            executionReportContext);

        return new ChdPreparedPolicyContext(
            command,
            isExtractCommand,
            compressionResolution,
            resolvedCompression,
            resolvedHunkSizeBytes,
            executionReportContext,
            policyFailure);
    }

    private static void LogSelectedProfile(
        string previousCommand,
        string resolvedInputPath,
        ChdProfileMediaKind profileMediaKind,
        ChdMediaFormatKind profileInputFormat,
        string profilePlatform,
        PlatformAwareChdProfileDecision profileDecision)
    {
        bool changed = !string.Equals(previousCommand, profileDecision.Command, StringComparison.OrdinalIgnoreCase);
        string template = changed
            ? "CHD profile policy changed command. Input={InputPath}; PreviousCommand={PreviousCommand}; Command={Command}; InputFormat={InputFormat}; MediaKind={MediaKind}; Platform={Platform}; EffectiveProfile={EffectiveProfile}; ReasonCode={ReasonCode}; CompressionPolicy={CompressionPolicy}; HunkPolicy={HunkPolicy}"
            : "CHD profile policy selected profile. Input={InputPath}; PreviousCommand={PreviousCommand}; Command={Command}; InputFormat={InputFormat}; MediaKind={MediaKind}; Platform={Platform}; EffectiveProfile={EffectiveProfile}; ReasonCode={ReasonCode}; CompressionPolicy={CompressionPolicy}; HunkPolicy={HunkPolicy}";

        if (changed)
        {
            Log.Information(
                template,
                resolvedInputPath,
                previousCommand,
                profileDecision.Command,
                profileInputFormat,
                profileMediaKind,
                profilePlatform,
                profileDecision.EffectiveProfileName,
                profileDecision.ReasonCode,
                profileDecision.CompressionPolicyName,
                profileDecision.HunkPolicyName);
            return;
        }

        Log.Debug(
            template,
            resolvedInputPath,
            previousCommand,
            profileDecision.Command,
            profileInputFormat,
            profileMediaKind,
            profilePlatform,
            profileDecision.EffectiveProfileName,
            profileDecision.ReasonCode,
            profileDecision.CompressionPolicyName,
            profileDecision.HunkPolicyName);
    }

    private static void LogCompressionTruth(
        string command,
        string resolvedInputPath,
        ChdCompressionResolution compressionResolution,
        string compressionPolicyName)
    {
        if (string.IsNullOrWhiteSpace(compressionResolution.RequestedPreset))
        {
            return;
        }

        Log.Information(
            "CHD compression preset resolved. Command={Command}, Input={InputPath}, RequestedPreset={RequestedPreset}, ResolvedCompression={ResolvedCompression}, EffectiveCompression={EffectiveCompression}, SameAsMameDefault={SameAsMameDefault}, NoteKey={NoteKey}, CompressionPolicy={CompressionPolicy}",
            command,
            resolvedInputPath,
            compressionResolution.RequestedPreset,
            compressionResolution.LogResolvedCompression,
            compressionResolution.EffectiveCompression,
            compressionResolution.SameAsMameDefault,
            compressionResolution.TruthNoteKey ?? string.Empty,
            compressionPolicyName);
    }

    private static void LogHunkPolicy(
        string command,
        string resolvedInputPath,
        int requestedHunkSizeBytes,
        int policyHunkSizeBytes,
        int resolvedHunkSizeBytes,
        string hunkPolicyNote,
        string hunkPolicyName)
    {
        if (string.IsNullOrWhiteSpace(hunkPolicyNote))
        {
            return;
        }

        Log.Information(
            "CHD hunk-size policy. Command={Command}, Input={InputPath}, Requested={RequestedHunkSetting}, PolicyInput={PolicyHunkSetting}, Resolved={ResolvedHunkSize}, Note={Note}, HunkPolicy={HunkPolicy}",
            command,
            resolvedInputPath,
            requestedHunkSizeBytes,
            policyHunkSizeBytes,
            resolvedHunkSizeBytes,
            hunkPolicyNote,
            hunkPolicyName);
    }

    internal static ChdExecutionReportContext BuildExecutionReportContext(
        string command,
        PlatformAwareChdProfileDecision? profileDecision,
        ChdCompressionResolution compressionResolution,
        int resolvedHunkSizeBytes,
        ChdmanCapabilitySnapshot chdmanCapabilities)
    {
        string compatibilityNotes = profileDecision is null
            ? string.Empty
            : string.Join(
                ";",
                new[]
                {
                    profileDecision.CompatibilityWarningCode,
                    profileDecision.ReasonCode,
                    profileDecision.CompressionPolicyName,
                    profileDecision.HunkPolicyName
                }.Where(static value => !string.IsNullOrWhiteSpace(value)));

        return new ChdExecutionReportContext(
            profileDecision?.EffectiveProfileName ?? string.Empty,
            command?.Trim() ?? string.Empty,
            string.IsNullOrWhiteSpace(compressionResolution.LogResolvedCompression) ? string.Empty : compressionResolution.LogResolvedCompression,
            resolvedHunkSizeBytes,
            string.IsNullOrWhiteSpace(compressionResolution.EffectiveCompression) ? string.Empty : compressionResolution.EffectiveCompression,
            resolvedHunkSizeBytes,
            compressionResolution.SameAsMameDefault,
            compatibilityNotes,
            chdmanCapabilities.Version ?? string.Empty);
    }

    internal static ChdProfileMediaKind ResolveProfileMediaKind(
        string inputPath,
        string command,
        ChdmanExtractionKind extractionKind,
        IsoChdmanCreateDiagnostics? isoDiagnostics)
    {
        if (extractionKind == ChdmanExtractionKind.ExtractCd)
        {
            return ChdProfileMediaKind.CdRom;
        }

        if (extractionKind == ChdmanExtractionKind.ExtractDvd)
        {
            return ChdProfileMediaKind.DvdRom;
        }

        if (extractionKind == ChdmanExtractionKind.ExtractHd)
        {
            return ChdProfileMediaKind.HardDisk;
        }

        string extension = Path.GetExtension(inputPath).ToLowerInvariant();
        if (extension == ".cso")
        {
            return ChdProfileMediaKind.DvdRom;
        }

        if (extension is ".cue" or ".gdi" or ".toc" or ".nrg" or ".bin")
        {
            return ChdProfileMediaKind.CdRom;
        }

        if (extension == ".iso" && isoDiagnostics.HasValue)
        {
            IsoChdmanCreateDiagnostics diagnostics = isoDiagnostics.Value;
            if (string.Equals(diagnostics.AutoSuggestedCommand, "createcd", StringComparison.OrdinalIgnoreCase))
            {
                return ChdProfileMediaKind.CdRom;
            }

            if (string.Equals(diagnostics.AutoSuggestedCommand, "createdvd", StringComparison.OrdinalIgnoreCase))
            {
                return ChdProfileMediaKind.DvdRom;
            }
        }

        if (string.Equals(command, "createcd", StringComparison.OrdinalIgnoreCase))
        {
            return ChdProfileMediaKind.CdRom;
        }

        if (string.Equals(command, "createdvd", StringComparison.OrdinalIgnoreCase))
        {
            return ChdProfileMediaKind.DvdRom;
        }

        if (string.Equals(command, "createhd", StringComparison.OrdinalIgnoreCase))
        {
            return ChdProfileMediaKind.HardDisk;
        }

        return ChdProfileMediaKind.Unknown;
    }

    internal static ChdMediaFormatKind ResolveProfileInputFormat(
        string inputPath,
        string command,
        ChdmanExtractionKind extractionKind)
    {
        if (extractionKind == ChdmanExtractionKind.ExtractCd)
        {
            return ChdMediaFormatKind.CdChd;
        }

        if (extractionKind == ChdmanExtractionKind.ExtractDvd)
        {
            return ChdMediaFormatKind.DvdChd;
        }

        if (extractionKind == ChdmanExtractionKind.ExtractHd)
        {
            return ChdMediaFormatKind.HdChd;
        }

        if (extractionKind == ChdmanExtractionKind.ExtractRaw)
        {
            return ChdMediaFormatKind.RawChd;
        }

        return Path.GetExtension(inputPath).ToLowerInvariant() switch
        {
            ".iso" => ChdMediaFormatKind.Iso,
            ".cue" => ChdMediaFormatKind.Cue,
            ".gdi" => ChdMediaFormatKind.Gdi,
            ".toc" => ChdMediaFormatKind.Toc,
            ".nrg" => ChdMediaFormatKind.Nrg,
            ".cso" => ChdMediaFormatKind.Cso,
            ".bin" when string.Equals(command, "createcd", StringComparison.OrdinalIgnoreCase) => ChdMediaFormatKind.Cue,
            ".chd" => ChdMediaFormatKind.Chd,
            _ => ChdMediaFormatKind.Unknown
        };
    }

    internal static string ResolveProfilePlatform(string inputPath, IsoChdmanCreateDiagnostics? isoDiagnostics)
    {
        if (isoDiagnostics.HasValue && !string.IsNullOrWhiteSpace(isoDiagnostics.Value.PlatformName))
        {
            return isoDiagnostics.Value.PlatformName;
        }

        try
        {
            PlatformDetectionResult detection = PlatformDetectionService.Detect(inputPath);
            return detection.PlatformName;
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException
                                  or InvalidDataException
                                  or OperationCanceledException
                                  or OverflowException)
        {
            return string.Empty;
        }
    }

    internal static void TryDeleteAuxiliaryOutputFile(string? path, string reason)
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

            File.Delete(fullPath);
            Log.Information("Deleted auxiliary chdman output. Path={Path}, Reason={Reason}", fullPath, reason);
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException
                                  or System.Security.SecurityException)
        {
            Log.Debug(ex, "Could not delete auxiliary chdman output. Path={Path}, Reason={Reason}", path, reason);
        }
    }

    internal static string NormalizeOptionalExtractCdOutputPath(
        string? path,
        IChdCommandPreparationService commandPreparation)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string normalized = commandPreparation.NormalizePathForCli(path);
        ConversionPathValidator.ThrowIfUnsafeForChdman(normalized, nameof(path));
        return normalized;
    }

    internal static ChdConversionResult BuildPreExecutionFailureResult(
        string inputPath,
        string outputPath,
        string messageKey,
        ChdCompressionResolution? compressionResolution = null,
        int resolvedHunkSizeBytes = 0,
        ChdExecutionReportContext? reportContext = null)
    {
        ChdCompressionResolution compression = compressionResolution ?? ChdCompressionResolution.NotApplicable;
        return new ChdConversionResult
        {
            IsSuccess = false,
            WasCancelled = false,
            ExitCode = 1,
            Status = ChdConversionStatus.Failed,
            InputPath = inputPath,
            OutputPath = outputPath,
            CommandLine = string.Empty,
            Output = string.Empty,
            Error = string.Empty,
            Message = messageKey,
            LogPath = string.Empty,
            CompressionCodecs = compression.LogResolvedCompression,
            RequestedCompressionPreset = compression.RequestedPreset,
            ResolvedCompressionCodecs = compression.LogResolvedCompression,
            EffectiveCompressionCodecs = compression.EffectiveCompression,
            EffectiveCompressionSameAsMameDefault = compression.SameAsMameDefault,
            CompressionTruthNoteKey = compression.TruthNoteKey,
            HunkSizeBytes = resolvedHunkSizeBytes > 0 ? resolvedHunkSizeBytes : null,
            RequestedProfile = reportContext?.RequestedProfile ?? string.Empty,
            ResolvedCommand = reportContext?.ResolvedCommand ?? string.Empty,
            ResolvedCompression = reportContext?.ResolvedCompression ?? compression.LogResolvedCompression,
            ResolvedHunkSize = reportContext?.ResolvedHunkSize > 0 ? reportContext.ResolvedHunkSize : null,
            EffectiveCompression = reportContext?.EffectiveCompression ?? compression.EffectiveCompression,
            EffectiveHunkSize = reportContext?.EffectiveHunkSize > 0 ? reportContext.EffectiveHunkSize : null,
            SameAsMameDefault = reportContext?.SameAsMameDefault ?? compression.SameAsMameDefault,
            CompatibilityNotes = reportContext?.CompatibilityNotes ?? string.Empty,
            ChdmanVersion = reportContext?.ChdmanVersion ?? string.Empty
        };
    }

    internal static ChdConversionResult BuildPreparationCancelledResult(
        string inputPath,
        string outputPath,
        string messageKey,
        ChdCompressionResolution? compressionResolution = null,
        int resolvedHunkSizeBytes = 0,
        ChdExecutionReportContext? reportContext = null)
    {
        ChdCompressionResolution compression = compressionResolution ?? ChdCompressionResolution.NotApplicable;
        return new ChdConversionResult
        {
            IsSuccess = false,
            WasCancelled = true,
            ExitCode = ChdmanProcessRunner.CanceledExitCode,
            Status = ChdConversionStatus.UserCanceled,
            InputPath = inputPath,
            OutputPath = outputPath,
            CommandLine = string.Empty,
            Output = string.Empty,
            Error = string.Empty,
            Message = string.IsNullOrWhiteSpace(messageKey) ? UserCancelledMessageKey : messageKey,
            LogPath = string.Empty,
            CompressionCodecs = compression.LogResolvedCompression,
            RequestedCompressionPreset = compression.RequestedPreset,
            ResolvedCompressionCodecs = compression.LogResolvedCompression,
            EffectiveCompressionCodecs = compression.EffectiveCompression,
            EffectiveCompressionSameAsMameDefault = compression.SameAsMameDefault,
            CompressionTruthNoteKey = compression.TruthNoteKey,
            HunkSizeBytes = resolvedHunkSizeBytes > 0 ? resolvedHunkSizeBytes : null,
            RequestedProfile = reportContext?.RequestedProfile ?? string.Empty,
            ResolvedCommand = reportContext?.ResolvedCommand ?? string.Empty,
            ResolvedCompression = reportContext?.ResolvedCompression ?? compression.LogResolvedCompression,
            ResolvedHunkSize = reportContext?.ResolvedHunkSize > 0 ? reportContext.ResolvedHunkSize : null,
            EffectiveCompression = reportContext?.EffectiveCompression ?? compression.EffectiveCompression,
            EffectiveHunkSize = reportContext?.EffectiveHunkSize > 0 ? reportContext.EffectiveHunkSize : null,
            SameAsMameDefault = reportContext?.SameAsMameDefault ?? compression.SameAsMameDefault,
            CompatibilityNotes = reportContext?.CompatibilityNotes ?? string.Empty,
            ChdmanVersion = reportContext?.ChdmanVersion ?? string.Empty
        };
    }

    internal static ChdConversionResult BuildCompletedConversionResult(
        string inputPath,
        string outputPath,
        string displayCommandLine,
        string output,
        string error,
        string logPath,
        TimeSpan duration,
        ChdmanCliRunner.Result run,
        bool success,
        ChdConversionStatus status,
        bool isExtractCommand,
        string? resultMessageKeyOverride,
        int passedProcessorLimit,
        ChdCompressionResolution compressionResolution,
        int resolvedHunkSizeBytes,
        long logicalInputBytes,
        ChdExecutionReportContext? reportContext = null) => new()
        {
            IsSuccess = success,
            WasCancelled = run.WasCancelled,
            ExitCode = run.ExitCode,
            Status = status,
            InputPath = inputPath,
            OutputPath = outputPath,
            CommandLine = displayCommandLine,
            Output = output,
            Error = error,
            Message = success
            ? (isExtractCommand ? ExtractionSuccessMessageKey : ConversionSuccessMessageKey)
            : resultMessageKeyOverride ?? (isExtractCommand ? ExtractionFailedMessageKey : ConversionFailedMessageKey),
            LogPath = logPath,
            ChdmanDuration = duration,
            NumProcessors = passedProcessorLimit,
            CompressionCodecs = compressionResolution.LogResolvedCompression,
            RequestedCompressionPreset = compressionResolution.RequestedPreset,
            ResolvedCompressionCodecs = compressionResolution.LogResolvedCompression,
            EffectiveCompressionCodecs = compressionResolution.EffectiveCompression,
            EffectiveCompressionSameAsMameDefault = compressionResolution.SameAsMameDefault,
            CompressionTruthNoteKey = compressionResolution.TruthNoteKey,
            HunkSizeBytes = resolvedHunkSizeBytes > 0 ? resolvedHunkSizeBytes : null,
            LogicalInputBytes = logicalInputBytes,
            RequestedProfile = reportContext?.RequestedProfile ?? string.Empty,
            ResolvedCommand = reportContext?.ResolvedCommand ?? string.Empty,
            ResolvedCompression = reportContext?.ResolvedCompression ?? compressionResolution.LogResolvedCompression,
            ResolvedHunkSize = reportContext?.ResolvedHunkSize > 0 ? reportContext.ResolvedHunkSize : null,
            EffectiveCompression = reportContext?.EffectiveCompression ?? compressionResolution.EffectiveCompression,
            EffectiveHunkSize = reportContext?.EffectiveHunkSize > 0 ? reportContext.EffectiveHunkSize : null,
            SameAsMameDefault = reportContext?.SameAsMameDefault ?? compressionResolution.SameAsMameDefault,
            CompatibilityNotes = reportContext?.CompatibilityNotes ?? string.Empty,
            ChdmanVersion = reportContext?.ChdmanVersion ?? string.Empty
        };

    internal static async Task WriteConversionLogAsync(
        string logPath,
        string command,
        string inputPath,
        string outputPath,
        int exitCode,
        string output,
        string error,
        bool success,
        ChdCompressionResolution compressionResolution,
        int resolvedHunkSizeBytes,
        int availableLogicalProcessors,
        int maxProcessorCount,
        bool enableAutoResourceLimiter,
        int reservedLogicalCores,
        int passedProcessorLimit,
        ConversionPerformanceMode performanceMode,
        ChdmanProcessPriorityMode priorityMode,
        TimeSpan duration,
        long logicalInputBytes,
        string diskPreflightMessageKey,
        string diskPreflightOperationKey,
        FileHashResult? inputSha1,
        ChdInputPreparationReport? inputPreparationReport = null,
        ChdExecutionReportContext? reportContext = null)
    {
        var logBuilder = new StringBuilder();

        logBuilder.AppendLine($"Time: {DateTime.Now:yyyyMMdd_HHmmss}");
        logBuilder.AppendLine($"Command: {command}");
        logBuilder.AppendLine($"Input: {inputPath}");
        logBuilder.AppendLine($"Output: {outputPath}");
        if (inputPreparationReport is not null)
        {
            logBuilder.AppendLine($"OriginalInputPath: {inputPreparationReport.OriginalInputPath}");
            logBuilder.AppendLine($"PreparedInputPath: {inputPreparationReport.PreparedInputPath}");
            logBuilder.AppendLine($"PreparationTool: {inputPreparationReport.PreparationTool}");
            logBuilder.AppendLine($"PreparationToolVersion: {inputPreparationReport.PreparationToolVersion}");
            logBuilder.AppendLine($"PreparationCommand: {inputPreparationReport.PreparationCommand}");
            logBuilder.AppendLine($"PreparationExitCode: {inputPreparationReport.PreparationExitCode}");
            logBuilder.AppendLine($"PreparationOutputBytes: {inputPreparationReport.PreparedOutputBytes?.ToString() ?? string.Empty}");
            logBuilder.AppendLine($"TemporaryIsoDeleted: {inputPreparationReport.TemporaryIsoDeleted}");
            logBuilder.AppendLine($"SourcePreserved: {inputPreparationReport.SourcePreserved}");
            logBuilder.AppendLine($"FinalChdCommand: {command}");
        }

        logBuilder.AppendLine($"ExitCode: {exitCode}");
        logBuilder.AppendLine($"Compression: {compressionResolution.LogResolvedCompression}");
        logBuilder.AppendLine($"RequestedPreset: {compressionResolution.RequestedPreset}");
        logBuilder.AppendLine($"ResolvedCompression: {compressionResolution.LogResolvedCompression}");
        logBuilder.AppendLine($"EffectiveCompression: {compressionResolution.EffectiveCompression}");
        logBuilder.AppendLine($"SameAsMameDefault: {compressionResolution.SameAsMameDefault}");

        if (!string.IsNullOrWhiteSpace(compressionResolution.TruthNoteKey))
        {
            logBuilder.AppendLine($"CompressionTruthNoteKey: {compressionResolution.TruthNoteKey}");
        }

        logBuilder.AppendLine($"HunkSize: {(resolvedHunkSizeBytes > 0 ? resolvedHunkSizeBytes.ToString() : "default")}");

        if (reportContext is not null)
        {
            logBuilder.AppendLine($"RequestedProfile: {reportContext.RequestedProfile}");
            logBuilder.AppendLine($"ResolvedCommand: {reportContext.ResolvedCommand}");
            logBuilder.AppendLine($"ResolvedCompression: {reportContext.ResolvedCompression}");
            logBuilder.AppendLine($"ResolvedHunkSize: {(reportContext.ResolvedHunkSize > 0 ? reportContext.ResolvedHunkSize.ToString() : "default")}");
            logBuilder.AppendLine($"EffectiveCompression: {reportContext.EffectiveCompression}");
            logBuilder.AppendLine($"EffectiveHunkSize: {(reportContext.EffectiveHunkSize > 0 ? reportContext.EffectiveHunkSize.ToString() : "default")}");
            logBuilder.AppendLine($"SameAsMameDefault: {reportContext.SameAsMameDefault}");
            logBuilder.AppendLine($"CompatibilityNotes: {reportContext.CompatibilityNotes}");
            logBuilder.AppendLine($"ChdmanVersion: {reportContext.ChdmanVersion}");
        }

        logBuilder.AppendLine($"AvailableLogicalProcessors: {availableLogicalProcessors}");
        logBuilder.AppendLine($"RequestedProcessors: {(maxProcessorCount > 0 ? maxProcessorCount.ToString() : "auto")}");
        logBuilder.AppendLine($"AutoResourceLimiter: {enableAutoResourceLimiter}");
        logBuilder.AppendLine($"ReservedLogicalCores: {reservedLogicalCores}");
        logBuilder.AppendLine($"PassedProcessors: {(passedProcessorLimit > 0 ? passedProcessorLimit.ToString() : "default")}");
        logBuilder.AppendLine($"PerformanceMode: {performanceMode}");
        logBuilder.AppendLine($"PriorityMode: {priorityMode}");
        logBuilder.AppendLine($"ChdmanDuration: {duration}");

        if (logicalInputBytes > 0)
        {
            logBuilder.AppendLine($"LogicalInputBytes: {logicalInputBytes}");
        }

        logBuilder.AppendLine($"DiskPreflightMessageKey: {diskPreflightMessageKey}");
        logBuilder.AppendLine($"DiskPreflightOperationKey: {diskPreflightOperationKey}");

        if (inputSha1 is not null)
        {
            string sha1Label = inputPreparationReport is not null
                ? "Prepared ISO SHA1"
                : "InputSHA1";
            logBuilder.AppendLine($"{sha1Label}: {inputSha1.Hex}");
        }

        logBuilder.AppendLine($"Success: {success}");
        AppendProcessText(logBuilder, "STDOUT", output);
        AppendProcessText(logBuilder, "STDERR", error);

        await File.WriteAllTextAsync(logPath, logBuilder.ToString(), CancellationToken.None).ConfigureAwait(false);
    }

    private static void AppendProcessText(StringBuilder logBuilder, string title, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        logBuilder.AppendLine();
        logBuilder.AppendLine($"=== {title} ===");
        logBuilder.AppendLine(text);
    }

    internal static ChdConversionResult BuildCancelledConversionResult(
        string inputPath,
        string outputPath,
        string displayCommandLine,
        string output,
        string error,
        string logPath,
        TimeSpan duration,
        int exitCode,
        int passedProcessorLimit,
        ChdCompressionResolution compressionResolution,
        int resolvedHunkSizeBytes,
        ChdExecutionReportContext? reportContext = null) => new()
        {
            IsSuccess = false,
            WasCancelled = true,
            ExitCode = exitCode,
            Status = ChdConversionStatus.UserCanceled,
            InputPath = inputPath,
            OutputPath = outputPath,
            CommandLine = displayCommandLine,
            Output = output,
            Error = error,
            Message = UserCancelledMessageKey,
            LogPath = logPath,
            ChdmanDuration = duration,
            NumProcessors = passedProcessorLimit,
            CompressionCodecs = compressionResolution.LogResolvedCompression,
            RequestedCompressionPreset = compressionResolution.RequestedPreset,
            ResolvedCompressionCodecs = compressionResolution.LogResolvedCompression,
            EffectiveCompressionCodecs = compressionResolution.EffectiveCompression,
            EffectiveCompressionSameAsMameDefault = compressionResolution.SameAsMameDefault,
            CompressionTruthNoteKey = compressionResolution.TruthNoteKey,
            HunkSizeBytes = resolvedHunkSizeBytes > 0 ? resolvedHunkSizeBytes : null,
            RequestedProfile = reportContext?.RequestedProfile ?? string.Empty,
            ResolvedCommand = reportContext?.ResolvedCommand ?? string.Empty,
            ResolvedCompression = reportContext?.ResolvedCompression ?? compressionResolution.LogResolvedCompression,
            ResolvedHunkSize = reportContext?.ResolvedHunkSize > 0 ? reportContext.ResolvedHunkSize : null,
            EffectiveCompression = reportContext?.EffectiveCompression ?? compressionResolution.EffectiveCompression,
            EffectiveHunkSize = reportContext?.EffectiveHunkSize > 0 ? reportContext.EffectiveHunkSize : null,
            SameAsMameDefault = reportContext?.SameAsMameDefault ?? compressionResolution.SameAsMameDefault,
            CompatibilityNotes = reportContext?.CompatibilityNotes ?? string.Empty,
            ChdmanVersion = reportContext?.ChdmanVersion ?? string.Empty
        };
}
