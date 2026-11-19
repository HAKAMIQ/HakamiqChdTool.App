using HakamiqChdTool.App.Services;
using System;
using System.Diagnostics;
using System.IO;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private const string BundledSevenZipVersion = "7-Zip 26.06 (x64)";

    // The real archive tests accept any 7-Zip the service resolves, including an installed copy under Program Files.
    // This test pins the bundled copy itself: the service must resolve Tools\7zip\7z.exe, which it only does when both
    // 7z.exe and 7z.dll match the pinned SHA-256 digests, and that copy must report the expected release.
    private static void TestBundledSevenZipMatchesPinnedDigests()
    {
        string bundledDirectory = Path.Combine(AppContext.BaseDirectory, "Tools", "7zip");
        string bundledExe = Path.Combine(bundledDirectory, "7z.exe");
        AssertTrue(File.Exists(bundledExe), "The bundled 7z.exe is copied to the output.");
        AssertTrue(File.Exists(Path.Combine(bundledDirectory, "7z.dll")), "The bundled 7z.dll is copied to the output.");

        AssertTrue(SevenZipToolService.Instance.TryGetExecutablePath(out string resolved), "A 7-Zip executable is resolved.");
        AssertEqual(Path.GetFullPath(bundledExe), Path.GetFullPath(resolved),
            "The bundled 7-Zip is resolved, so its files match the pinned SHA-256 digests.");

        var startInfo = new ProcessStartInfo(resolved, "i")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("7-Zip did not start.");
        process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        AssertTrue(process.WaitForExit(30_000), "7-Zip exits after printing its information.");
        AssertTrue(output.Contains(BundledSevenZipVersion, StringComparison.Ordinal),
            "The bundled 7-Zip reports " + BundledSevenZipVersion + ".");
    }
}
