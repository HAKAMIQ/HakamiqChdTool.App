using HakamiqChdTool.App.Services;
using System;
using System.IO;

namespace HakamiqChdTool.App.Core.Input;

public sealed class MediaInputClassifier : IMediaInputClassifier
{
    public static readonly MediaInputClassifier Shared = new();

    public MediaInputDescriptor Classify(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new MediaInputDescriptor(
                string.Empty,
                string.Empty,
                MediaInputKind.Unknown,
                string.Empty,
                Exists: false,
                IsDirectory: false,
                ChdWorkflowProfilePlanner.InvalidInputPathMessageKey);
        }

        string originalPath = path.Trim();
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(originalPath);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return new MediaInputDescriptor(
                originalPath,
                originalPath,
                MediaInputKind.Unknown,
                string.Empty,
                Exists: false,
                IsDirectory: false,
                ChdWorkflowProfilePlanner.InvalidInputPathMessageKey);
        }

        bool isDirectory = Directory.Exists(fullPath);
        bool isFile = !isDirectory && File.Exists(fullPath);

        if (isDirectory)
        {
            return new MediaInputDescriptor(
                originalPath,
                fullPath,
                MediaInputKind.Folder,
                string.Empty,
                Exists: true,
                IsDirectory: true,
                string.Empty);
        }

        string extension = ResolveExtension(fullPath);
        MediaInputKind kind = ResolveKind(extension);

        return new MediaInputDescriptor(
            originalPath,
            fullPath,
            kind,
            extension,
            Exists: isFile,
            IsDirectory: false,
            ResolveFailureMessageKey(kind, isFile));
    }

    private static string ResolveExtension(string fullPath)
    {
        try
        {
            return Path.GetExtension(fullPath).ToLowerInvariant();
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return string.Empty;
        }
    }

    private static MediaInputKind ResolveKind(string extension) => extension switch
    {
        ".iso" => MediaInputKind.ISO,
        ".pkg" => MediaInputKind.PKG,
        ".chd" => MediaInputKind.CHD,
        ".cso" => MediaInputKind.CSO,
        ".cue" => MediaInputKind.CUE,
        ".bin" => MediaInputKind.BIN,
        ".gdi" => MediaInputKind.GDI,
        "" => MediaInputKind.Unknown,
        _ => MediaInputKind.Other
    };

    private static string ResolveFailureMessageKey(MediaInputKind kind, bool isFile)
    {
        if (!isFile)
        {
            return ChdWorkflowProfilePlanner.InputFileNotFoundMessageKey;
        }

        return kind is MediaInputKind.Unknown or MediaInputKind.Other or MediaInputKind.PKG
            ? ChdWorkflowProfilePlanner.UnsupportedMessageKey
            : string.Empty;
    }

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or IOException
        or NotSupportedException
        or PathTooLongException
        or UnauthorizedAccessException
        or System.Security.SecurityException;
}
