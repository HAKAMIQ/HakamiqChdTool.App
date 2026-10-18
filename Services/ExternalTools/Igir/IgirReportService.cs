using HakamiqChdTool.App.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HakamiqChdTool.App.Services.Configuration;

namespace HakamiqChdTool.App.Services.ExternalTools.Igir;

public sealed record IgirReportResult(string ReportPath, IReadOnlyList<IgirReportRow> Rows);

public sealed class IgirReportService(IgirToolLocator locator, ExternalToolProcessRunner processRunner)
{
    public IgirReportService() : this(new IgirToolLocator(), new ExternalToolProcessRunner()) { }

    public async Task<IgirReportResult> RunAsync(string applicationDirectory, IgirReportRequest request,
        CancellationToken cancellationToken = default)
    {
        using AppSettingsService settingsService = new();
        string executable = await locator.FindVerifiedAsync(applicationDirectory, settingsService.Load().ExternalIgirPath, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException("Verified igir v5.7.2 executable is not installed.");
        IReadOnlyList<string> arguments = IgirArgs.BuildReport(request);
        Directory.CreateDirectory(request.WorkRoot);
        string cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HakamiqChdTool", "Igir");
        Directory.CreateDirectory(cacheRoot);
        Directory.CreateDirectory(Path.Combine(request.WorkRoot, "temp"));
        if (!HakamiqChdTool.App.Core.Input.InputResolver.IsSafeExistingPath(request.WorkRoot)
            || !HakamiqChdTool.App.Core.Input.InputResolver.IsSafeExistingPath(Path.Combine(request.WorkRoot, "temp"))
            || !HakamiqChdTool.App.Core.Input.InputResolver.IsSafeExistingPath(cacheRoot))
            throw new IOException("igir work or cache path traverses a reparse point.");
        ExternalToolProcessResult result = await processRunner.RunAsync(executable, arguments, cancellationToken, 64 * 1024, "igir.exe")
            .ConfigureAwait(false);
        Log.Information("igir report completed. ExitCode={ExitCode} Cancelled={Cancelled} StdOut={StdOut} StdErr={StdErr}",
            result.ExitCode, result.WasCancelled, result.StandardOutput, result.StandardError);
        if (result.WasCancelled) throw new OperationCanceledException(cancellationToken);
        if (result.ExitCode != 0) throw new IOException($"igir report failed with exit code {result.ExitCode}.");
        string reportPath = Path.Combine(Path.GetFullPath(request.WorkRoot), "report.csv");
        return new IgirReportResult(reportPath, IgirReportParser.Parse(reportPath));
    }
}
