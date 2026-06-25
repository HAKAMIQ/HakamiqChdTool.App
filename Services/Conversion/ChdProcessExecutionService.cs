using HakamiqChdTool.App.Models;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public sealed class ChdProcessExecutionService : IChdProcessExecutionService
{

    public string FormatCommandLineForDisplay(string executablePath, IReadOnlyList<string> arguments) =>
        ChdmanCliRunner.FormatCommandLineForDisplay(executablePath, arguments);

    public Task<ChdmanCliRunner.Result> ExecuteAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        bool parseProgressPercent,
        IProgress<int>? progress,
        Action<int>? onProcessStarted,
        CancellationToken cancellationToken,
        string? exclusiveFileAccessPath,
        string? monitoredOutputPath,
        IProgress<PerformanceSample>? performanceProgress,
        ChdmanProcessPriorityMode priorityMode) =>
        ChdmanCliRunner.ExecuteAsync(
            executablePath: executablePath,
            arguments: arguments,
            parseProgressPercent: parseProgressPercent,
            progress: progress,
            onProcessStarted: onProcessStarted,
            cancellationToken: cancellationToken,
            exclusiveFileAccessPath: exclusiveFileAccessPath,
            monitoredOutputPath: monitoredOutputPath,
            performanceProgress: performanceProgress,
            priorityMode: priorityMode);
    public bool IsCreateCdHunkSizeMultipleError(ChdmanCliRunner.Result run, out int rejectedHunkSize, out int requiredSectorSize)
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


}
