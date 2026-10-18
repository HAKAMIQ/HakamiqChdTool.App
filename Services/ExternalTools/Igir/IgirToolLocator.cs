using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using HakamiqChdTool.App.Services;

namespace HakamiqChdTool.App.Services.ExternalTools.Igir;

public sealed class IgirToolLocator
{
    public const string Version = "5.7.2";
    public const string ExecutableSha256 = "716ECBD7033081FC000A51FBD0588FEE6C1F1E95DAB1EFD6443324BC73597511";
    private const string RelativePath = "Tools/igir/win-x64/igir.exe";

    public async Task<string?> FindVerifiedAsync(string applicationDirectory, CancellationToken cancellationToken = default) =>
        await FindVerifiedAsync(applicationDirectory, string.Empty, cancellationToken).ConfigureAwait(false);

    public async Task<string?> FindVerifiedAsync(string applicationDirectory, string? preferredPath, CancellationToken cancellationToken = default)
    {
        string path = string.IsNullOrWhiteSpace(preferredPath)
            ? Path.GetFullPath(Path.Combine(applicationDirectory, RelativePath))
            : Path.GetFullPath(preferredPath.Trim());
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
