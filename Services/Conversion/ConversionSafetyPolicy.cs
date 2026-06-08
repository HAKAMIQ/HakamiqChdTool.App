using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Storage;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services.Conversion;

internal sealed record ConversionSafetyDecision(
    bool CanStartConversion,
    string? FailureCode,
    string UserMessageKey,
    bool HadInputReadWarning)
{
    public static ConversionSafetyDecision Allow(bool hadInputReadWarning = false) => new(
        true,
        null,
        string.Empty,
        hadInputReadWarning);

    public static ConversionSafetyDecision Block(string failureCode, string messageKey) => new(
        false,
        failureCode,
        messageKey,
        true);
}

internal sealed class ConversionSafetyPolicy
{
    public const string InputReadFailureMessageKey = "LocConversionSafety_InputReadCrcOrIoFailure";
    public const string SameDiskWarningMessageKey = "LocConversionSafety_SourceAndOutputSameVolume";
    public const string ChdmanInputReadWarningMessageKey = "LocConversionSafety_ChdmanInputReadWarning";

    private readonly ILogger _log;

    public ConversionSafetyPolicy()
        : this(Log.ForContext<ConversionSafetyPolicy>())
    {
    }

    public ConversionSafetyPolicy(ILogger log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<DeepHashAnalysisResult> RunDeepHashInputReadCheckAsync(
        string inputPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        DeepHashAnalysisResult result = await DeepHashAnalyzer
            .DeepHashAnalyzeAsync(inputPath, redumpDatabase: null, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(result.FailureCode))
        {
            _log.Warning(
                "Deep hash input-read safety check returned a failure code. Input={Input}; FailureCode={FailureCode}",
                inputPath,
                result.FailureCode);
        }

        return result;
    }

    public ConversionSafetyDecision EvaluateDeepHashResult(DeepHashAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsFatalInputReadFailure)
        {
            return ConversionSafetyDecision.Block(
                DeepHashAnalyzer.InputReadCrcOrIoFailureCode,
                InputReadFailureMessageKey);
        }

        return ConversionSafetyDecision.Allow();
    }

    public bool ShouldWarnSameSourceAndOutputVolume(StorageTopologySnapshot topology)
    {
        ArgumentNullException.ThrowIfNull(topology);
        return topology.SourceAndFinalOutputSameVolume;
    }

    public bool LooksLikeChdmanInputReadFailure(ChdConversionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        string text = string.Concat(result.Output, Environment.NewLine, result.Error);
        return text.Contains("cyclic redundancy check", StringComparison.OrdinalIgnoreCase)
            || text.Contains("data error", StringComparison.OrdinalIgnoreCase)
            || text.Contains("crc", StringComparison.OrdinalIgnoreCase)
            || text.Contains("i/o error", StringComparison.OrdinalIgnoreCase)
            || text.Contains("io error", StringComparison.OrdinalIgnoreCase)
            || text.Contains("read error", StringComparison.OrdinalIgnoreCase)
            || text.Contains("source read failure", StringComparison.OrdinalIgnoreCase)
            || text.Contains("unreadable source file", StringComparison.OrdinalIgnoreCase);
    }
}
