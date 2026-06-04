using HakamiqChdTool.App.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace HakamiqChdTool.App.Services.BinCueRescue;

internal static class CueRescueWriter
{
    private const string RescueWorkspaceFolderName = "BinCueRescue";
    private const string GeneratedCueFileName = "rescued.cue";

    private static readonly string[] CueLineSeparators =
    [
        "\r\n",
        "\n"
    ];

    internal static CueRescueWriteResult Write(
        BinCueRescuePlan? plan,
        string? processTempRoot = null,
        CancellationToken cancellationToken = default)
    {
        if (plan is null)
        {
            return CueRescueWriteResult.Fail(
                CueRescueWriteFailureReason.PlanIsNull);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (plan.IsRefused)
        {
            return CueRescueWriteResult.Fail(
                CueRescueWriteFailureReason.PlanNotUsable);
        }

        if (plan.IsAmbiguous)
        {
            return CueRescueWriteResult.Fail(
                CueRescueWriteFailureReason.AmbiguousPlan);
        }

        if (plan.Decision != BinCueRescueDecision.GenerateTempCue || !plan.CanGenerateTempCue)
        {
            return CueRescueWriteResult.Fail(
                CueRescueWriteFailureReason.PlanNotUsable);
        }

        if (plan.OrderedTracks.Count == 0)
        {
            return CueRescueWriteResult.Fail(
                CueRescueWriteFailureReason.EmptyPlan);
        }

        if (!TryResolveProcessTempRoot(processTempRoot, out string resolvedProcessTempRoot))
        {
            return CueRescueWriteResult.Fail(
                CueRescueWriteFailureReason.UnsafeTempRoot);
        }

        string workspacePath;
        try
        {
            workspacePath = CreateRescueWorkspace(resolvedProcessTempRoot);
        }
        catch (Exception ex) when (IsIoFailure(ex) || IsPathFailure(ex))
        {
            return CueRescueWriteResult.Fail(
                CueRescueWriteFailureReason.CouldNotCreateWorkspace);
        }

        if (!IsPathUnderDirectory(workspacePath, resolvedProcessTempRoot)
            || HasReparsePointInExistingPathFromVolumeRoot(workspacePath))
        {
            TryDeleteDirectory(workspacePath);

            return CueRescueWriteResult.Fail(
                CueRescueWriteFailureReason.UnsafeTempRoot);
        }

        try
        {
            if (!TryPrepareTracks(plan, out List<PreparedCueTrack> preparedTracks, out CueRescueWriteFailureReason prepareFailureReason))
            {
                TryDeleteDirectory(workspacePath);

                return CueRescueWriteResult.Fail(
                    prepareFailureReason);
            }

            List<StagedCueTrack> stagedTracks = StageTrackFiles(
                preparedTracks,
                workspacePath,
                cancellationToken);

            string cueText = BuildCueText(stagedTracks);
            if (!IsCueContentSafe(cueText))
            {
                TryDeleteDirectory(workspacePath);

                return CueRescueWriteResult.Fail(
                    CueRescueWriteFailureReason.InvalidCueContent);
            }

            string cuePath = Path.Combine(workspacePath, GeneratedCueFileName);
            if (!IsPathUnderDirectory(cuePath, workspacePath))
            {
                TryDeleteDirectory(workspacePath);

                return CueRescueWriteResult.Fail(
                    CueRescueWriteFailureReason.UnsafeTempRoot);
            }

            AtomicWriteNewFile(cuePath, cueText, cancellationToken);

            if (!IsPathUnderDirectory(workspacePath, resolvedProcessTempRoot)
                || HasReparsePointInExistingPathFromVolumeRoot(workspacePath))
            {
                TryDeleteDirectory(workspacePath);

                return CueRescueWriteResult.Fail(
                    CueRescueWriteFailureReason.UnsafeTempRoot);
            }

            return CueRescueWriteResult.Success(
                cuePath,
                workspacePath,
                stagedTracks.Count);
        }
        catch (OperationCanceledException)
        {
            TryDeleteDirectory(workspacePath);
            throw;
        }
        catch (Exception ex) when (IsIoFailure(ex) || IsPathFailure(ex))
        {
            TryDeleteDirectory(workspacePath);

            return CueRescueWriteResult.Fail(
                CueRescueWriteFailureReason.AtomicWriteFailed);
        }
    }

    private static bool TryPrepareTracks(
        BinCueRescuePlan plan,
        out List<PreparedCueTrack> preparedTracks,
        out CueRescueWriteFailureReason failureReason)
    {
        preparedTracks = [];
        failureReason = CueRescueWriteFailureReason.None;

        HashSet<int> usedTrackNumbers = [];

        for (int i = 0; i < plan.OrderedTracks.Count; i++)
        {
            BinCueRescueTrackPlan track = plan.OrderedTracks[i];

            if (track.TrackNumber < 1 || track.TrackNumber > 99 || !usedTrackNumbers.Add(track.TrackNumber))
            {
                failureReason = CueRescueWriteFailureReason.UnsupportedTrack;
                preparedTracks.Clear();
                return false;
            }

            if (string.IsNullOrWhiteSpace(track.SourceBinPath))
            {
                failureReason = CueRescueWriteFailureReason.MissingTrackFile;
                preparedTracks.Clear();
                return false;
            }

            string fullSourcePath;
            try
            {
                fullSourcePath = NormalizeFullPath(track.SourceBinPath);
            }
            catch (Exception ex) when (IsPathFailure(ex) || IsIoFailure(ex))
            {
                failureReason = CueRescueWriteFailureReason.MissingTrackFile;
                preparedTracks.Clear();
                return false;
            }

            if (!File.Exists(fullSourcePath)
                || HasReparsePointInExistingPathFromVolumeRoot(fullSourcePath)
                || !string.Equals(Path.GetExtension(fullSourcePath), ".bin", StringComparison.OrdinalIgnoreCase))
            {
                failureReason = CueRescueWriteFailureReason.MissingTrackFile;
                preparedTracks.Clear();
                return false;
            }

            string cueTrackMode = NormalizeCueTrackMode(track.CueTrackMode);
            if (cueTrackMode.Length == 0)
            {
                failureReason = CueRescueWriteFailureReason.UnsupportedTrack;
                preparedTracks.Clear();
                return false;
            }

            preparedTracks.Add(new PreparedCueTrack(
                track.TrackNumber,
                fullSourcePath,
                cueTrackMode));
        }

        return true;
    }

    private static string NormalizeCueTrackMode(string cueTrackMode)
    {
        if (string.IsNullOrWhiteSpace(cueTrackMode))
        {
            return string.Empty;
        }

        string normalized = cueTrackMode.Trim().ToUpperInvariant();

        return normalized switch
        {
            "MODE1/2352" => "MODE1/2352",
            "MODE2/2352" => "MODE2/2352",
            "AUDIO" => "AUDIO",
            _ => string.Empty
        };
    }

    private static List<StagedCueTrack> StageTrackFiles(
        IReadOnlyList<PreparedCueTrack> preparedTracks,
        string workspacePath,
        CancellationToken cancellationToken)
    {
        List<StagedCueTrack> stagedTracks = [];
        HashSet<string> usedFileNames = new(StringComparer.OrdinalIgnoreCase);

        foreach (PreparedCueTrack track in preparedTracks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string relativeFileName = CreateSafeRelativeTrackFileName(
                track.SourcePath,
                track.TrackNumber,
                usedFileNames);

            string destinationPath = Path.Combine(workspacePath, relativeFileName);
            if (!IsPathUnderDirectory(destinationPath, workspacePath)
                || HasReparsePointInExistingPathFromVolumeRoot(track.SourcePath))
            {
                throw new IOException();
            }

            if (!TryStageTrackHardLink(
                    destinationPath,
                    track.SourcePath,
                    workspacePath,
                    out string cueFilePath))
            {
                cueFilePath = CreateSafeAbsoluteCueFilePath(track.SourcePath);
            }

            stagedTracks.Add(new StagedCueTrack(
                track.TrackNumber,
                cueFilePath,
                track.CueMode));
        }

        return stagedTracks;
    }

    private static bool TryStageTrackHardLink(
        string destinationPath,
        string sourcePath,
        string workspacePath,
        out string cueFilePath)
    {
        cueFilePath = string.Empty;

        try
        {
            if (File.Exists(destinationPath))
            {
                return false;
            }

            if (!TryCreateHardLink(destinationPath, sourcePath)
                || !File.Exists(destinationPath)
                || HasReparsePointInExistingPath(destinationPath, workspacePath))
            {
                TryDeleteFile(destinationPath);
                return false;
            }

            string relativePath = Path.GetRelativePath(workspacePath, destinationPath)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');

            if (!IsCueFilePathSafe(relativePath, allowRootedPath: false))
            {
                TryDeleteFile(destinationPath);
                return false;
            }

            cueFilePath = relativePath;
            return true;
        }
        catch (Exception ex) when (IsIoFailure(ex) || IsPathFailure(ex))
        {
            TryDeleteFile(destinationPath);
            return false;
        }
    }

    private static string CreateSafeAbsoluteCueFilePath(string sourcePath)
    {
        string fullSourcePath = NormalizeFullPath(sourcePath);

        if (!File.Exists(fullSourcePath)
            || HasReparsePointInExistingPathFromVolumeRoot(fullSourcePath))
        {
            throw new IOException();
        }

        string cuePath = fullSourcePath
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

        if (!IsCueFilePathSafe(cuePath, allowRootedPath: true))
        {
            throw new IOException();
        }

        return cuePath;
    }

    private static string BuildCueText(IReadOnlyList<StagedCueTrack> stagedTracks)
    {
        StringBuilder builder = new();

        foreach (StagedCueTrack track in stagedTracks)
        {
            string quotedPath = EscapeCueQuotedText(track.CueFilePath);

            builder.Append("FILE \"");
            builder.Append(quotedPath);
            builder.AppendLine("\" BINARY");

            builder.Append("  TRACK ");
            builder.Append(track.TrackNumber.ToString("00", CultureInfo.InvariantCulture));
            builder.Append(' ');
            builder.AppendLine(track.CueMode);

            builder.AppendLine("    INDEX 01 00:00:00");
        }

        return builder.ToString();
    }

    private static bool IsCueContentSafe(string cueText)
    {
        if (string.IsNullOrWhiteSpace(cueText))
        {
            return false;
        }

        if (cueText.Contains('\0', StringComparison.Ordinal))
        {
            return false;
        }

        string[] lines = cueText.Split(CueLineSeparators, StringSplitOptions.None);
        foreach (string line in lines)
        {
            if (!line.StartsWith("FILE \"", StringComparison.Ordinal))
            {
                continue;
            }

            int firstQuote = line.IndexOf('"');
            int lastQuote = line.LastIndexOf('"');
            if (firstQuote < 0 || lastQuote <= firstQuote)
            {
                return false;
            }

            string cueFilePath = line.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
            if (!IsCueFilePathSafe(cueFilePath, allowRootedPath: true))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsCueFilePathSafe(
        string cueFilePath,
        bool allowRootedPath)
    {
        if (string.IsNullOrWhiteSpace(cueFilePath))
        {
            return false;
        }

        if (cueFilePath.IndexOfAny(['\0', '\r', '\n', '"']) >= 0)
        {
            return false;
        }

        if (Path.IsPathRooted(cueFilePath) && !allowRootedPath)
        {
            return false;
        }

        if (ContainsParentTraversalSegment(cueFilePath))
        {
            return false;
        }

        return true;
    }

    private static bool ContainsParentTraversalSegment(string path)
    {
        string[] segments = path.Split(
            ['/', '\\'],
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

    private static void AtomicWriteNewFile(
        string cuePath,
        string cueText,
        CancellationToken cancellationToken)
    {
        string directoryPath = Path.GetDirectoryName(cuePath)
            ?? throw new IOException();

        Directory.CreateDirectory(directoryPath);

        if (HasReparsePointInExistingPathFromVolumeRoot(directoryPath))
        {
            throw new IOException();
        }

        string tmpCuePath = Path.Combine(
            directoryPath,
            $"{Path.GetFileName(cuePath)}.{Guid.NewGuid():N}.tmp");

        if (!IsPathUnderDirectory(tmpCuePath, directoryPath))
        {
            throw new IOException();
        }

        try
        {
            using (FileStream stream = new(
                tmpCuePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.WriteThrough))
            {
                byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                    .GetBytes(cueText);

                cancellationToken.ThrowIfCancellationRequested();
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(cuePath) || HasReparsePointInExistingPath(tmpCuePath, directoryPath))
            {
                throw new IOException();
            }

            File.Move(tmpCuePath, cuePath, overwrite: false);
        }
        finally
        {
            if (File.Exists(tmpCuePath))
            {
                TryDeleteFile(tmpCuePath);
            }
        }
    }

    private static bool TryResolveProcessTempRoot(
        string? explicitProcessTempRoot,
        out string resolvedProcessTempRoot)
    {
        resolvedProcessTempRoot = string.Empty;

        string candidate = string.IsNullOrWhiteSpace(explicitProcessTempRoot)
            ? AppPaths.ProcessTempRoot
            : explicitProcessTempRoot;

        string fullCandidate;

        try
        {
            fullCandidate = NormalizeFullPath(candidate);
        }
        catch (Exception ex) when (IsPathFailure(ex) || IsIoFailure(ex))
        {
            return false;
        }

        if (IsRootDirectory(fullCandidate))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(fullCandidate);
        }
        catch (Exception ex) when (IsPathFailure(ex) || IsIoFailure(ex))
        {
            return false;
        }

        if (HasReparsePointInExistingPathFromVolumeRoot(fullCandidate))
        {
            return false;
        }

        resolvedProcessTempRoot = fullCandidate;
        return true;
    }

    private static string CreateRescueWorkspace(string processTempRoot)
    {
        string workspaceRoot = Path.Combine(processTempRoot, RescueWorkspaceFolderName);
        Directory.CreateDirectory(workspaceRoot);

        if (!IsPathUnderDirectory(workspaceRoot, processTempRoot)
            || HasReparsePointInExistingPathFromVolumeRoot(workspaceRoot))
        {
            throw new IOException();
        }

        for (int attempt = 0; attempt < 20; attempt++)
        {
            string workspacePath = Path.Combine(
                workspaceRoot,
                $"rescue_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");

            if (Directory.Exists(workspacePath))
            {
                continue;
            }

            Directory.CreateDirectory(workspacePath);

            if (!IsPathUnderDirectory(workspacePath, processTempRoot)
                || HasReparsePointInExistingPathFromVolumeRoot(workspacePath))
            {
                TryDeleteDirectory(workspacePath);
                throw new IOException();
            }

            return workspacePath;
        }

        throw new IOException();
    }

    private static string CreateSafeRelativeTrackFileName(
        string sourcePath,
        int trackNumber,
        HashSet<string> usedFileNames)
    {
        string fileName = Path.GetFileName(sourcePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"track{trackNumber:00}.bin";
        }

        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidChar, '_');
        }

        fileName = fileName.Replace('"', '_').Trim();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"track{trackNumber:00}.bin";
        }

        string baseName = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".bin";
        }

        string candidate = fileName;
        if (usedFileNames.Add(candidate))
        {
            return candidate;
        }

        candidate = $"track{trackNumber:00}_{baseName}{extension}";
        if (usedFileNames.Add(candidate))
        {
            return candidate;
        }

        for (int i = 1; i <= 999; i++)
        {
            candidate = $"track{trackNumber:00}_{i:000}_{baseName}{extension}";
            if (usedFileNames.Add(candidate))
            {
                return candidate;
            }
        }

        throw new IOException();
    }

    private static string EscapeCueQuotedText(string text)
    {
        return text.Replace("\\", "/").Replace("\"", "_");
    }

    private static bool TryCreateHardLink(
        string destinationPath,
        string sourcePath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        try
        {
            return CreateHardLinkW(destinationPath, sourcePath, IntPtr.Zero);
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (IsIoFailure(ex) || IsPathFailure(ex))
        {
            return false;
        }
    }

    private static bool IsPathUnderDirectory(
        string childPath,
        string parentDirectory)
    {
        string fullChild = NormalizeFullPath(childPath);
        string fullParent = NormalizeFullPath(parentDirectory);

        if (string.Equals(fullChild, fullParent, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string fullParentWithSeparator = EnsureDirectorySeparatorSuffix(fullParent);

        return fullChild.StartsWith(fullParentWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasReparsePointInExistingPathFromVolumeRoot(string candidatePath)
    {
        try
        {
            string candidate = NormalizeFullPath(candidatePath);
            string? root = Path.GetPathRoot(candidate);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return HasReparsePointInExistingPath(candidate, root);
        }
        catch (Exception ex) when (IsIoFailure(ex) || IsPathFailure(ex))
        {
            return true;
        }
    }

    private static bool HasReparsePointInExistingPath(string candidatePath, string rootPath)
    {
        try
        {
            string candidate = NormalizeFullPath(candidatePath);
            string root = NormalizeFullPath(rootPath);

            if (!IsSamePathOrChild(candidate, root))
            {
                return true;
            }

            string current = candidate;

            while (true)
            {
                if ((File.Exists(current) || Directory.Exists(current)) && IsExistingPathReparsePoint(current))
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

                current = NormalizeFullPath(parent);
            }
        }
        catch (Exception ex) when (IsIoFailure(ex) || IsPathFailure(ex))
        {
            return true;
        }
    }

    private static bool IsExistingPathReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (IsIoFailure(ex) || IsPathFailure(ex))
        {
            return true;
        }
    }

    private static bool IsSamePathOrChild(string candidatePath, string rootPath)
    {
        string candidate = NormalizeFullPath(candidatePath);
        string root = NormalizeFullPath(rootPath);

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(EnsureDirectorySeparatorSuffix(root), StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            NormalizeFullPath(left),
            NormalizeFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRootDirectory(string path)
    {
        string fullPath = NormalizeFullPath(path);
        string? root = Path.GetPathRoot(fullPath);

        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        string fullRoot = NormalizeFullPath(root);
        return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFullPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);

        if (!string.IsNullOrWhiteSpace(root)
            && fullPath.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }

    private static string EnsureDirectorySeparatorSuffix(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
               || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
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
            or UnauthorizedAccessException;
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
            {
                return;
            }

            if (IsExistingPathReparsePoint(directoryPath))
            {
                TryDeleteDirectoryLinkOnly(directoryPath);
                return;
            }

            foreach (string file in Directory.EnumerateFiles(directoryPath))
            {
                TryDeleteFile(file);
            }

            foreach (string childDirectory in Directory.EnumerateDirectories(directoryPath))
            {
                if (IsExistingPathReparsePoint(childDirectory))
                {
                    TryDeleteDirectoryLinkOnly(childDirectory);
                    continue;
                }

                TryDeleteDirectory(childDirectory);
            }

            ClearBlockingDirectoryAttributes(directoryPath);
            Directory.Delete(directoryPath, recursive: false);
        }
        catch (Exception ex) when (IsIoFailure(ex) || IsPathFailure(ex))
        {
        }
    }

    private static void TryDeleteDirectoryLinkOnly(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: false);
            }
        }
        catch (Exception ex) when (IsIoFailure(ex) || IsPathFailure(ex))
        {
        }
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            File.Delete(filePath);
        }
        catch (Exception ex) when (IsIoFailure(ex) || IsPathFailure(ex))
        {
        }
    }

    private static void ClearBlockingDirectoryAttributes(string directoryPath)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
            {
                return;
            }

            if (IsExistingPathReparsePoint(directoryPath))
            {
                return;
            }

            FileAttributes attributes = File.GetAttributes(directoryPath);
            FileAttributes cleaned = attributes
                & ~FileAttributes.ReadOnly
                & ~FileAttributes.Hidden
                & ~FileAttributes.System;

            if ((cleaned & FileAttributes.Directory) != FileAttributes.Directory)
            {
                cleaned |= FileAttributes.Directory;
            }

            if (cleaned != attributes)
            {
                File.SetAttributes(directoryPath, cleaned);
            }
        }
        catch (Exception ex) when (IsIoFailure(ex) || IsPathFailure(ex))
        {
        }
    }

#pragma warning disable SYSLIB1054
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);
#pragma warning restore SYSLIB1054

    private sealed record PreparedCueTrack(
        int TrackNumber,
        string SourcePath,
        string CueMode);

    private sealed record StagedCueTrack(
        int TrackNumber,
        string CueFilePath,
        string CueMode);
}