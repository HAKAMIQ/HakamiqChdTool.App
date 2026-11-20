using HakamiqChdTool.App.Services.Shell;
using HakamiqChdTool.App.Startup;
using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Threading;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // --mode and --add are parsed, paths become absolute and unique, and bad input is refused.
    private static void TestLaunchArgsParse()
    {
        string cwd = Environment.CurrentDirectory;
        AssertTrue(LaunchArgs.TryParse([], out LaunchRequest empty, out _), "No arguments is a plain start.");
        AssertFalse(empty.HasPaths, "A plain start carries no paths.");
        AssertEqual(LaunchMode.None, empty.Mode, "A plain start keeps the selected mode.");

        AssertTrue(LaunchArgs.TryParse(["--mode", "Extract", "--add", @"C:\Games\a.chd", "b.chd", @"c:\games\A.CHD"], out LaunchRequest request, out _), "Mode and paths parse.");
        AssertEqual(LaunchMode.Extract, request.Mode, "Mode is read case-insensitively.");
        AssertEqual(2, request.Paths.Count, "Duplicate paths are dropped.");
        AssertEqual(@"C:\Games\a.chd", request.Paths[0], "An absolute path is kept.");
        AssertEqual(Path.Combine(cwd, "b.chd"), request.Paths[1], "A relative path is made absolute against the launching process.");

        AssertFalse(LaunchArgs.TryParse(["--mode", "delete"], out _, out _), "An unknown mode is refused.");
        AssertFalse(LaunchArgs.TryParse(["--mode"], out _, out _), "A missing mode is refused.");
        AssertFalse(LaunchArgs.TryParse(["--format", "c:"], out _, out _), "An unknown option is refused.");
        AssertFalse(LaunchArgs.TryParse([@"C:\a.iso"], out _, out _), "A path without --add is refused.");
        AssertFalse(LaunchArgs.TryParse(["--add", @"C:\games\*.iso"], out _, out _), "Wildcards are refused.");
        AssertFalse(LaunchArgs.TryParse(["--add", "C:\\a\u0001.iso"], out _, out _), "Control characters are refused.");

        string[] many = ["--add", .. Enumerable.Range(0, LaunchArgs.MaxPaths + 1).Select(i => $@"C:\g\{i}.iso")];
        AssertFalse(LaunchArgs.TryParse(many, out _, out _), "More than the path limit is refused.");
    }

    // A request crosses the pipe to the running instance intact; malformed and oversized messages are ignored.
    private static void TestInstanceChannelRoundTrip()
    {
        string pipeName = "HakamiqChdTool.Tests." + Guid.NewGuid().ToString("N");
        var received = new List<LaunchRequest>();
        using var arrived = new SemaphoreSlim(0);
        using (InstanceChannel.Start(pipeName, request =>
        {
            lock (received)
            {
                received.Add(request);
            }

            arrived.Release();
        }))
        {
            var request = new LaunchRequest(LaunchMode.Verify, [@"C:\Games\Disc (1).chd", @"D:\Mixed\Folder"]);
            AssertTrue(InstanceChannel.TrySend(pipeName, request, TimeSpan.FromSeconds(5)), "The request is sent.");
            AssertTrue(arrived.Wait(TimeSpan.FromSeconds(5)), "The running instance receives the request.");
            AssertEqual(LaunchMode.Verify, received[0].Mode, "The mode arrives.");
            AssertTrue(received[0].Paths.SequenceEqual(request.Paths), "The paths arrive in order.");

            AssertTrue(InstanceChannel.TrySend(pipeName, LaunchRequest.Empty, TimeSpan.FromSeconds(5)), "A plain activation is sent.");
            AssertTrue(arrived.Wait(TimeSpan.FromSeconds(5)), "A plain activation arrives.");
            AssertFalse(received[1].HasPaths, "A plain activation has no paths.");

            // A malformed message must not reach the callback, and the channel keeps working afterwards.
            using (var raw = new System.IO.Pipes.NamedPipeClientStream(".", pipeName, System.IO.Pipes.PipeDirection.Out, System.IO.Pipes.PipeOptions.CurrentUserOnly))
            {
                raw.Connect(5000);
                byte[] junk = Encoding.UTF8.GetBytes("{\"v\":1,\"mode\":\"format\",\"paths\":[]}");
                raw.Write(junk, 0, junk.Length);
            }

            AssertTrue(InstanceChannel.TrySend(pipeName, new LaunchRequest(LaunchMode.Convert, [@"C:\x.iso"]), TimeSpan.FromSeconds(5)), "The channel still accepts requests after a bad message.");
            AssertTrue(arrived.Wait(TimeSpan.FromSeconds(5)), "The next good request arrives.");
            AssertEqual(3, received.Count, "The malformed message was ignored.");
            AssertEqual(LaunchMode.Convert, received[2].Mode, "The good request after it is intact.");
        }

        AssertFalse(InstanceChannel.TrySend(pipeName, LaunchRequest.Empty, TimeSpan.FromMilliseconds(300)), "Sending fails when no instance is listening.");
    }

    // Message decoding refuses wrong versions, relative paths, unknown modes and oversized payloads; large requests split.
    private static void TestInstanceChannelDecodingIsStrict()
    {
        static byte[] Json(string text) => Encoding.UTF8.GetBytes(text);
        AssertTrue(InstanceChannel.TryDecode(Json("{\"v\":1,\"mode\":\"\",\"paths\":[\"C:\\\\a.iso\"]}"), out LaunchRequest ok), "A valid message decodes.");
        AssertEqual(@"C:\a.iso", ok.Paths[0], "The path decodes.");
        AssertFalse(InstanceChannel.TryDecode(Json("{\"v\":2,\"mode\":\"\",\"paths\":[]}"), out _), "Another format version is refused.");
        AssertFalse(InstanceChannel.TryDecode(Json("{\"v\":1,\"mode\":\"\",\"paths\":[\"a.iso\"]}"), out _), "A relative path is refused.");
        AssertFalse(InstanceChannel.TryDecode(Json("{\"v\":1,\"mode\":\"\",\"paths\":[\"C:\\\\..\\\\x\\\\..\\\\a.iso\"]}"), out _), "A path that is not already normalized is refused.");
        AssertFalse(InstanceChannel.TryDecode(Json("{\"v\":1,\"mode\":\"wipe\",\"paths\":[]}"), out _), "An unknown mode is refused.");
        AssertFalse(InstanceChannel.TryDecode(Json("[1,2]"), out _), "A non-object is refused.");
        AssertFalse(InstanceChannel.TryDecode(Json("not json"), out _), "Text that is not JSON is refused.");
        AssertFalse(InstanceChannel.TryDecode(new byte[InstanceChannel.MaxMessageBytes + 1], out _), "An oversized message is refused.");

        string longName = new('a', 30_000);
        var big = new LaunchRequest(LaunchMode.Convert, [.. Enumerable.Range(0, 100).Select(i => $@"C:\{i}{longName}.iso")]);
        List<byte[]> messages = [.. InstanceChannel.Encode(big)];
        AssertTrue(messages.Count > 1, "A request over the message limit is split.");
        var decoded = new List<string>();
        foreach (byte[] message in messages)
        {
            AssertTrue(message.Length <= InstanceChannel.MaxMessageBytes, "Each part fits the message limit.");
            AssertTrue(InstanceChannel.TryDecode(message, out LaunchRequest part), "Each part decodes.");
            AssertEqual(LaunchMode.Convert, part.Mode, "Each part keeps the mode.");
            decoded.AddRange(part.Paths);
        }

        AssertTrue(decoded.SequenceEqual(big.Paths), "The parts carry every path in order.");
    }

    // Registration writes the expected verbs under a private key, repeats safely, and removal deletes only its own keys.
    private static void TestExplorerMenuRegistersAndRemovesOnlyItsOwnKeys()
    {
        string testRoot = @"Software\HakamiqChdTool.Tests\" + Guid.NewGuid().ToString("N");
        string classes = testRoot + @"\Classes";
        try
        {
            using (RegistryKey other = Registry.CurrentUser.CreateSubKey(classes + @"\SystemFileAssociations\.iso\shell\OtherApp.Open\command"))
            {
                other.SetValue(string.Empty, "other.exe \"%1\"");
            }

            var registration = new ExplorerMenuRegistration(Registry.CurrentUser, classes);
            var text = new ExplorerMenuText("Convert to CHD", "Extract", "Verify", "CHD Tool");
            const string exe = @"C:\Tools\CHD Tool\HakamiqChdTool.exe";
            AssertFalse(registration.IsRegistered(), "Nothing is registered at first.");
            AssertFalse(registration.TryRegister("relative.exe", text), "A relative executable path is refused.");
            AssertTrue(registration.TryRegister(exe, text), "Registration succeeds.");
            AssertTrue(registration.TryRegister(exe, text with { Convert = "تحويل إلى CHD" }), "Registering again updates in place.");
            AssertTrue(registration.IsRegistered(), "The menu reports as registered.");

            foreach (string extension in ExplorerMenuRegistration.ConvertExtensions)
            {
                using RegistryKey? verb = Registry.CurrentUser.OpenSubKey($@"{classes}\SystemFileAssociations\{extension}\shell\HakamiqChdTool.Convert");
                AssertTrue(verb is not null, $"{extension} has a Convert entry.");
                AssertEqual("تحويل إلى CHD", verb!.GetValue("MUIVerb") as string, $"{extension} shows the latest text.");
                using RegistryKey? command = verb.OpenSubKey("command");
                AssertEqual($"\"{exe}\" --mode convert --add \"%1\"", command?.GetValue(string.Empty) as string, $"{extension} starts the app in convert mode with the quoted file.");
            }

            using (RegistryKey? extract = Registry.CurrentUser.OpenSubKey($@"{classes}\SystemFileAssociations\.chd\shell\HakamiqChdTool.Extract\command"))
            {
                AssertEqual($"\"{exe}\" --mode extract --add \"%1\"", extract?.GetValue(string.Empty) as string, "CHD files get Extract.");
            }

            using (RegistryKey? verify = Registry.CurrentUser.OpenSubKey($@"{classes}\Directory\shell\HakamiqChdTool.Menu\shell\3.Verify\command"))
            {
                AssertEqual($"\"{exe}\" --mode verify --add \"%1\"", verify?.GetValue(string.Empty) as string, "Folders get a submenu with Verify.");
            }

            AssertFalse(ExplorerMenuRegistration.ConvertExtensions.Contains(".bin"), "Plain BIN files get no entry; they need their CUE.");
            AssertFalse(ExplorerMenuRegistration.ConvertExtensions.Contains(".chd"), "CHD files are not offered for conversion.");

            // An entry for an extension that is no longer listed is still removed.
            Registry.CurrentUser.CreateSubKey(classes + @"\SystemFileAssociations\.old\shell\HakamiqChdTool.Convert\command")!.Dispose();

            AssertTrue(registration.TryUnregister(), "Removal succeeds.");
            AssertFalse(registration.IsRegistered(), "The menu reports as removed.");
            using (RegistryKey? stale = Registry.CurrentUser.OpenSubKey(classes + @"\SystemFileAssociations\.old\shell\HakamiqChdTool.Convert"))
            {
                AssertTrue(stale is null, "Entries for extensions no longer listed are removed too.");
            }

            using (RegistryKey? chd = Registry.CurrentUser.OpenSubKey(classes + @"\SystemFileAssociations\.chd\shell"))
            {
                AssertEqual(0, chd?.GetSubKeyNames().Length ?? 0, "CHD entries are removed.");
            }

            using (RegistryKey? other = Registry.CurrentUser.OpenSubKey(classes + @"\SystemFileAssociations\.iso\shell\OtherApp.Open\command"))
            {
                AssertEqual("other.exe \"%1\"", other?.GetValue(string.Empty) as string, "Another app's entry is left untouched.");
            }

            AssertTrue(registration.TryUnregister(), "Removing again is harmless.");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(testRoot, throwOnMissingSubKey: false);
        }
    }
}
