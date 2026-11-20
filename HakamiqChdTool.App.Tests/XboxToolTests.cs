using System.IO;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Xbox;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // The three bundled Xbox tools are in the build output, match their pinned SHA-256 digests and identify themselves.
    private static void TestBundledXboxToolsMatchPinnedDigests()
    {
        var runner = new ExternalToolProcessRunner();
        foreach ((XboxTool tool, string[] args, string expected) in new[]
        {
            (XboxTool.Xdvdfs, new[] { "--version" }, "xdvdfs-cli 0.8.3"),
            (XboxTool.Iso2God, new[] { "--version" }, "iso2god"),
            (XboxTool.ZArchive, Array.Empty<string>(), "ZArchive")
        })
        {
            AssertTrue(XboxToolLocator.TryLocate(tool, out string path), $"{tool} is bundled and matches its pinned digest.");
            ExternalToolProcessResult result = runner.RunAsync(path, args, CancellationToken.None, 4096,
                    candidate => XboxToolLocator.TryValidate(tool, candidate, out string normalized) ? normalized : null)
                .GetAwaiter().GetResult();
            AssertTrue((result.StandardOutput + result.StandardError).Contains(expected, StringComparison.OrdinalIgnoreCase),
                $"{tool} reports \"{expected}\".");
        }
    }

    // A missing, empty, tampered or misplaced Xbox tool is refused, and the runner refuses it again right before start.
    private static void TestXboxToolLocatorRejectsMissingAndTampered(string workDirectory)
    {
        string appRoot = Path.Combine(workDirectory, "xbox-tools-" + Guid.NewGuid().ToString("N"));
        string path = XboxToolLocator.BundledPath(XboxTool.Xdvdfs, appRoot);
        AssertFalse(XboxToolLocator.TryLocate(XboxTool.Xdvdfs, out _, appRoot), "A missing tool is refused.");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        AssertFalse(XboxToolLocator.TryLocate(XboxTool.Xdvdfs, out _, appRoot), "An empty tool is refused.");

        File.WriteAllBytes(path, [(byte)'M', (byte)'Z', 1, 2, 3]);
        AssertFalse(XboxToolLocator.TryLocate(XboxTool.Xdvdfs, out _, appRoot), "A tampered tool is refused.");

        AssertTrue(XboxToolLocator.TryLocate(XboxTool.Xdvdfs, out string real), "The real bundled tool is found.");
        File.Copy(real, path, overwrite: true);
        AssertTrue(XboxToolLocator.TryLocate(XboxTool.Xdvdfs, out _, appRoot), "An exact copy at the bundled path is accepted.");
        AssertFalse(XboxToolLocator.TryValidate(XboxTool.Iso2God, path, out _, appRoot), "A tool at another tool's path is refused.");

        string elsewhere = Path.Combine(appRoot, "xdvdfs.exe");
        File.Copy(real, elsewhere);
        AssertFalse(XboxToolLocator.TryValidate(XboxTool.Xdvdfs, elsewhere, out _, appRoot), "The pinned file outside its bundled folder is refused.");

        // Replaced after it was located: the runner checks again and does not start it.
        File.WriteAllBytes(path, [(byte)'M', (byte)'Z', 9]);
        ExternalToolProcessResult result = new ExternalToolProcessRunner().RunAsync(path, ["--version"], CancellationToken.None, 4096,
                candidate => XboxToolLocator.TryValidate(XboxTool.Xdvdfs, candidate, out string normalized, appRoot) ? normalized : null)
            .GetAwaiter().GetResult();
        AssertEqual(1, result.ExitCode, "A tool replaced after it was located is not started.");
        AssertTrue(result.StandardError.Contains("invalid", StringComparison.OrdinalIgnoreCase), "The refusal says the tool path is invalid.");
    }
}
