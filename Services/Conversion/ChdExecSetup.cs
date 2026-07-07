using HakamiqChdTool.App.Core.Chd.Commands;
using HakamiqChdTool.App.Core.Chd.Profiles;
using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static HakamiqChdTool.App.Services.ChdConversionMessages;

namespace HakamiqChdTool.App.Services;

internal static class ChdConversionExecutionSetup
{
    internal sealed record DiskPreflightContext(
        string MessageKey,
        string OperationKey);

    internal sealed record OutputTargetContext(
        string ExtractCdCueOutputPathForArgument,
        string ExtractCdBinOutputPathForArgument);

    internal sealed record ArgumentContext(
        int AvailableLogicalProcessors,
        int PassedProcessorLimit,
        List<string> Arguments,
        string MonitoredOutputPath);

    internal static DiskPreflightContext ResolveDiskPreflight(
        bool enableDiskSpaceGuard,
        string resolvedInputPath,
        string resolvedOutputPath,
        string command,
        long? expectedOutputBytes,
        bool isExtractCommand)
    {
        DiskPreflightMode diskPreflightMode = isExtractCommand
            ? DiskPreflightMode.ExtractFromChd
            : DiskPreflightMode.CreateChd;

        if (!enableDiskSpaceGuard)
        {
            string operationKey = DiskSpacePreflightService.DescribeOperationKey(command, diskPreflightMode);
            Log.Information(
                "Disk preflight skipped because EnableDiskSpaceGuard is disabled. Input={InputPath}, Output={OutputPath}, Command={Command}, OperationKey={OperationKey}",
                resolvedInputPath,
                resolvedOutputPath,
                command,
                operationKey);

            return new DiskPreflightContext("DiskPreflightDisabled", operationKey);
        }

        DiskPreflightResult diskPreflight = DiskSpacePreflightService.CheckOrThrow(
            resolvedInputPath,
            resolvedOutputPath,
            command,
            expectedOutputBytes);

        Log.Information(
            "Disk preflight passed. Root={Root}, InputBytes={InputBytes}, EstimatedRequiredBytes={EstimatedRequiredBytes}, AvailableFreeBytes={AvailableFreeBytes}, MessageKey={MessageKey}, OperationKey={OperationKey}",
            diskPreflight.TargetRoot,
            diskPreflight.InputBytes,
            diskPreflight.EstimatedRequiredBytes,
            diskPreflight.AvailableFreeBytes,
            diskPreflight.MessageKey,
            diskPreflight.OperationKey);

        return new DiskPreflightContext(diskPreflight.MessageKey, diskPreflight.OperationKey);
    }

    internal static OutputTargetContext PrepareOutputTargets(
        ChdmanExtractionKind extractionKind,
        string resolvedOutputPath,
        string resolvedExtractCdCueOutputPath,
        string resolvedExtractCdBinOutputPath,
        IChdCommandPreparationService commandPreparation)
    {
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
                : commandPreparation.BuildExtractCdBinOutputPath(extractCdCueOutputPathForArgument))
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

        return new OutputTargetContext(extractCdCueOutputPathForArgument, extractCdBinOutputPathForArgument);
    }

    internal static ArgumentContext BuildArgumentContext(
        ChdPlatformProfile? createProfile,
        bool isExtractCommand,
        string command,
        string resolvedInputPath,
        string resolvedOutputPath,
        ChdmanExtractionKind extractionKind,
        string extractCdCueOutputPathForArgument,
        string extractCdBinOutputPathForArgument,
        int maxProcessorCount,
        bool enableAutoResourceLimiter,
        int reservedLogicalCores,
        ConversionPerformanceMode performanceMode,
        string resolvedCompression,
        int resolvedHunkSizeBytes,
        bool allowOverwriteOutput,
        IChdCommandPreparationService commandPreparation)
    {
        int availableLogicalProcessors = ProcessorTopologyService.GetAvailableLogicalProcessorCount();
        int normalizedProcessorLimit = ProcessorTopologyService.ResolveChdmanProcessorCount(
            maxProcessorCount,
            enableAutoResourceLimiter,
            reservedLogicalCores,
            performanceMode);

        int passedProcessorLimit = isExtractCommand ? 0 : normalizedProcessorLimit;

        List<string> arguments = !isExtractCommand && createProfile is not null
            ? ChdmanCommandBuilder
                .BuildCreateArgs(createProfile, resolvedInputPath, resolvedOutputPath, passedProcessorLimit)
                .ToList()
            : new List<string>
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

        if (allowOverwriteOutput && (isExtractCommand || commandPreparation.IsCreateCommand(command)))
        {
            arguments.Add("-f");
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

        return new ArgumentContext(
            availableLogicalProcessors,
            passedProcessorLimit,
            arguments,
            monitoredOutputPath);
    }
}
