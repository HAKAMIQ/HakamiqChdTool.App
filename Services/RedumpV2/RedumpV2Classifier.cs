using HakamiqChdTool.App.Models.PlayStation.BluRayAnalysis;
using HakamiqChdTool.App.Services.PlayStation.BluRayAnalysis;
using Serilog;
using System.IO;
using System.Security;

namespace HakamiqChdTool.App.Services;

public sealed class RedumpV2Classifier
{
    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip",
        ".rar",
        ".7z"
    };

    private readonly BluRayIsoAnalysisService _bluRayIsoAnalysis;

    public RedumpV2Classifier()
        : this(new BluRayIsoAnalysisService())
    {
    }

    public RedumpV2Classifier(BluRayIsoAnalysisService bluRayIsoAnalysis)
    {
        _bluRayIsoAnalysis = bluRayIsoAnalysis
            ?? throw new ArgumentNullException(nameof(bluRayIsoAnalysis));
    }

    public RedumpSourceClassification Classify(
        string inputPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        cancellationToken.ThrowIfCancellationRequested();

        string fullPath = Path.GetFullPath(inputPath.Trim());

        if (Directory.Exists(fullPath))
        {
            return ClassifyDirectory(fullPath, cancellationToken);
        }

        if (!File.Exists(fullPath))
        {
            return new RedumpSourceClassification(
                fullPath,
                RedumpSourceFormat.Unknown,
                IsDirectory: false,
                Path.GetFileName(fullPath),
                SourceBytes: 0);
        }

        return ClassifyExistingFile(fullPath, cancellationToken);
    }

    private static RedumpSourceClassification ClassifyDirectory(
        string fullPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RedumpSourceFormat directoryFormat =
            LooksLikePs3JbFolder(fullPath, cancellationToken)
                ? RedumpSourceFormat.Ps3JbFolder
                : RedumpSourceFormat.Unknown;

        return new RedumpSourceClassification(
            fullPath,
            directoryFormat,
            IsDirectory: true,
            GetDirectoryDisplayName(fullPath),
            SourceBytes: 0);
    }

    private RedumpSourceClassification ClassifyExistingFile(
        string fullPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        FileInfo info = new(fullPath);
        string fileName = info.Name;
        string extension = info.Extension;

        RedumpSourceFormat format = ClassifyFile(
            fullPath,
            fileName,
            extension,
            cancellationToken);

        long sourceBytes = TryGetFileLength(info, fullPath);

        return new RedumpSourceClassification(
            fullPath,
            format,
            IsDirectory: false,
            fileName,
            sourceBytes);
    }

    private RedumpSourceFormat ClassifyFile(
        string fullPath,
        string fileName,
        string extension,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        /*
         * Archive must take precedence over NKit.
         *
         * Example:
         * game.nkit.iso.zip
         *
         * The source is an archive containing an NKit image,
         * not a directly readable NKit image.
         */
        if (ArchiveExtensions.Contains(extension))
        {
            return RedumpSourceFormat.Archive;
        }

        if (LooksLikeNkitFile(fileName, extension))
        {
            return RedumpSourceFormat.Nkit;
        }

        string normalizedExtension = extension.ToLowerInvariant();

        return normalizedExtension switch
        {
            ".iso" => LooksLikeDecryptedPs3Iso(fullPath, cancellationToken)
                ? RedumpSourceFormat.DecryptedPs3Iso
                : RedumpSourceFormat.Iso,

            ".gcm" => RedumpSourceFormat.Iso,

            ".cue" => RedumpSourceFormat.BinCue,
            ".bin" => RedumpSourceFormat.BinCue,
            ".img" => RedumpSourceFormat.BinCue,
            ".raw" => RedumpSourceFormat.BinCue,

            ".gdi" => RedumpSourceFormat.Gdi,
            ".chd" => RedumpSourceFormat.Chd,
            ".cso" => RedumpSourceFormat.Cso,
            ".rvz" => RedumpSourceFormat.Rvz,
            ".wbfs" => RedumpSourceFormat.Wbfs,

            _ => RedumpSourceFormat.Unknown
        };
    }

    private static bool LooksLikeNkitFile(
        string fileName,
        string extension)
    {
        return fileName.Contains(
                ".nkit.",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                extension,
                ".nkit",
                StringComparison.OrdinalIgnoreCase);
    }

    private bool LooksLikeDecryptedPs3Iso(
        string fullPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return _bluRayIsoAnalysis.TryAnalyze(
                    fullPath,
                    out BluRayIsoAnalysisResult? result,
                    BluRayAnalysisProfile.Quick,
                    cancellationToken)
                && result?.LooksLikePs3Disc == true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsExpectedProbeFailure(ex))
        {
            Log.Debug(
                ex,
                "Redump V2 PS3 ISO classification probe failed. Path={Path}",
                fullPath);

            return false;
        }
    }

    private static bool LooksLikePs3JbFolder(
        string directory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            string ps3GameDirectory = Path.Combine(
                directory,
                "PS3_GAME");

            if (!Directory.Exists(ps3GameDirectory))
            {
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();

            string rootDiscSfb = Path.Combine(
                directory,
                "PS3_DISC.SFB");

            if (File.Exists(rootDiscSfb))
            {
                return true;
            }

            cancellationToken.ThrowIfCancellationRequested();

            string paramSfo = Path.Combine(
                ps3GameDirectory,
                "PARAM.SFO");

            string userDirectory = Path.Combine(
                ps3GameDirectory,
                "USRDIR");

            /*
             * Some extracted PS3 layouts may not include PS3_DISC.SFB.
             * Requiring both PARAM.SFO and USRDIR prevents classifying an
             * arbitrary or empty PS3_GAME directory as a valid JB folder.
             */
            return File.Exists(paramSfo)
                && Directory.Exists(userDirectory);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsExpectedProbeFailure(ex))
        {
            Log.Debug(
                ex,
                "Redump V2 PS3 JB folder classification probe failed. Directory={Directory}",
                directory);

            return false;
        }
    }

    private static long TryGetFileLength(
        FileInfo info,
        string fullPath)
    {
        try
        {
            return info.Length;
        }
        catch (Exception ex) when (IsExpectedProbeFailure(ex))
        {
            /*
             * Classification can still remain useful when the file
             * disappears, becomes locked, or loses access between the
             * initial File.Exists check and FileInfo.Length.
             */
            Log.Debug(
                ex,
                "Redump V2 source length read failed. Path={Path}",
                fullPath);

            return 0;
        }
    }

    private static bool IsExpectedProbeFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or SecurityException;
    }

    private static string GetDirectoryDisplayName(string fullPath)
    {
        string trimmed = fullPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        string name = Path.GetFileName(trimmed);

        return string.IsNullOrWhiteSpace(name)
            ? trimmed
            : name;
    }
}