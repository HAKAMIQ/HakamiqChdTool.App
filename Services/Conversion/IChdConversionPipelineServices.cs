using HakamiqChdTool.App.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text;

namespace HakamiqChdTool.App.Services;

public interface IChdCommandPreparationService
{
    string BuildCommand(string inputPath, ChdmanExtractionKind extractionKind = ChdmanExtractionKind.None, IsoCreateCommandOverride isoCreateCommandOverride = IsoCreateCommandOverride.Auto);
    string NormalizePathForCli(string path);
    (string Command, IsoChdmanCreateDiagnostics? IsoDiagnostics) ResolveTwoWayCommandWithOptionalIsoDiagnostics(string inputExtension, ChdmanExtractionKind extractionKind, string? fullInputPathForIsoClassification, IsoCreateCommandOverride isoCreateCommandOverride);
    string BuildLogsDirectory();
    string SanitizeFileName(string value);
    bool IsCreateCommand(string command);
    bool IsExtractCommand(string command);
    string ResolveCompressionSetting(string? compressionCodecs, string command);
    ChdCompressionResolution ResolveCompressionSettingWithTruth(string? compressionCodecs, string command);
    int ResolveHunkSizeSetting(int hunkSizeBytes, string command, string inputPath, out string policyNote);
    string BuildExtractCdBinOutputPath(string cueOutputPath);
    bool TryBuildCreateCdHunkRetrySize(int requestedHunkSizeBytes, int requiredSectorSize, int currentResolvedHunkSizeBytes, out int retryHunkSizeBytes);
    void ReplaceOrAddHunkSizeArgument(List<string> arguments, int hunkSizeBytes);
    bool IsExtractCdSplitbinPatternRequired(ChdmanCliRunner.Result run);
    void ReplaceExtractCdBinOutputArgument(List<string> arguments, string binOutputPath);
    string BuildSplitBinExtractCdBinOutputPath(string cueOutputPath);
}

public interface IChdProcessExecutionService
{
    string FormatCommandLineForDisplay(string executablePath, IReadOnlyList<string> arguments);

    Task<ChdmanCliRunner.Result> ExecuteAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        bool parseProgressPercent,
        IProgress<int>? progress,
        Action<int>? onProcessStarted,
        CancellationToken cancellationToken,
        string? exclusiveFileAccessPath,
        string? monitoredOutputPath,
        IProgress<PerformanceSample>? performanceProgress,
        ChdmanProcessPriorityMode priorityMode);

    bool IsCreateCdHunkSizeMultipleError(ChdmanCliRunner.Result run, out int rejectedHunkSize, out int requiredSectorSize);
}

public interface IChdResultMappingService
{
    void TryDeleteIncompleteOutputs(string outputPath, bool isExtractCommand, string reason);
    bool VerifyOutputExists(string outputPath, bool isExtractCommand);
}

public interface IChdVerificationBridge
{
    bool TryValidateDescriptorDependenciesBeforeChdman(string inputPath, string command, out string failureMessageKey);
    bool TryNormalizeExtractedCueBinOutput(string cueOutputPath);
}

public interface IChdProgressParser
{
    ChdmanProgressSnapshot ParseSnapshot(string? line, bool isErrorLine, int? minimumPercent = null);

    bool TryParseLastPercent(string? text, out int percent);

    bool TryParseLastPercent(StringBuilder rolling, out int percent);

    bool TryParseActiveProgressSnapshot(
        StringBuilder rolling,
        bool isErrorLine,
        int? minimumPercent,
        out ChdmanProgressSnapshot snapshot);

    string StripPercentTokensForNarrative(string? detail);

    string ToCleanLogLine(string? line);
}
