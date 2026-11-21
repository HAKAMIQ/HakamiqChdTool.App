using System.IO;
using System.Security.Cryptography;
using HakamiqChdTool.App.Services.Xbox;

namespace HakamiqChdTool.App.Services.ExternalTools;

public enum BundledToolState
{
    Verified,
    Missing,
    Changed
}

/// <summary>One bundled tool as the Options window shows it.</summary>
public sealed record BundledToolStatus(string Name, string Version, string PurposeKey, BundledToolState State);

/// <summary>
/// Lists the tools shipped in Tools\ with their versions (Tools/VERSIONS.md) and checks each file against the SHA-256
/// the app pins before running it. Read only: nothing is run, moved or repaired here. CsoKit and igir have their own
/// rows in the Add-ons tab because they can be replaced by a chosen copy.
/// </summary>
public static class BundledToolsCheck
{
    internal sealed record ToolFile(string RelativePath, string Sha256);

    internal sealed record Tool(string Name, string Version, string PurposeKey, ToolFile[] Files);

    internal static readonly Tool[] Tools =
    [
        new("chdman", "0.289", "LocTools_ChdmanPurpose",
            [new(RuntimeToolService.BundledChdmanRelativePath, RuntimeToolService.BundledChdmanSha256Hex)]),
        new("7-Zip", "26.06", "LocTools_SevenZipPurpose",
            [new(Path.Combine("Tools", "7zip", "7z.exe"), SevenZipToolService.BundledSevenZipExeSha256),
             new(Path.Combine("Tools", "7zip", "7z.dll"), SevenZipToolService.BundledSevenZipDllSha256)]),
        new("xdvdfs", "0.8.3", "LocTools_XdvdfsPurpose",
            [new(Path.Combine("Tools", "xdvdfs", "win-x64", "xdvdfs.exe"), XboxToolLocator.XdvdfsSha256)]),
        new("iso2god", "1.8.1", "LocTools_Iso2GodPurpose",
            [new(Path.Combine("Tools", "iso2god", "win-x64", "iso2god.exe"), XboxToolLocator.Iso2GodSha256)]),
        new("ZArchive", "0.1.2", "LocTools_ZArchivePurpose",
            [new(Path.Combine("Tools", "zarchive", "win-x64", "zarchive.exe"), XboxToolLocator.ZArchiveSha256)])
    ];

    /// <summary>Hashes every bundled file; call it off the UI thread (about 30 MB is read).</summary>
    public static IReadOnlyList<BundledToolStatus> Check(string? appDirectory = null, CancellationToken cancellationToken = default)
    {
        string root = appDirectory ?? AppContext.BaseDirectory;
        var result = new List<BundledToolStatus>(Tools.Length);
        foreach (Tool tool in Tools)
        {
            BundledToolState state = BundledToolState.Verified;
            foreach (ToolFile file in tool.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BundledToolState fileState = CheckFile(Path.Combine(root, file.RelativePath), file.Sha256);
                if (fileState == BundledToolState.Missing || (fileState == BundledToolState.Changed && state == BundledToolState.Verified))
                {
                    state = fileState;
                }
            }

            result.Add(new BundledToolStatus(tool.Name, tool.Version, tool.PurposeKey, state));
        }

        return result;
    }

    private static BundledToolState CheckFile(string path, string expectedSha256)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return BundledToolState.Missing;
            }

            // A link could point anywhere; the runtime refuses it too.
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return BundledToolState.Changed;
            }

            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
            return CryptographicOperations.FixedTimeEquals(SHA256.HashData(stream), Convert.FromHexString(expectedSha256))
                ? BundledToolState.Verified
                : BundledToolState.Changed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return BundledToolState.Missing;
        }
    }
}
