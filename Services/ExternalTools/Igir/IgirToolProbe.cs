using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HakamiqChdTool.App.Services;

namespace HakamiqChdTool.App.Services.ExternalTools.Igir;

public sealed record IgirToolProbeResult(string Path, string Version, bool IsAvailable, bool IsMissing);

public sealed class IgirToolProbe(IgirToolLocator locator, ExternalToolProcessRunner runner)
{
    private static readonly Regex VersionPattern = new(@"(?<![A-Za-z0-9.])v(?<version>\d+\.\d+\.\d+)(?:\s|$)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public IgirToolProbe() : this(new IgirToolLocator(), new ExternalToolProcessRunner()) { }

    public async Task<IgirToolProbeResult> CheckAsync(string appDirectory, string? preferredPath, CancellationToken token)
    {
        string? path = await locator.FindVerifiedAsync(appDirectory, preferredPath, token).ConfigureAwait(false);
        if (path is null)
        {
            string shownPath = !string.IsNullOrWhiteSpace(preferredPath)
                ? preferredPath
                : Path.GetFullPath(Path.Combine(appDirectory, "Tools", "igir", "win-x64", "igir.exe"));
            bool exists = File.Exists(shownPath);
            return new IgirToolProbeResult(shownPath, string.Empty, false, !exists);
        }

        // igir 5.7.2 prints its version in the report help banner; it does not support --version.
        ExternalToolProcessResult result = await runner.RunAsync(path, ["report", "--help"], token, 4096, "igir.exe").ConfigureAwait(false);
        string version = ParseVersion(result.StandardOutput + Environment.NewLine + result.StandardError);
        bool valid = !result.WasCancelled && result.ExitCode == 0
            && string.Equals(version, IgirToolLocator.Version, StringComparison.Ordinal);
        return new IgirToolProbeResult(path, version, valid, false);
    }

    private static string ParseVersion(string output)
    {
        Match match = VersionPattern.Match(output);
        string version = match.Success ? match.Groups["version"].Value : string.Empty;
        return Version.TryParse(version, out _) ? version : string.Empty;
    }
}
