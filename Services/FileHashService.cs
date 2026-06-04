using System.IO;
using System.Security.Cryptography;

namespace HakamiqChdTool.App.Services;

public enum FileHashAlgorithm
{
    MD5,
    SHA1,
    SHA256
}

public sealed record FileHashResult(
    string Path,
    FileHashAlgorithm Algorithm,
    string Hex,
    long BytesRead);

public static class FileHashService
{
    private const int BufferSize = 1024 * 1024;

    private const string InvalidPathMessageKey = "LocFileHash_InvalidPath";
    private const string FileNotFoundMessageKey = "LocFileHash_FileNotFound";
    private const string UnsupportedAlgorithmMessageKey = "LocFileHash_UnsupportedAlgorithm";

    public static async Task<FileHashResult> ComputeAsync(
        string path,
        FileHashAlgorithm algorithm = FileHashAlgorithm.SHA1,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(InvalidPathMessageKey, nameof(path));
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException(InvalidPathMessageKey, nameof(path), ex);
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(FileNotFoundMessageKey, fullPath);
        }

        using IncrementalHash hasher = CreateHasher(algorithm);

        await using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: BufferSize,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        byte[] buffer = new byte[BufferSize];
        long total = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int read = await stream
                .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                .ConfigureAwait(false);

            if (read <= 0)
            {
                break;
            }

            total += read;
            hasher.AppendData(buffer.AsSpan(0, read));
        }

        string hex = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        return new FileHashResult(fullPath, algorithm, hex, total);
    }

    private static IncrementalHash CreateHasher(FileHashAlgorithm algorithm)
    {
        HashAlgorithmName hashAlgorithmName = algorithm switch
        {
            FileHashAlgorithm.MD5 => HashAlgorithmName.MD5,
            FileHashAlgorithm.SHA1 => HashAlgorithmName.SHA1,
            FileHashAlgorithm.SHA256 => HashAlgorithmName.SHA256,
            _ => throw new ArgumentException(UnsupportedAlgorithmMessageKey, nameof(algorithm))
        };

        return IncrementalHash.CreateHash(hashAlgorithmName);
    }
}