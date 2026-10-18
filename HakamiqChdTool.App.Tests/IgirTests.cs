using HakamiqChdTool.App.Services.ExternalTools.Igir;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static void TestIgirArgumentsAreReportOnly(string workDirectory)
    {
        string library = Path.Combine(workDirectory, "igir-args-library");
        Directory.CreateDirectory(library);
        string dat = Path.Combine(workDirectory, "igir-args.dat");
        File.WriteAllText(dat, "synthetic DAT");
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string run = Path.Combine(appData, "HakamiqChdTool", "Igir", "test-" + Guid.NewGuid().ToString("N"));
        var args = IgirArgs.BuildReport(new IgirReportRequest(library, [dat], run));
        AssertEqual("report", args[0], "igir invocation must use its read-only report command.");
        AssertTrue(args.Contains("--input-checksum-min") && args.Contains("--report-output")
            && args.Contains("--cache-path") && args.Contains("--temp-dir"), "The verified report options are present.");
        AssertFalse(args.Any(argument => argument is "move" or "copy" or "link" or "extract" or "zip" or "clean" or "playlist" or "fixdat" or "dir2dat"
            or "--overwrite" or "--overwrite-invalid" or "--move-delete-dirs" or "--clean-backup" or "--patch"),
            "No igir write command or flag may be constructed.");
        AssertEqual("5.7.2", IgirToolLocator.Version, "The executable version is pinned.");
    }

    private static void TestIgirCandidateValidation(AppReflection app, string workDirectory)
    {
        string executable = Path.Combine(workDirectory, "igir.exe");
        File.WriteAllBytes(executable, [0x4D, 0x5A]);
        AssertTrue(app.ValidateExternalToolCandidate(executable, "igir.exe", out string normalized)
            && Path.GetFullPath(executable).Equals(normalized, StringComparison.OrdinalIgnoreCase),
            "The external path validator accepts an existing igir.exe without requiring CsoKit files.");
        AssertFalse(app.ValidateExternalToolCandidate(executable, "csokit.exe", out _),
            "The external path validator rejects an executable with the wrong tool identity.");
    }

    private static void TestIgirLocatorRejectsMissingAndTampered(AppReflection app, string workDirectory)
    {
        string missingRoot = Path.Combine(workDirectory, "igir-missing-app");
        Directory.CreateDirectory(missingRoot);
        AssertFalse(app.HasVerifiedIgir(missingRoot), "The igir locator returns no executable when the bundled path is missing.");

        string tamperedRoot = Path.Combine(workDirectory, "igir-tampered-app");
        string tool = Path.Combine(tamperedRoot, "Tools", "igir", "win-x64", "igir.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(tool)!);
        File.WriteAllBytes(tool, [0x4D, 0x5A]);
        AssertFalse(app.HasVerifiedIgir(tamperedRoot), "The igir locator rejects a present executable with a mismatched SHA-256.");
    }

    private static void TestIgirProbeDistinguishesMissingAndInvalid(string workDirectory)
    {
        string appRoot = Path.Combine(workDirectory, "igir-probe-missing");
        Directory.CreateDirectory(appRoot);
        IgirToolProbe probe = new();
        IgirToolProbeResult missing = probe.CheckAsync(appRoot, null, CancellationToken.None).GetAwaiter().GetResult();
        AssertTrue(missing.IsMissing && !missing.IsAvailable && missing.Path.EndsWith("igir.exe", StringComparison.OrdinalIgnoreCase),
            "A missing bundled igir has an unavailable state and a visible expected path.");

        string tampered = Path.Combine(workDirectory, "igir-probe-tampered.exe");
        File.WriteAllBytes(tampered, [0x4D, 0x5A]);
        IgirToolProbeResult invalid = probe.CheckAsync(appRoot, tampered, CancellationToken.None).GetAwaiter().GetResult();
        AssertTrue(!invalid.IsMissing && !invalid.IsAvailable && invalid.Path == tampered,
            "An existing file with a wrong digest is a failed check, not a missing tool.");

        string repositoryRoot = Path.GetDirectoryName(FindRepoFile("HakamiqChdTool.App.csproj"))!;
        string officialTool = Path.Combine(repositoryRoot, "Tools", "igir", "win-x64", "igir.exe");
        if (File.Exists(officialTool))
        {
            IgirToolProbeResult verified = probe.CheckAsync(repositoryRoot, officialTool, CancellationToken.None).GetAwaiter().GetResult();
            AssertTrue(verified.IsAvailable && verified.Version == "5.7.2",
                "The pinned official executable is connected only when its report help banner confirms v5.7.2.");
        }
    }

    private static void TestIgirReportParserHandlesRfcCsv(string workDirectory)
    {
        string report = Path.Combine(workDirectory, "igir-report.csv");
        File.WriteAllText(report,
            "DAT Name,Game Name,Status,ROM Files,BIOS\r\n" +
            "\"Sony, PlayStation\",\"Game, (USA)\",FOUND,\"disc, one.chd\",false\r\n" +
            "\"DAT\",\"Multiline\r\nTitle\",DUPLICATE,\"a.bin; b.bin\",true\r\n", Encoding.UTF8);
        var rows = IgirReportParser.Parse(report);
        AssertTrue(rows.Count == 2 && rows[0].DatName == "Sony, PlayStation"
            && rows[0].RomFiles == "disc, one.chd" && rows[0].Flags["BIOS"] == "false",
            "Quoted commas and boolean columns parse without losing field boundaries.");
        AssertTrue(rows[1].GameName == "Multiline\r\nTitle" && rows[1].Status == "DUPLICATE",
            "Quoted newlines and duplicate status are retained.");
        File.WriteAllText(report, "DAT Name,Game Name,Status,ROM Files\r\nD,G,UNKNOWN,a.iso\r\n");
        bool rejected = false;
        try { _ = IgirReportParser.Parse(report); }
        catch (InvalidDataException) { rejected = true; }
        AssertTrue(rejected, "Unknown statuses fail closed instead of being guessed.");
        File.WriteAllText(report, "DAT Name,Status\r\nDAT,FOUND\r\n");
        rejected = false;
        try { _ = IgirReportParser.Parse(report); }
        catch (InvalidDataException) { rejected = true; }
        AssertTrue(rejected, "A report missing required columns is rejected.");
    }
}
