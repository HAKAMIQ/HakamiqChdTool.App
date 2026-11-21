using System.IO;
using System.Security.Cryptography;

namespace HakamiqChdTool.App.Services.Xbox;

/// <summary>The bundled Xbox tools.</summary>
internal enum XboxTool
{
    Xdvdfs,
    Iso2God,
    ZArchive
}

/// <summary>
/// Finds the bundled Xbox tools. Each one has a fixed path under Tools and a pinned SHA-256; there is no user override.
/// The check runs again immediately before every start (see <see cref="ExternalToolProcessRunner"/>), so a file
/// replaced after the app started is refused.
/// </summary>
internal static class XboxToolLocator
{
    public const string XdvdfsSha256 = "DF5F19954EF706C130C256546BF67CF7725611BDB30D1815577187007A6F907A";
    public const string Iso2GodSha256 = "1459FCF8FF5A89E039B02C68C06B006B7A036F3B1CA237C2D7E03D61BD211D47";
    public const string ZArchiveSha256 = "F9BBA11DF7EE120186E013214C14C210690F72AB2940ECC158FC40BF36E78B6C";

    private static readonly byte[] XdvdfsDigest = Convert.FromHexString(XdvdfsSha256);
    private static readonly byte[] Iso2GodDigest = Convert.FromHexString(Iso2GodSha256);
    private static readonly byte[] ZArchiveDigest = Convert.FromHexString(ZArchiveSha256);

    public static string ExecutableName(XboxTool tool) => tool switch
    {
        XboxTool.Xdvdfs => "xdvdfs.exe",
        XboxTool.Iso2God => "iso2god.exe",
        XboxTool.ZArchive => "zarchive.exe",
        _ => throw new ArgumentOutOfRangeException(nameof(tool))
    };

    public static string BundledPath(XboxTool tool, string? appDirectory = null) => Path.GetFullPath(Path.Combine(
        appDirectory ?? AppContext.BaseDirectory,
        "Tools",
        tool switch
        {
            XboxTool.Xdvdfs => "xdvdfs",
            XboxTool.Iso2God => "iso2god",
            XboxTool.ZArchive => "zarchive",
            _ => throw new ArgumentOutOfRangeException(nameof(tool))
        },
        "win-x64",
        ExecutableName(tool)));

    /// <summary>True when the bundled tool exists, is not reached through a reparse point and matches its pinned digest.</summary>
    public static bool TryLocate(XboxTool tool, out string path, string? appDirectory = null)
    {
        path = BundledPath(tool, appDirectory);
        if (TryValidate(tool, path, out string normalized, appDirectory))
        {
            path = normalized;
            return true;
        }

        return false;
    }

    public static bool TryValidate(XboxTool tool, string candidatePath, out string normalized, string? appDirectory = null)
    {
        normalized = string.Empty;
        try
        {
            if (!string.Equals(Path.GetFullPath(candidatePath), BundledPath(tool, appDirectory), StringComparison.OrdinalIgnoreCase)
                || !ExternalToolPathValidator.TryValidateCandidate(candidatePath, ExecutableName(tool), out string fullPath))
            {
                return false;
            }

            byte[] expected = tool switch
            {
                XboxTool.Xdvdfs => XdvdfsDigest,
                XboxTool.Iso2God => Iso2GodDigest,
                _ => ZArchiveDigest
            };

            using FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(stream), expected))
            {
                return false;
            }

            normalized = fullPath;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
