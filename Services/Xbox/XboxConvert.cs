using System.IO;
using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace HakamiqChdTool.App.Services.Xbox;

internal enum XboxSourceKind
{
    Unsupported,
    XboxIso,
    XboxFolder,
    Xbox360Iso,
    Xbox360Folder
}

/// <summary>The format an Xbox 360 game is converted to. Xbox games always become XISO.</summary>
internal enum Xbox360Format
{
    God,
    Zar
}

/// <summary>Reads and writes the saved Xbox 360 format (AppSettings.Xbox360Format).</summary>
internal static class XboxFormats
{
    public const string God = "god";
    public const string Zar = "zar";

    /// <summary>"zar" selects ZAR; anything else, including an unknown or empty value, is Games on Demand.</summary>
    public static Xbox360Format Parse(string? value) =>
        string.Equals(value?.Trim(), Zar, StringComparison.OrdinalIgnoreCase) ? Xbox360Format.Zar : Xbox360Format.God;

    public static string ToSetting(Xbox360Format format) => format == Xbox360Format.Zar ? Zar : God;
}

internal enum XboxConvertOutcome
{
    Done,
    Skipped,
    Failed,
    Cancelled
}

internal sealed record XboxConvertResult(
    XboxConvertOutcome Outcome,
    string MessageKey,
    string? OutputPath = null,
    long SourceBytes = 0,
    long OutputBytes = 0);

/// <summary>
/// Converts Xbox games to XISO (xdvdfs) and Xbox 360 games to Games on Demand (iso2god) or ZAR (zarchive). Every
/// result is checked before it is kept: XISO by content checksum (or by unpacking and comparing every file), GOD by its
/// container header, ZAR by unpacking it again and comparing every file. Work happens in a temporary file or folder in
/// the output folder; the source is never written to or deleted, and an existing output is never replaced.
/// </summary>
internal sealed class XboxConvertService
{
    public const string DoneKey = "LocXbox_Done";
    public const string ExistsKey = "LocXbox_OutputExists";
    public const string NotXboxKey = "LocXbox_NotXbox";
    public const string OutputFolderKey = "LocXbox_OutputFolderInvalid";
    public const string ToolMissingKey = "LocXbox_ToolMissing";
    public const string ToolFailedKey = "LocXbox_ToolFailed";
    public const string CheckFailedKey = "LocXbox_CheckFailed";
    public const string CancelledKey = "LocXbox_Cancelled";
    public const string FailedKey = "LocXbox_Failed";

    private const int ToolOutputLimit = 64 * 1024;
    private static readonly byte[] XboxMediaMagic = "MICROSOFT*XBOX*MEDIA"u8.ToArray();
    private static readonly long[] XdvdfsOffsets = [0x10000, 0x18310000, 0xFDA0000, 0x2090000];

    private readonly ExternalToolProcessRunner _runner;
    private readonly string? _appDirectory;

    public XboxConvertService(ExternalToolProcessRunner runner, string? appDirectory = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _appDirectory = appDirectory;
    }

    /// <summary>Xbox or Xbox 360 by the game's start file: default.xbe (Xbox) or default.xex (Xbox 360).</summary>
    public async Task<XboxSourceKind> DetectAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (Directory.Exists(path))
            {
                return File.Exists(Path.Combine(path, "default.xex")) ? XboxSourceKind.Xbox360Folder
                    : File.Exists(Path.Combine(path, "default.xbe")) ? XboxSourceKind.XboxFolder
                    : XboxSourceKind.Unsupported;
            }

            if (!File.Exists(path) || !HasXdvdfsSignature(path))
            {
                return XboxSourceKind.Unsupported;
            }

            ExternalToolProcessResult listing = await RunAsync(XboxTool.Xdvdfs, ["ls", path], cancellationToken).ConfigureAwait(false);
            if (listing.ExitCode != 0)
            {
                return XboxSourceKind.Unsupported;
            }

            string[] names = listing.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool HasRootFile(string name) => names.Any(line => line.TrimStart('/').Equals(name, StringComparison.OrdinalIgnoreCase));
            return HasRootFile("default.xex") ? XboxSourceKind.Xbox360Iso
                : HasRootFile("default.xbe") ? XboxSourceKind.XboxIso
                : XboxSourceKind.Unsupported;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return XboxSourceKind.Unsupported;
        }
    }

    public async Task<XboxConvertResult> ConvertAsync(
        string source,
        string outputFolder,
        Xbox360Format format,
        CancellationToken cancellationToken)
    {
        var temporary = new List<string>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            XboxSourceKind kind = await DetectAsync(source, cancellationToken).ConfigureAwait(false);
            if (kind == XboxSourceKind.Unsupported)
            {
                return new XboxConvertResult(XboxConvertOutcome.Failed, NotXboxKey);
            }

            if (!IsUsableOutputFolder(source, outputFolder))
            {
                return new XboxConvertResult(XboxConvertOutcome.Failed, OutputFolderKey);
            }

            long sourceBytes = SizeOf(source);
            string name = Directory.Exists(source)
                ? new DirectoryInfo(source).Name
                : Path.GetFileNameWithoutExtension(source);

            XboxConvertResult result = kind switch
            {
                XboxSourceKind.XboxIso or XboxSourceKind.XboxFolder =>
                    await ToXisoAsync(source, Path.Combine(outputFolder, name + ".iso"), outputFolder, temporary, cancellationToken).ConfigureAwait(false),
                _ when format == Xbox360Format.Zar =>
                    await ToZarAsync(source, Path.Combine(outputFolder, name + ".zar"), outputFolder, temporary, cancellationToken).ConfigureAwait(false),
                _ =>
                    await ToGodAsync(source, outputFolder, temporary, cancellationToken).ConfigureAwait(false)
            };

            return result with { SourceBytes = sourceBytes };
        }
        catch (OperationCanceledException)
        {
            return new XboxConvertResult(XboxConvertOutcome.Cancelled, CancelledKey);
        }
        catch (InvalidOperationException ex) when (ex.Message == ToolMissingKey)
        {
            return new XboxConvertResult(XboxConvertOutcome.Failed, ToolMissingKey);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            Log.Warning(ex, "Xbox conversion failed. Source={Source}", source);
            return new XboxConvertResult(XboxConvertOutcome.Failed, FailedKey);
        }
        finally
        {
            foreach (string path in temporary)
            {
                TryDelete(path);
            }
        }
    }

    private async Task<XboxConvertResult> ToXisoAsync(string source, string final, string outputFolder, List<string> temporary, CancellationToken ct)
    {
        if (File.Exists(final) || Directory.Exists(final))
        {
            return new XboxConvertResult(XboxConvertOutcome.Skipped, ExistsKey, final);
        }

        string image = Temporary(outputFolder, ".iso", temporary);
        if (await RunAsync(XboxTool.Xdvdfs, ["pack", source, image], ct).ConfigureAwait(false) is { ExitCode: not 0 })
        {
            return new XboxConvertResult(XboxConvertOutcome.Failed, ToolFailedKey);
        }

        if (!HasSignatureAt(image, 0x10000))
        {
            return new XboxConvertResult(XboxConvertOutcome.Failed, CheckFailedKey);
        }

        bool same;
        if (Directory.Exists(source))
        {
            // checksum reads images only: unpack the new XISO and compare every file with the folder.
            string check = Temporary(outputFolder, string.Empty, temporary);
            if (await RunAsync(XboxTool.Xdvdfs, ["unpack", image, check], ct).ConfigureAwait(false) is { ExitCode: not 0 })
            {
                return new XboxConvertResult(XboxConvertOutcome.Failed, CheckFailedKey);
            }

            same = SameFiles(source, check, ct);
        }
        else
        {
            string? before = await ChecksumAsync(source, ct).ConfigureAwait(false);
            string? after = await ChecksumAsync(image, ct).ConfigureAwait(false);
            same = before is not null && string.Equals(before, after, StringComparison.OrdinalIgnoreCase);
        }

        if (!same)
        {
            return new XboxConvertResult(XboxConvertOutcome.Failed, CheckFailedKey);
        }

        File.Move(image, final, overwrite: false);
        return new XboxConvertResult(XboxConvertOutcome.Done, DoneKey, final, OutputBytes: new FileInfo(final).Length);
    }

    private async Task<XboxConvertResult> ToZarAsync(string source, string final, string outputFolder, List<string> temporary, CancellationToken ct)
    {
        if (File.Exists(final) || Directory.Exists(final))
        {
            return new XboxConvertResult(XboxConvertOutcome.Skipped, ExistsKey, final);
        }

        string folder = source;
        if (!Directory.Exists(source))
        {
            folder = Temporary(outputFolder, string.Empty, temporary);
            if (await RunAsync(XboxTool.Xdvdfs, ["unpack", source, folder], ct).ConfigureAwait(false) is { ExitCode: not 0 })
            {
                return new XboxConvertResult(XboxConvertOutcome.Failed, ToolFailedKey);
            }
        }

        string archive = Temporary(outputFolder, ".zar", temporary);
        if (await RunAsync(XboxTool.ZArchive, [folder, archive], ct).ConfigureAwait(false) is { ExitCode: not 0 } || !File.Exists(archive))
        {
            return new XboxConvertResult(XboxConvertOutcome.Failed, ToolFailedKey);
        }

        string check = Temporary(outputFolder, string.Empty, temporary);
        if (await RunAsync(XboxTool.ZArchive, [archive, check], ct).ConfigureAwait(false) is { ExitCode: not 0 }
            || !SameFiles(folder, check, ct))
        {
            return new XboxConvertResult(XboxConvertOutcome.Failed, CheckFailedKey);
        }

        File.Move(archive, final, overwrite: false);
        return new XboxConvertResult(XboxConvertOutcome.Done, DoneKey, final, OutputBytes: new FileInfo(final).Length);
    }

    private async Task<XboxConvertResult> ToGodAsync(string source, string outputFolder, List<string> temporary, CancellationToken ct)
    {
        string image = source;
        if (Directory.Exists(source))
        {
            // iso2god reads an image, so a game folder is packed to a temporary XISO first.
            image = Temporary(outputFolder, ".iso", temporary);
            if (await RunAsync(XboxTool.Xdvdfs, ["pack", source, image], ct).ConfigureAwait(false) is { ExitCode: not 0 })
            {
                return new XboxConvertResult(XboxConvertOutcome.Failed, ToolFailedKey);
            }
        }

        string work = Temporary(outputFolder, string.Empty, temporary);
        if (await RunAsync(XboxTool.Iso2God, ["-j", "1", image, work], ct).ConfigureAwait(false) is { ExitCode: not 0 })
        {
            return new XboxConvertResult(XboxConvertOutcome.Failed, ToolFailedKey);
        }

        if (!TryReadGod(work, out string titleId, out string container))
        {
            return new XboxConvertResult(XboxConvertOutcome.Failed, CheckFailedKey);
        }

        string finalContainer = Path.Combine(outputFolder, titleId, "00007000", Path.GetFileName(container));
        if (File.Exists(finalContainer) || Directory.Exists(finalContainer + ".data"))
        {
            return new XboxConvertResult(XboxConvertOutcome.Skipped, ExistsKey, Path.Combine(outputFolder, titleId));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(finalContainer)!);
        Directory.Move(container + ".data", finalContainer + ".data");
        File.Move(container, finalContainer, overwrite: false);
        string finalTitle = Path.Combine(outputFolder, titleId);
        return new XboxConvertResult(XboxConvertOutcome.Done, DoneKey, finalTitle, OutputBytes: SizeOf(finalContainer) + SizeOf(finalContainer + ".data"));
    }

    /// <summary>One title folder (8 hex digits) with 00007000\&lt;container&gt; starting with "LIVE" and its .data folder.</summary>
    internal static bool TryReadGod(string work, out string titleId, out string container)
    {
        titleId = string.Empty;
        container = string.Empty;
        string[] titles = Directory.GetDirectories(work);
        if (titles.Length != 1 || !IsHex8(Path.GetFileName(titles[0])))
        {
            return false;
        }

        string content = Path.Combine(titles[0], "00007000");
        string[] files = Directory.Exists(content) ? Directory.GetFiles(content) : [];
        if (files.Length != 1 || !IsHex8(Path.GetFileName(files[0]))
            || !Directory.Exists(files[0] + ".data") || Directory.GetFiles(files[0] + ".data").Length == 0)
        {
            return false;
        }

        byte[] header = new byte[4];
        using (FileStream stream = File.OpenRead(files[0]))
        {
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length
                || !header.AsSpan().SequenceEqual("LIVE"u8))
            {
                return false;
            }
        }

        titleId = Path.GetFileName(titles[0]);
        container = files[0];
        return true;
    }

    private async Task<string?> ChecksumAsync(string image, CancellationToken ct)
    {
        ExternalToolProcessResult result = await RunAsync(XboxTool.Xdvdfs, ["checksum", image], ct).ConfigureAwait(false);
        string line = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;
        string hash = line.Split('\t', ' ')[0];
        return result.ExitCode == 0 && hash.Length == 64 ? hash : null;
    }

    private async Task<ExternalToolProcessResult> RunAsync(XboxTool tool, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!XboxToolLocator.TryLocate(tool, out string path, _appDirectory))
        {
            throw new InvalidOperationException(ToolMissingKey);
        }

        ExternalToolProcessResult result = await _runner.RunAsync(path, arguments, ct, ToolOutputLimit,
                candidate => XboxToolLocator.TryValidate(tool, candidate, out string normalized, _appDirectory) ? normalized : null)
            .ConfigureAwait(false);

        if (result.WasCancelled)
        {
            throw new OperationCanceledException(ct);
        }

        if (result.ExitCode != 0)
        {
            Log.Warning("Xbox tool {Tool} exited with {ExitCode}: {Error}", tool, result.ExitCode, result.StandardError);
        }

        return result;
    }

    private static bool IsUsableOutputFolder(string source, string outputFolder)
    {
        if (string.IsNullOrWhiteSpace(outputFolder) || !Directory.Exists(outputFolder))
        {
            return false;
        }

        string output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputFolder)) + Path.DirectorySeparatorChar;
        string from = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)) + Path.DirectorySeparatorChar;
        // A game folder must not be packed into itself.
        return !(Directory.Exists(source) && output.StartsWith(from, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasXdvdfsSignature(string path)
    {
        long size = new FileInfo(path).Length;
        return XdvdfsOffsets.Any(offset => offset + XboxMediaMagic.Length <= size && HasSignatureAt(path, offset));
    }

    private static bool HasSignatureAt(string path, long offset)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (offset + XboxMediaMagic.Length > stream.Length)
        {
            return false;
        }

        stream.Position = offset;
        byte[] buffer = new byte[XboxMediaMagic.Length];
        return stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length
            && buffer.AsSpan().SequenceEqual(XboxMediaMagic);
    }

    /// <summary>Same relative file names (case-insensitive) and the same SHA-256 for every file.</summary>
    internal static bool SameFiles(string expectedFolder, string actualFolder, CancellationToken ct)
    {
        Dictionary<string, string> Files(string root) => Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path), path => path, StringComparer.OrdinalIgnoreCase);

        Dictionary<string, string> expected = Files(expectedFolder);
        Dictionary<string, string> actual = Files(actualFolder);
        if (expected.Count != actual.Count)
        {
            return false;
        }

        foreach ((string relative, string path) in expected)
        {
            ct.ThrowIfCancellationRequested();
            if (!actual.TryGetValue(relative, out string? other) || new FileInfo(path).Length != new FileInfo(other).Length)
            {
                return false;
            }

            using FileStream a = File.OpenRead(path);
            using FileStream b = File.OpenRead(other);
            if (!SHA256.HashData(a).AsSpan().SequenceEqual(SHA256.HashData(b)))
            {
                return false;
            }
        }

        return true;
    }

    private static string Temporary(string outputFolder, string extension, List<string> temporary)
    {
        string path = Path.Combine(outputFolder, ".xbox-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant() + extension);
        temporary.Add(path);
        return path;
    }

    private static long SizeOf(string path) =>
        Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length)
            : File.Exists(path) ? new FileInfo(path).Length : 0;

    private static bool IsHex8(string name) => name.Length == 8 && name.All(Uri.IsHexDigit);

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not remove a temporary Xbox conversion file. Path={Path}", path);
        }
    }
}
