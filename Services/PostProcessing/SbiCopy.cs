using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.IO;

namespace HakamiqChdTool.App.Services.PostProcessing;

public sealed class SbiArtifactCopier : ISbiArtifactCopier
{
    private const string SbiArtifactKind = "SBI";
    private const string SbiCopyFailedMessageCode = "LocPostProcessing_SbiCopyFailed";

    private readonly ILogger _logger;

    public SbiArtifactCopier()
        : this(Log.ForContext<SbiArtifactCopier>())
    {
    }

    internal SbiArtifactCopier(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public PostConversionArtifactResult CopyMatchingSbiIfExists(string workingInputPath, string outputChdPath)
    {
        try
        {
            return CopyMatchingSbiIfExistsCore(workingInputPath, outputChdPath);
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
            _logger.Warning(
                ex,
                "Post-conversion SBI artifact copy failed. Input={Input}; Output={Output}",
                workingInputPath,
                outputChdPath);

            return PostConversionArtifactResult.WithFailure(
                SbiArtifactKind,
                SbiCopyFailedMessageCode,
                ResolveFailureTarget(outputChdPath));
        }
    }

    private PostConversionArtifactResult CopyMatchingSbiIfExistsCore(string workingInputPath, string outputChdPath)
    {
        if (string.IsNullOrWhiteSpace(workingInputPath) || string.IsNullOrWhiteSpace(outputChdPath))
        {
            return PostConversionArtifactResult.Empty;
        }

        string workingInputFullPath = Path.GetFullPath(workingInputPath.Trim());
        if (!string.Equals(Path.GetExtension(workingInputFullPath), ".cue", StringComparison.OrdinalIgnoreCase))
        {
            return PostConversionArtifactResult.Empty;
        }

        if (!File.Exists(workingInputFullPath)
            || HasReparsePointInExistingPathFromVolumeRoot(workingInputFullPath))
        {
            throw new IOException();
        }

        string? workingDirectoryCandidate = Path.GetDirectoryName(workingInputFullPath);
        if (string.IsNullOrWhiteSpace(workingDirectoryCandidate))
        {
            return PostConversionArtifactResult.Empty;
        }

        string workingDirectory = Path.GetFullPath(workingDirectoryCandidate);
        if (!IsPathInsideDirectory(workingInputFullPath, workingDirectory)
            || HasReparsePointInExistingPathFromVolumeRoot(workingDirectory))
        {
            throw new IOException();
        }

        string? sourceSbiCandidate = Path.ChangeExtension(workingInputFullPath, ".sbi");
        if (string.IsNullOrWhiteSpace(sourceSbiCandidate))
        {
            return PostConversionArtifactResult.Empty;
        }

        string sourceSbi = Path.GetFullPath(sourceSbiCandidate);
        if (!IsPathInsideDirectory(sourceSbi, workingDirectory))
        {
            throw new IOException();
        }

        if (!File.Exists(sourceSbi))
        {
            return PostConversionArtifactResult.Empty;
        }

        if (HasReparsePointInExistingPath(sourceSbi, workingDirectory))
        {
            throw new IOException();
        }

        string outputChdFullPath = Path.GetFullPath(outputChdPath.Trim());
        if (!string.Equals(Path.GetExtension(outputChdFullPath), ".chd", StringComparison.OrdinalIgnoreCase))
        {
            return PostConversionArtifactResult.Empty;
        }

        if (!File.Exists(outputChdFullPath)
            || HasReparsePointInExistingPathFromVolumeRoot(outputChdFullPath))
        {
            throw new IOException();
        }

        string? outputDirectoryCandidate = Path.GetDirectoryName(outputChdFullPath);
        if (string.IsNullOrWhiteSpace(outputDirectoryCandidate))
        {
            return PostConversionArtifactResult.Empty;
        }

        string outputDirectory = Path.GetFullPath(outputDirectoryCandidate);
        if (!TryNormalizeExistingSafeDirectory(outputDirectory, out string safeOutputDirectory))
        {
            throw new IOException();
        }

        string? destinationSbiCandidate = Path.ChangeExtension(outputChdFullPath, ".sbi");
        if (string.IsNullOrWhiteSpace(destinationSbiCandidate))
        {
            return PostConversionArtifactResult.Empty;
        }

        string destinationSbi = Path.GetFullPath(destinationSbiCandidate);
        if (!IsPathInsideDirectory(destinationSbi, safeOutputDirectory))
        {
            throw new IOException();
        }

        CopyFileAtomically(sourceSbi, destinationSbi, safeOutputDirectory);

        _logger.Information(
            "Post-conversion SBI artifact copied. Source={SourceSbi}; Destination={DestinationSbi}",
            sourceSbi,
            destinationSbi);

        return new PostConversionArtifactResult { SbiCopiedCount = 1 };
    }

    private static void CopyFileAtomically(
        string sourcePath,
        string destinationPath,
        string destinationDirectory)
    {
        string fullSourcePath = Path.GetFullPath(sourcePath);
        string fullDestinationDirectory = Path.GetFullPath(destinationDirectory);
        string fullDestinationPath = Path.GetFullPath(destinationPath);

        if (!File.Exists(fullSourcePath)
            || HasReparsePointInExistingPathFromVolumeRoot(fullSourcePath)
            || !TryNormalizeExistingSafeDirectory(fullDestinationDirectory, out string safeDestinationDirectory)
            || !IsPathInsideDirectory(fullDestinationPath, safeDestinationDirectory))
        {
            throw new IOException();
        }

        if (File.Exists(fullDestinationPath)
            && HasReparsePointInExistingPath(fullDestinationPath, safeDestinationDirectory))
        {
            throw new IOException();
        }

        string destinationFileName = Path.GetFileName(fullDestinationPath);
        if (string.IsNullOrWhiteSpace(destinationFileName))
        {
            throw new IOException();
        }

        string tempPath = Path.Combine(
            safeDestinationDirectory,
            $"{destinationFileName}.{Guid.NewGuid():N}.tmp");

        if (!IsPathInsideDirectory(tempPath, safeDestinationDirectory))
        {
            throw new IOException();
        }

        try
        {
            File.Copy(fullSourcePath, tempPath, overwrite: false);

            if (HasReparsePointInExistingPath(tempPath, safeDestinationDirectory)
                || File.Exists(fullDestinationPath)
                    && HasReparsePointInExistingPath(fullDestinationPath, safeDestinationDirectory))
            {
                throw new IOException();
            }

            File.Move(tempPath, fullDestinationPath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private static string? ResolveFailureTarget(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
            return null;
        }
    }

    private static bool TryNormalizeExistingSafeDirectory(string path, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path.Trim());

            if (!Directory.Exists(fullPath)
                || IsUnsafeRoot(fullPath)
                || HasReparsePointInExistingPathFromVolumeRoot(fullPath))
            {
                return false;
            }

            normalized = fullPath;
            return true;
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
            return false;
        }
    }

    private static bool IsPathInsideDirectory(string fullPath, string directory)
    {
        string normalizedDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        string normalizedPath = Path.GetFullPath(fullPath);

        return normalizedPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
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
        catch (Exception ex) when (IsPathOrIoFailure(ex))
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
        catch (Exception ex) when (IsPathOrIoFailure(ex))
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
        catch (Exception ex) when (IsPathOrIoFailure(ex))
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
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
            return true;
        }
    }

    private static string TrimDirectorySeparators(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath) && !HasReparsePointInExistingPathFromVolumeRoot(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
        }
    }

    private static bool IsPathOrIoFailure(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or InvalidOperationException
            or System.Security.SecurityException;
    }
}
