using Serilog;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public enum CsoToolStatus
{
    Available = 0,
    Missing = 1,
    Failed = 2
}

public sealed record CsoToolProbeResult(
    CsoToolStatus Status,
    string ToolPath,
    string VersionText,
    string MessageKey,
    int? ExitCode,
    string DiagnosticText)
{
    public bool IsAvailable => Status == CsoToolStatus.Available;
}

public sealed class CsoToolProbe
{
    public const string ToolMissingMessageKey = "LocWorkflow_CsoToolMissing";
    public const string ToolFailedMessageKey = "LocWorkflow_CsoToolFailed";

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
    private static readonly ILogger Log = global::Serilog.Log.ForContext<CsoToolProbe>();

    private readonly CsoToolLocator _locator;
    private readonly ExternalToolProcessRunner _runner;

    public CsoToolProbe()
        : this(new CsoToolLocator(), new ExternalToolProcessRunner())
    {
    }

    public CsoToolProbe(CsoToolLocator locator, ExternalToolProcessRunner runner)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<CsoToolProbeResult> CheckAsync(CancellationToken cancellationToken)
    {
        CsoToolLocation location = _locator.Locate();
        if (!location.IsFound)
        {
            return new CsoToolProbeResult(
                CsoToolStatus.Missing,
                string.Empty,
                string.Empty,
                ToolMissingMessageKey,
                null,
                string.Empty);
        }

        using CancellationTokenSource probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCts.CancelAfter(ProbeTimeout);

        ExternalToolProcessResult probe = await _runner
            .RunAsync(location.ToolPath, ["--version"], probeCts.Token)
            .ConfigureAwait(false);

        string diagnostic = JoinProcessText(probe.StandardOutput, probe.StandardError);
        string versionText = FirstNonEmptyLine(diagnostic);

        if (probe.WasCancelled
            || probe.ExitCode != 0
            || !versionText.Contains("Hakamiq.CsoKit", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning(
                "Hakamiq CsoKit probe failed. Tool={ToolPath}; ExitCode={ExitCode}; Cancelled={Cancelled}; VersionText={VersionText}",
                location.ToolPath,
                probe.ExitCode,
                probe.WasCancelled,
                versionText);

            return new CsoToolProbeResult(
                CsoToolStatus.Failed,
                location.ToolPath,
                versionText,
                ToolFailedMessageKey,
                probe.ExitCode,
                diagnostic);
        }

        return new CsoToolProbeResult(
            CsoToolStatus.Available,
            location.ToolPath,
            versionText,
            string.Empty,
            probe.ExitCode,
            diagnostic);
    }

    private static string JoinProcessText(string stdout, string stderr)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return stderr?.Trim() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(stderr))
        {
            return stdout.Trim();
        }

        return stdout.Trim() + Environment.NewLine + stderr.Trim();
    }

    private static string FirstNonEmptyLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line.Trim();
            }
        }

        return string.Empty;
    }
}
