using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using System;
using System.IO;

namespace HakamiqChdTool.App.Core.Workflow;

internal static class WorkflowPendingPathPolicy
{
    private const int ArchiveExtractionFolderNameMaxLength = 24;
    private const int ArchiveExtractionSessionIdLength = 16;
    public static string BuildArchiveExtractionDirectory(string originalArchivePath)
    {
        string runtimeRoot = AppPaths.CombineProcessTemp("TempExtraction");
        string archiveName = BuildShortArchiveExtractionFolderName(originalArchivePath);
        string sessionId = Guid.NewGuid().ToString("N")[..ArchiveExtractionSessionIdLength];

        return Path.Combine(runtimeRoot, sessionId + "_" + archiveName);
    }

    public static void CopyMatchingSbiIfExists(string workingInputPath, string outputChdPath)
    {
        if (!string.Equals(Path.GetExtension(workingInputPath), ".cue", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string sourceSbi = Path.ChangeExtension(workingInputPath, ".sbi");

        if (!File.Exists(sourceSbi))
        {
            return;
        }

        string destinationSbi = Path.ChangeExtension(outputChdPath, ".sbi");
        string? destinationDirectory = Path.GetDirectoryName(destinationSbi);

        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        File.Copy(sourceSbi, destinationSbi, overwrite: true);
    }

    public static string SanitizePathSegment(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(value) ? "Item" : value;
    }

    private static string BuildShortArchiveExtractionFolderName(string originalArchivePath)
    {
        string archiveName = SanitizePathSegment(Path.GetFileNameWithoutExtension(originalArchivePath))
            .Replace(' ', '_');

        while (archiveName.Contains("__", StringComparison.Ordinal))
        {
            archiveName = archiveName.Replace("__", "_", StringComparison.Ordinal);
        }

        archiveName = archiveName.Trim('_', '.', ' ');

        if (archiveName.Length > ArchiveExtractionFolderNameMaxLength)
        {
            archiveName = archiveName[..ArchiveExtractionFolderNameMaxLength].Trim('_', '.', ' ');
        }

        return string.IsNullOrWhiteSpace(archiveName) ? "archive" : archiveName;
    }


    public static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    public static long TryGetFileSize(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    public static int MapProgressRange(int rawValue, int minimum, int maximum)
    {
        int clampedRaw = Math.Clamp(rawValue, 0, 100);

        if (maximum <= minimum)
        {
            return minimum;
        }

        double ratio = clampedRaw / 100.0;
        return minimum + (int)Math.Round((maximum - minimum) * ratio);
    }

    public static double MapProgressRange(int rawValue, double minimum, double maximum)
    {
        int clampedRaw = Math.Clamp(rawValue, 0, 100);

        if (maximum <= minimum)
        {
            return minimum;
        }

        double ratio = clampedRaw / 100.0;
        return minimum + ((maximum - minimum) * ratio);
    }

    public static string DetermineRequestedAction(string path) =>
        QueueItemOperationCatalog.GetInitialRequestedAction(path);


}
