using HakamiqChdTool.App.Models.PlayStation.BluRayAnalysis;
using HakamiqChdTool.App.Services.PlayStation.BluRayAnalysis;
using Serilog;
using System.IO;

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
        _bluRayIsoAnalysis = bluRayIsoAnalysis ?? throw new ArgumentNullException(nameof(bluRayIsoAnalysis));
    }

    public RedumpSourceClassification Classify(string inputPath, CancellationToken cancellationToken)
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

    private static RedumpSourceClassification ClassifyDirectory(string fullPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RedumpSourceFormat directoryFormat = LooksLikePs3JbFolder(fullPath)
            ? RedumpSourceFormat.Ps3JbFolder
            : RedumpSourceFormat.Unknown;

        return new RedumpSourceClassification(
            fullPath,
            directoryFormat,
            IsDirectory: true,
            GetDirectoryDisplayName(fullPath),
            SourceBytes: 0);
    }

    private RedumpSourceClassification ClassifyExistingFile(string fullPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        FileInfo info = new(fullPath);
        string fileName = info.Name;
        string extension = info.Extension;
        RedumpSourceFormat format = ClassifyFile(fullPath, fileName, extension, cancellationToken);

        return new RedumpSourceClassification(
            fullPath,
            format,
            IsDirectory: false,
            fileName,
            info.Length);
    }

    private RedumpSourceFormat ClassifyFile(
        string fullPath,
        string fileName,
        string extension,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (LooksLikeNkitFile(fileName, extension))
        {
            return RedumpSourceFormat.Nkit;
        }

        if (ArchiveExtensions.Contains(extension))
        {
            return RedumpSourceFormat.Archive;
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

    private static bool LooksLikeNkitFile(string fileName, string extension)
    {
        return fileName.Contains(".nkit.", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".nkit", StringComparison.OrdinalIgnoreCase);
    }

    private bool LooksLikeDecryptedPs3Iso(string fullPath, CancellationToken cancellationToken)
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
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException)
        {
            Log.Debug(ex, "Redump V2 PS3 ISO classification probe failed. Path={Path}", fullPath);
            return false;
        }
    }

    private static bool LooksLikePs3JbFolder(string directory)
    {
        try
        {
            string ps3Game = Path.Combine(directory, "PS3_GAME");
            string rootDiscSfb = Path.Combine(directory, "PS3_DISC.SFB");
            string ps3GameDiscSfb = Path.Combine(ps3Game, "PS3_DISC.SFB");

            return Directory.Exists(ps3Game)
                || File.Exists(rootDiscSfb)
                || File.Exists(ps3GameDiscSfb);
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException)
        {
            Log.Debug(ex, "Redump V2 PS3 JB folder classification probe failed. Directory={Directory}", directory);
            return false;
        }
    }

    private static string GetDirectoryDisplayName(string fullPath)
    {
        string trimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = Path.GetFileName(trimmed);

        return string.IsNullOrWhiteSpace(name)
            ? trimmed
            : name;
    }
}