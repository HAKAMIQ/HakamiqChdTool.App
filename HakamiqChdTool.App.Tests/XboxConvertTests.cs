using System.IO;
using System.Text;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Xbox;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static string MakeXboxGameFolder(string root, string name, string startFile, int seed, int extraBytes = 256 * 1024)
    {
        string folder = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(folder, "media", "sound"));
        var random = new Random(seed);
        byte[] Bytes(int count)
        {
            byte[] data = new byte[count];
            random.NextBytes(data);
            return data;
        }

        File.WriteAllBytes(Path.Combine(folder, startFile), Bytes(64 * 1024));
        File.WriteAllBytes(Path.Combine(folder, "media", "level1.dat"), Bytes(extraBytes));
        File.WriteAllBytes(Path.Combine(folder, "media", "sound", "theme.wav"), Bytes(48 * 1024));
        return folder;
    }

    private static bool HasXboxTemporaryFiles(string folder) =>
        Directory.EnumerateFileSystemEntries(folder).Any(path => Path.GetFileName(path).StartsWith(".xbox-", StringComparison.Ordinal));

    // Xbox game folder -> XISO (unpacked again and compared), XISO -> XISO (content checksum), an existing output is
    // skipped, and no temporary file is left.
    private static void TestXboxGameConvertsToXiso(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "xbox-xiso-" + Guid.NewGuid().ToString("N"));
        string output = Path.Combine(root, "out");
        string output2 = Path.Combine(root, "out2");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(output2);
        string game = MakeXboxGameFolder(root, "Some Game", "default.xbe", 1);
        var service = new XboxConvertService(new ExternalToolProcessRunner());

        AssertEqual(XboxSourceKind.XboxFolder, service.DetectAsync(game, CancellationToken.None).GetAwaiter().GetResult(), "A folder with default.xbe is an Xbox game.");
        XboxConvertResult first = service.ConvertAsync(game, output, Xbox360Format.God, CancellationToken.None).GetAwaiter().GetResult();
        AssertEqual(XboxConvertOutcome.Done, first.Outcome, "The Xbox folder converts: " + first.MessageKey);
        string xiso = Path.Combine(output, "Some Game.iso");
        AssertEqual(xiso, first.OutputPath, "The XISO is named after the folder.");
        AssertTrue(first.OutputBytes > 0 && first.SourceBytes > 0, "Sizes are reported.");
        AssertFalse(HasXboxTemporaryFiles(output), "No temporary file is left.");

        AssertEqual(XboxSourceKind.XboxIso, service.DetectAsync(xiso, CancellationToken.None).GetAwaiter().GetResult(), "The XISO is an Xbox image (default.xbe in its root).");
        XboxConvertResult again = service.ConvertAsync(game, output, Xbox360Format.God, CancellationToken.None).GetAwaiter().GetResult();
        AssertEqual(XboxConvertOutcome.Skipped, again.Outcome, "An existing output is skipped, not replaced.");

        XboxConvertResult fromImage = service.ConvertAsync(xiso, output2, Xbox360Format.God, CancellationToken.None).GetAwaiter().GetResult();
        AssertEqual(XboxConvertOutcome.Done, fromImage.Outcome, "An XISO converts again with the same content checksum: " + fromImage.MessageKey);
        AssertFalse(HasXboxTemporaryFiles(output2), "No temporary file is left after an image conversion.");

        XboxConvertResult inside = service.ConvertAsync(game, Path.Combine(game, "media"), Xbox360Format.God, CancellationToken.None).GetAwaiter().GetResult();
        AssertEqual(XboxConvertService.OutputFolderKey, inside.MessageKey, "A game folder is never packed into itself.");

        string notXbox = Path.Combine(root, "Other.iso");
        File.WriteAllBytes(notXbox, new byte[0x20000]);
        AssertEqual(XboxConvertService.NotXboxKey, service.ConvertAsync(notXbox, output, Xbox360Format.God, CancellationToken.None).GetAwaiter().GetResult().MessageKey, "An image without the Xbox signature is refused.");
    }

    // Xbox 360 game folder -> ZAR, checked by unpacking it again; GOD of an invalid game fails cleanly; a stopped run
    // and a missing tool leave nothing behind.
    private static void TestXbox360GameConvertsToZar(string workDirectory)
    {
        string root = Path.Combine(workDirectory, "xbox360-zar-" + Guid.NewGuid().ToString("N"));
        string output = Path.Combine(root, "out");
        Directory.CreateDirectory(output);
        string game = MakeXboxGameFolder(root, "Some 360 Game", "default.xex", 2);
        var service = new XboxConvertService(new ExternalToolProcessRunner());

        AssertEqual(XboxSourceKind.Xbox360Folder, service.DetectAsync(game, CancellationToken.None).GetAwaiter().GetResult(), "A folder with default.xex is an Xbox 360 game.");
        XboxConvertResult zar = service.ConvertAsync(game, output, Xbox360Format.Zar, CancellationToken.None).GetAwaiter().GetResult();
        AssertEqual(XboxConvertOutcome.Done, zar.Outcome, "The Xbox 360 folder converts to ZAR: " + zar.MessageKey);
        AssertTrue(File.Exists(Path.Combine(output, "Some 360 Game.zar")), "The ZAR is named after the folder.");
        AssertFalse(HasXboxTemporaryFiles(output), "No temporary folder is left after the ZAR check.");

        // The synthetic default.xex is not a real executable, so iso2god refuses it; nothing is left behind.
        XboxConvertResult god = service.ConvertAsync(game, output, Xbox360Format.God, CancellationToken.None).GetAwaiter().GetResult();
        AssertEqual(XboxConvertOutcome.Failed, god.Outcome, "GOD of an invalid game fails.");
        AssertFalse(HasXboxTemporaryFiles(output), "A failed GOD conversion leaves no temporary files.");

        string stopOutput = Path.Combine(root, "stop");
        Directory.CreateDirectory(stopOutput);
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            XboxConvertResult stopped = service.ConvertAsync(game, stopOutput, Xbox360Format.Zar, cancelled.Token).GetAwaiter().GetResult();
            AssertEqual(XboxConvertOutcome.Cancelled, stopped.Outcome, "A stopped conversion reports Cancelled.");
        }

        string big = MakeXboxGameFolder(root, "Big 360 Game", "default.xex", 3, extraBytes: 96 * 1024 * 1024);
        using (var soon = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
        {
            XboxConvertResult midRun = service.ConvertAsync(big, stopOutput, Xbox360Format.Zar, soon.Token).GetAwaiter().GetResult();
            AssertTrue(midRun.Outcome is XboxConvertOutcome.Cancelled or XboxConvertOutcome.Done, "A run stopped part way ends cleanly.");
        }

        AssertFalse(HasXboxTemporaryFiles(stopOutput), "A stopped run leaves no temporary files.");

        var noTools = new XboxConvertService(new ExternalToolProcessRunner(), appDirectory: Path.Combine(root, "no-tools"));
        AssertEqual(XboxSourceKind.Xbox360Folder, noTools.DetectAsync(game, CancellationToken.None).GetAwaiter().GetResult(), "Folders are detected without the tools.");
        AssertEqual(XboxConvertService.ToolMissingKey, noTools.ConvertAsync(game, Directory.CreateDirectory(Path.Combine(root, "none")).FullName, Xbox360Format.Zar, CancellationToken.None).GetAwaiter().GetResult().MessageKey, "A missing tool is reported.");
    }

    // The Games on Demand output check: one title folder, a "LIVE" container and its data folder.
    private static void TestXboxGodOutputCheck(string workDirectory)
    {
        string work = Path.Combine(workDirectory, "god-check-" + Guid.NewGuid().ToString("N"));
        string content = Path.Combine(work, "485507D1", "00007000");
        Directory.CreateDirectory(Path.Combine(content, "0F8298C3.data"));
        File.WriteAllBytes(Path.Combine(content, "0F8298C3.data", "Data0000"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(content, "0F8298C3"), Encoding.ASCII.GetBytes("LIVE----"));

        AssertTrue(XboxConvertService.TryReadGod(work, out string titleId, out string container), "A GOD layout is read.");
        AssertEqual("485507D1", titleId, "The title ID is the folder name.");
        AssertEqual(Path.Combine(content, "0F8298C3"), container, "The container file is found.");

        File.WriteAllBytes(Path.Combine(content, "0F8298C3"), Encoding.ASCII.GetBytes("CON ----"));
        AssertFalse(XboxConvertService.TryReadGod(work, out _, out _), "A container that is not LIVE is refused.");
        File.WriteAllBytes(Path.Combine(content, "0F8298C3"), Encoding.ASCII.GetBytes("LIVE----"));
        Directory.CreateDirectory(Path.Combine(work, "ABCDEF01"));
        AssertFalse(XboxConvertService.TryReadGod(work, out _, out _), "More than one title folder is refused.");
    }

    // The saved Xbox 360 format: Games on Demand by default, ZAR when chosen; it survives Clone and CopyFrom, and an
    // unknown value falls back to Games on Demand.
    private static void TestXbox360FormatSetting()
    {
        AssertEqual("god", new HakamiqChdTool.App.Models.AppSettings().Xbox360Format, "Games on Demand is the default.");
        AssertEqual("zar", new HakamiqChdTool.App.Models.AppSettings { Xbox360Format = "zar" }.Clone().Xbox360Format, "Clone keeps the format.");
        var copy = new HakamiqChdTool.App.Models.AppSettings();
        copy.CopyFrom(new HakamiqChdTool.App.Models.AppSettings { Xbox360Format = "zar" });
        AssertEqual("zar", copy.Xbox360Format, "CopyFrom keeps the format.");

        AssertEqual(Xbox360Format.Zar, XboxFormats.Parse("ZAR"), "zar selects ZAR.");
        AssertEqual(Xbox360Format.God, XboxFormats.Parse("god"), "god selects Games on Demand.");
        AssertEqual(Xbox360Format.God, XboxFormats.Parse("iso"), "An unknown value falls back to Games on Demand.");
        AssertEqual(Xbox360Format.God, XboxFormats.Parse(null), "A missing value falls back to Games on Demand.");
        AssertEqual("zar", XboxFormats.ToSetting(Xbox360Format.Zar), "ZAR is saved as zar.");
        AssertEqual("god", XboxFormats.ToSetting(Xbox360Format.God), "Games on Demand is saved as god.");
    }
}