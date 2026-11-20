using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using HakamiqChdTool.App.Services;

namespace HakamiqChdTool.App.Services.ExternalTools.Igir;

public sealed class IgirToolLocator
{
    public const string Version = "5.8.1";
    public const string ExecutableSha256 = "F3BCD0173C5E79A3DCEC8CC65707007035849920AB362B51F09E133BDBB63DD1";
    private const string RelativePath = "Tools/igir/win-x64/igir.exe";

    public async Task<string?> FindVerifiedAsync(string applicationDirectory, CancellationToken cancellationToken = default) =>
        await FindVerifiedAsync(applicationDirectory, string.Empty, cancellationToken).ConfigureAwait(false);

    public async Task<string?> FindVerifiedAsync(string applicationDirectory, string? preferredPath, CancellationToken cancellationToken = default)
    {
        string bundledPath = Path.GetFullPath(Path.Combine(applicationDirectory, RelativePath));
        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            string? preferred = await VerifyAsync(Path.GetFullPath(preferredPath.Trim()), cancellationToken).ConfigureAwait(false);
            if (preferred is not null)
            {
                return preferred;
            }

            // A chosen file that is not the pinned build must not also switch off the bundled igir.
        }

        return await VerifyAsync(bundledPath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>True when the file at <paramref name="path"/> is the pinned igir build.</summary>
    public async Task<bool> IsPinnedBuildAsync(string path, CancellationToken cancellationToken = default) =>
        !string.IsNullOrWhiteSpace(path)
        && await VerifyAsync(Path.GetFullPath(path.Trim()), cancellationToken).ConfigureAwait(false) is not null;

    private static async Task<string?> VerifyAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        if (!ExternalToolPathValidator.TryValidateCandidate(path, "igir.exe", out string normalized))
            return null;
        await using FileStream stream = new(normalized, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] actual = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        byte[] expected = Convert.FromHexString(ExecutableSha256);
        return CryptographicOperations.FixedTimeEquals(actual, expected) ? normalized : null;
    }
}
