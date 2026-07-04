using Serilog;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace HakamiqChdTool.App.Services.Integrity;

public sealed class Sha256FileIntegrityVerifier : IIntegrityVerifier
{
    public const string DefaultManifestRelativePath = "Integrity\\protected-features.manifest.json";

    private const string ExpectedManifestFormat = "HakamiqIntegrityManifest.v1";

    private static readonly ILogger Logger = Log.ForContext<Sha256FileIntegrityVerifier>();

    private readonly string _baseDirectory;
    private readonly IntegrityManifestReader _manifestReader;

    public Sha256FileIntegrityVerifier(string manifestPath, string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        _baseDirectory = Path.GetFullPath(baseDirectory);
        _manifestReader = new IntegrityManifestReader(manifestPath);
    }

    public static Sha256FileIntegrityVerifier CreateDefault(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        string manifestPath = Path.Combine(baseDirectory, DefaultManifestRelativePath);
        return new Sha256FileIntegrityVerifier(manifestPath, baseDirectory);
    }

    public IntegrityVerificationResult VerifyFile(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!TryNormalizeSafeBaseDirectory(_baseDirectory, out string? safeBaseDirectory))
        {
            return IntegrityVerificationResult.Failed(
                IntegrityVerificationStatus.VerificationFailed,
                _baseDirectory,
                null,
                null,
                null);
        }

        string fullFilePath;
        try
        {
            fullFilePath = Path.GetFullPath(filePath);
        }
        catch (Exception ex) when (IsPathFailure(ex))
        {
            return IntegrityVerificationResult.Failed(
                IntegrityVerificationStatus.VerificationFailed,
                safeBaseDirectory,
                null,
                null,
                null);
        }

        if (!TryGetRelativePathUnderBase(safeBaseDirectory, fullFilePath, out string? relativePath))
        {
            return IntegrityVerificationResult.Failed(
                IntegrityVerificationStatus.VerificationFailed,
                fullFilePath,
                null,
                null,
                null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(fullFilePath))
        {
            return IntegrityVerificationResult.Failed(
                IntegrityVerificationStatus.FileMissing,
                fullFilePath,
                relativePath,
                null,
                null);
        }

        if (HasReparsePointInExistingPath(fullFilePath, safeBaseDirectory))
        {
            return IntegrityVerificationResult.Failed(
                IntegrityVerificationStatus.VerificationFailed,
                fullFilePath,
                relativePath,
                null,
                null);
        }

        if (!_manifestReader.Exists)
        {
            return IntegrityVerificationResult.Failed(
                IntegrityVerificationStatus.ManifestMissing,
                fullFilePath,
                relativePath,
                null,
                null);
        }

        if (!TryValidateManifestPath(safeBaseDirectory, _manifestReader.ManifestPath))
        {
            return IntegrityVerificationResult.Failed(
                IntegrityVerificationStatus.ManifestInvalid,
                fullFilePath,
                relativePath,
                null,
                null);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            IntegrityManifest? manifest = _manifestReader.TryRead();
            if (manifest is null
                || !string.Equals(manifest.Format, ExpectedManifestFormat, StringComparison.Ordinal)
                || manifest.Files is not { Length: > 0 })
            {
                return IntegrityVerificationResult.Failed(
                    IntegrityVerificationStatus.ManifestInvalid,
                    fullFilePath,
                    relativePath,
                    null,
                    null);
            }

            IntegrityManifestEntry? entry = FindEntry(manifest, relativePath);
            if (entry is null)
            {
                return IntegrityVerificationResult.Failed(
                    IntegrityVerificationStatus.ManifestEntryMissing,
                    fullFilePath,
                    relativePath,
                    null,
                    null);
            }

            string expectedSha256 = NormalizeSha256(entry.Sha256);
            if (expectedSha256.Length != 64)
            {
                return IntegrityVerificationResult.Failed(
                    IntegrityVerificationStatus.ManifestInvalid,
                    fullFilePath,
                    relativePath,
                    entry.Sha256,
                    null);
            }

            string actualSha256 = ComputeSha256(fullFilePath, cancellationToken);
            if (!string.Equals(expectedSha256, actualSha256, StringComparison.OrdinalIgnoreCase))
            {
                return IntegrityVerificationResult.Failed(
                    IntegrityVerificationStatus.HashMismatch,
                    fullFilePath,
                    relativePath,
                    expectedSha256,
                    actualSha256);
            }

            return IntegrityVerificationResult.Passed(
                fullFilePath,
                relativePath,
                expectedSha256,
                actualSha256);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsVerificationFailure(ex) || ex is JsonException or CryptographicException)
        {
            Logger.Warning(
                ex,
                "File integrity verification failed. FilePath={FilePath}, ManifestPath={ManifestPath}",
                fullFilePath,
                _manifestReader.ManifestPath);

            return IntegrityVerificationResult.Failed(
                IntegrityVerificationStatus.VerificationFailed,
                fullFilePath,
                relativePath,
                null,
                null);
        }
    }

    private static IntegrityManifestEntry? FindEntry(
        IntegrityManifest manifest,
        string relativePath)
    {
        foreach (IntegrityManifestEntry? entry in manifest.Files)
        {
            if (entry is null)
            {
                continue;
            }

            string entryPath = NormalizeRelativePath(entry.Path);
            if (string.Equals(entryPath, relativePath, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    private static string ComputeSha256(
        string filePath,
        CancellationToken cancellationToken)
    {
        using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.SequentialScan);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        byte[] buffer = new byte[128 * 1024];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, read);
        }

        cancellationToken.ThrowIfCancellationRequested();

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static bool TryNormalizeSafeBaseDirectory(
        string baseDirectory,
        [NotNullWhen(true)] out string? safeBaseDirectory)
    {
        safeBaseDirectory = null;

        try
        {
            string fullBaseDirectory = Path.GetFullPath(baseDirectory);

            if (!Directory.Exists(fullBaseDirectory)
                || IsUnsafeRoot(fullBaseDirectory)
                || HasReparsePointInExistingPathFromVolumeRoot(fullBaseDirectory))
            {
                return false;
            }

            safeBaseDirectory = fullBaseDirectory;
            return true;
        }
        catch (Exception ex) when (IsPathFailure(ex) || IsIoFailure(ex))
        {
            return false;
        }
    }

    private static bool TryValidateManifestPath(
        string safeBaseDirectory,
        string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            return false;
        }

        try
        {
            string fullManifestPath = Path.GetFullPath(manifestPath);

            return File.Exists(fullManifestPath)
                   && TryGetRelativePathUnderBase(safeBaseDirectory, fullManifestPath, out _)
                   && !HasReparsePointInExistingPath(fullManifestPath, safeBaseDirectory);
        }
        catch (Exception ex) when (IsPathFailure(ex) || IsIoFailure(ex))
        {
            return false;
        }
    }

    private static bool TryGetRelativePathUnderBase(
        string baseDirectory,
        string fullFilePath,
        [NotNullWhen(true)] out string? relativePath)
    {
        relativePath = null;

        string fullBaseDirectory;
        string fullCandidatePath;

        try
        {
            fullBaseDirectory = Path.GetFullPath(baseDirectory);
            fullCandidatePath = Path.GetFullPath(fullFilePath);
        }
        catch (Exception ex) when (IsPathFailure(ex))
        {
            return false;
        }

        string rootedBase = EnsureDirectorySeparatorSuffix(fullBaseDirectory);
        if (!fullCandidatePath.StartsWith(rootedBase, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string candidateRelativePath = NormalizeRelativePath(Path.GetRelativePath(fullBaseDirectory, fullCandidatePath));
        if (string.IsNullOrWhiteSpace(candidateRelativePath)
            || Path.IsPathRooted(candidateRelativePath)
            || ContainsParentTraversalSegment(candidateRelativePath))
        {
            return false;
        }

        relativePath = candidateRelativePath;
        return true;
    }

    private static string NormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value.Trim().Replace(" ", string.Empty).ToUpperInvariant();

        if (normalized.Length != 64)
        {
            return string.Empty;
        }

        foreach (char c in normalized)
        {
            bool isHex =
                c is >= '0' and <= '9'
                || c is >= 'A' and <= 'F';

            if (!isHex)
            {
                return string.Empty;
            }
        }

        return normalized;
    }

    private static string NormalizeRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim()
            .Replace('\\', '/')
            .TrimStart('/');
    }

    private static bool ContainsParentTraversalSegment(string path)
    {
        string[] segments = path.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string segment in segments)
        {
            if (string.Equals(segment, "..", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasReparsePointInExistingPathFromVolumeRoot(string candidatePath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string? root = Path.GetPathRoot(candidate);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return HasReparsePointInExistingPath(candidate, root);
        }
        catch (Exception ex) when (IsPathFailure(ex) || IsIoFailure(ex))
        {
            return true;
        }
    }

    private static bool HasReparsePointInExistingPath(string candidatePath, string rootPath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string root = Path.GetFullPath(rootPath);

            if (!IsSamePathOrChild(candidate, root))
            {
                return true;
            }

            string current = candidate;

            while (true)
            {
                if ((File.Exists(current) || Directory.Exists(current)) && IsReparsePoint(current))
                {
                    return true;
                }

                if (PathsEqual(current, root))
                {
                    return false;
                }

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent) || PathsEqual(parent, current))
                {
                    return true;
                }

                current = parent;
            }
        }
        catch (Exception ex) when (IsPathFailure(ex) || IsIoFailure(ex))
        {
            return true;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (IsPathFailure(ex) || IsIoFailure(ex))
        {
            return true;
        }
    }

    private static bool IsSamePathOrChild(string candidatePath, string rootPath)
    {
        string candidate = TrimDirectorySeparators(Path.GetFullPath(candidatePath));
        string root = TrimDirectorySeparators(Path.GetFullPath(rootPath));

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            TrimDirectorySeparators(Path.GetFullPath(left)),
            TrimDirectorySeparators(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnsafeRoot(string path)
    {
        try
        {
            string fullPath = TrimDirectorySeparators(Path.GetFullPath(path));
            string? root = Path.GetPathRoot(fullPath);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return PathsEqual(fullPath, root);
        }
        catch (Exception ex) when (IsPathFailure(ex))
        {
            return true;
        }
    }

    private static string EnsureDirectorySeparatorSuffix(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
               || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string TrimDirectorySeparators(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsVerificationFailure(Exception ex)
    {
        return IsPathFailure(ex) || IsIoFailure(ex);
    }

    private static bool IsPathFailure(Exception ex)
    {
        return ex is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException;
    }

    private static bool IsIoFailure(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException;
    }
}