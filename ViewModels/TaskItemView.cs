using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Services;
using Humanizer;
using System;
using System.Globalization;
using System.IO;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class TaskQueueItemViewModel
{
    public void RefreshThemeBrushes()
    {
        UpdateResultBadgeBrushes();
        OnPropertyChanged(nameof(IntegrityForegroundBrush));
    }

    private void RefreshSourceMetadata()
    {
        string path = SourcePath;

        if (string.IsNullOrWhiteSpace(path))
        {
            ClearSourceMetadata();
            return;
        }

        try
        {
            string normalizedPath = Path.GetFullPath(path.Trim());

            FileName = Path.GetFileName(normalizedPath);
            InputType = Path.GetExtension(normalizedPath).TrimStart('.').ToUpperInvariant();
            SourceDirectoryDisplay = Path.GetDirectoryName(normalizedPath) ?? "-";
            ShortSourcePathDisplay = BuildShortPath(SourceDirectoryDisplay);
            FileSizeDisplay = BuildFileSizeDisplay(normalizedPath);
            OnPropertyChanged(nameof(FileTitleDisplay));
            OnPropertyChanged(nameof(FilePathSubtitleDisplay));
            OnPropertyChanged(nameof(SourceFilePath));
            OnPropertyChanged(nameof(SourceExtensionDisplay));
            OnPropertyChanged(nameof(QueueTitleDisplay));
            NotifyMetadataStripChanged();
        }
        catch (Exception ex) when (IsExpectedFileSizeException(ex))
        {
            ClearSourceMetadata();
        }
    }

    private void ClearSourceMetadata()
    {
        FileName = string.Empty;
        InputType = string.Empty;
        FileSizeDisplay = "-";
        SourceDirectoryDisplay = "-";
        ShortSourcePathDisplay = "-";
        OnPropertyChanged(nameof(FileTitleDisplay));
        OnPropertyChanged(nameof(FilePathSubtitleDisplay));
        OnPropertyChanged(nameof(SourceFilePath));
        OnPropertyChanged(nameof(SourceExtensionDisplay));
        OnPropertyChanged(nameof(QueueTitleDisplay));
        NotifyMetadataStripChanged();
    }

    private void NotifyMetadataStripChanged()
    {
        OnPropertyChanged(nameof(DisplaySourceFormat));
        OnPropertyChanged(nameof(DisplayTargetFormat));
        OnPropertyChanged(nameof(DisplayPlannedAction));
        OnPropertyChanged(nameof(DisplayAvailableOperationsText));
        OnPropertyChanged(nameof(SelectedOperationDisplay));
        OnPropertyChanged(nameof(HasConcreteProcessingPlan));
        NotifyUiCardLayoutProperties();
    }

    private string BuildDisplaySourceFormat()
    {
        if (string.IsNullOrWhiteSpace(SourcePath))
        {
            return MetaLoc("LocMeta_Source_Unknown");
        }

        string extension = Path.GetExtension(SourcePath).ToLowerInvariant();
        return extension switch
        {
            ".cue" => MetaLoc("LocMeta_SourceFormat_CueBin"),
            ".bin" or ".img" => "BIN",
            ".iso" => "ISO",
            ".chd" => "CHD",
            ".gdi" => "GDI",
            ".toc" => "TOC",
            ".nrg" => "NRG",
            ".zip" or ".rar" or ".7z" => MetaLoc("LocMeta_SourceFormat_Archive"),
            _ when string.IsNullOrWhiteSpace(extension) => MetaLoc("LocMeta_Source_Unknown"),
            _ => string.IsNullOrWhiteSpace(InputType) ? MetaLoc("LocMeta_Source_Unknown") : InputType
        };
    }

    private string BuildDisplayTargetFormat()
    {
        if (string.IsNullOrWhiteSpace(SourcePath))
        {
            return MetaLoc("LocMeta_Target_Dash");
        }

        if (RequestedAction is TaskActionCodes.PendingSelection or TaskActionCodes.Unsupported)
        {
            return MetaLoc("LocMeta_Target_Dash");
        }

        if (IsVerifyChdMetadataRun())
        {
            return MetaLoc("LocMeta_Target_Verify");
        }

        return RequestedAction switch
        {
            TaskActionCodes.ConvertToChd => MetaLoc("LocMeta_Target_ChD"),
            TaskActionCodes.StageArchiveForConversion => MetaLoc("LocMeta_Target_ChD"),
            TaskActionCodes.ExtractFromChd => MetaLoc("LocMeta_Target_IsoBin"),
            TaskActionCodes.VerifyChd => MetaLoc("LocMeta_Target_Verify"),
            _ => MetaLoc("LocMeta_Target_Dash")
        };
    }

    private string BuildDisplayPlannedAction()
    {
        if (string.IsNullOrWhiteSpace(SourcePath))
        {
            return MetaLoc("LocMeta_Planned_NotDetermined");
        }

        if (RequestedAction == TaskActionCodes.PendingSelection)
        {
            return MetaLoc("LocMeta_Planned_NotDetermined");
        }

        if (RequestedAction == TaskActionCodes.Unsupported)
        {
            return ArabicUi.GetActionArabicLabel(TaskActionCodes.Unsupported);
        }

        if (IsVerifyChdMetadataRun())
        {
            return ArabicUi.GetActionArabicLabel(TaskActionCodes.VerifyChd);
        }

        return RequestedAction switch
        {
            TaskActionCodes.ConvertToChd => ArabicUi.GetActionArabicLabel(TaskActionCodes.ConvertToChd),
            TaskActionCodes.StageArchiveForConversion => MetaLoc("LocMeta_Planned_ExtractArchiveThenConvert"),
            TaskActionCodes.ExtractFromChd => ArabicUi.GetActionArabicLabel(TaskActionCodes.ExtractFromChd),
            TaskActionCodes.VerifyChd => ArabicUi.GetActionArabicLabel(TaskActionCodes.VerifyChd),
            _ => MetaLoc("LocMeta_Planned_NotDetermined")
        };
    }

    private static string MetaLoc(string resourceKey)
    {
        string value = ArabicUi.Get(resourceKey);
        return string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

    private static string BuildShortPath(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || directoryPath == "-")
        {
            return "-";
        }

        try
        {
            string normalized = TrimMetadataDirectorySeparators(Path.GetFullPath(directoryPath.Replace('/', '\\')));
            string[] segments = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length <= 3)
            {
                return normalized;
            }

            return $"...\\{segments[^2]}\\{segments[^1]}";
        }
        catch (Exception ex) when (IsExpectedFileSizeException(ex))
        {
            return "-";
        }
    }

    private static string BuildFileSizeDisplay(string path)
    {
        try
        {
            if (!TryNormalizeExistingMetadataFilePath(path, out string safePath))
            {
                return "-";
            }

            long bytes = new FileInfo(safePath).Length;
            return bytes.Bytes().Humanize("0.##", CultureInfo.GetCultureInfo("en-US"));
        }
        catch (Exception ex) when (IsExpectedFileSizeException(ex))
        {
            return "-";
        }
    }

    private static bool TryNormalizeExistingMetadataFilePath(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path.Trim());

            if (!File.Exists(fullPath)
                || HasMetadataReparsePointInExistingPathFromVolumeRoot(fullPath))
            {
                return false;
            }

            ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(path));

            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (IsExpectedFileSizeException(ex))
        {
            return false;
        }
    }

    private static bool HasMetadataReparsePointInExistingPathFromVolumeRoot(string candidatePath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string? root = Path.GetPathRoot(candidate);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return HasMetadataReparsePointInExistingPath(candidate, root);
        }
        catch (Exception ex) when (IsExpectedFileSizeException(ex))
        {
            return true;
        }
    }

    private static bool HasMetadataReparsePointInExistingPath(string candidatePath, string rootPath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string root = Path.GetFullPath(rootPath);

            if (!IsMetadataSamePathOrChild(root, candidate))
            {
                return true;
            }

            string current = candidate;

            while (true)
            {
                if ((File.Exists(current) || Directory.Exists(current)) && IsMetadataReparsePoint(current))
                {
                    return true;
                }

                if (MetadataPathsEqual(current, root))
                {
                    return false;
                }

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent) || MetadataPathsEqual(parent, current))
                {
                    return true;
                }

                current = parent;
            }
        }
        catch (Exception ex) when (IsExpectedFileSizeException(ex))
        {
            return true;
        }
    }

    private static bool IsMetadataReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (IsExpectedFileSizeException(ex))
        {
            return true;
        }
    }

    private static bool IsMetadataSamePathOrChild(string rootPath, string candidatePath)
    {
        string root = TrimMetadataDirectorySeparators(Path.GetFullPath(rootPath));
        string candidate = TrimMetadataDirectorySeparators(Path.GetFullPath(candidatePath));

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(EnsureMetadataDirectorySeparatorSuffix(root), StringComparison.OrdinalIgnoreCase);
    }

    private static bool MetadataPathsEqual(string left, string right)
    {
        return string.Equals(
            TrimMetadataDirectorySeparators(Path.GetFullPath(left)),
            TrimMetadataDirectorySeparators(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureMetadataDirectorySeparatorSuffix(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string TrimMetadataDirectorySeparators(string path)
    {
        string? root = Path.GetPathRoot(path);

        if (!string.IsNullOrWhiteSpace(root)
            && path.Length <= root.Length)
        {
            return root;
        }

        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.IsNullOrEmpty(trimmed) && !string.IsNullOrWhiteSpace(root)
            ? root
            : trimmed;
    }

    private static bool IsExpectedFileSizeException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException;
    }

    private void UpdateResultBadgeBrushes()
    {
        (ResultBadgeBrush, ResultBadgeForegroundBrush) = FinalResult switch
        {
            TaskFinalResultCodes.Healthy or TaskFinalResultCodes.Moved or TaskFinalResultCodes.Extracted =>
                (ResolveBrush("SuccessBadgeBrush", 46, 46, 46), ResolveBrush("SuccessBadgeForegroundBrush", 207, 207, 207)),
            TaskFinalResultCodes.Failed
                or TaskFinalResultCodes.FailedConvert
                or TaskFinalResultCodes.FailedExtract
                or TaskFinalResultCodes.FailedVerify
                or TaskFinalResultCodes.SourceUnreadable =>
                (ResolveBrush("ErrorBadgeBrush", 253, 238, 238), ResolveBrush("ErrorBadgeForegroundBrush", 196, 43, 28)),
            TaskFinalResultCodes.PasswordRequired =>
                (ResolveBrush("WarningBadgeBrush", 255, 244, 229), ResolveBrush("WarningBadgeForegroundBrush", 161, 92, 0)),
            TaskFinalResultCodes.SkippedExists =>
                (ResolveBrush("WarningBadgeBrush", 243, 244, 246), ResolveBrush("WarningBadgeForegroundBrush", 75, 85, 99)),
            TaskFinalResultCodes.Cancelled =>
                (ResolveBrush("ErrorBadgeBrush", 253, 238, 238), ResolveBrush("ErrorBadgeForegroundBrush", 196, 43, 28)),
            TaskFinalResultCodes.None =>
                (ResolveBrush("NeutralBadgeBrush", 243, 244, 246), ResolveBrush("NeutralBadgeForegroundBrush", 55, 65, 81)),
            _ => (ResolveBrush("ActiveBadgeBrush", 234, 243, 255), ResolveBrush("ActiveBadgeForegroundBrush", 15, 108, 189))
        };
    }

    private static MediaBrush ResolveBrush(string resourceKey, byte r, byte g, byte b)
    {
        if (System.Windows.Application.Current?.Resources[resourceKey] is MediaBrush brush)
        {
            return brush;
        }

        return CreateBrush(r, g, b);
    }

    private static MediaBrush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(MediaColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void NotifyUiCardLayoutProperties()
    {
        OnPropertyChanged(nameof(QueueCardBadgeTextIsolated));
        OnPropertyChanged(nameof(UiShowIdleOperationPicker));
        OnPropertyChanged(nameof(UiShowIdleStartRow));
        OnPropertyChanged(nameof(UiShowIdleSurface));
        OnPropertyChanged(nameof(UiShowProgressRow));
        OnPropertyChanged(nameof(UiShowRunningActionsRow));
        OnPropertyChanged(nameof(UiShowTerminalActionsRow));
        OnPropertyChanged(nameof(UiShowMetadataExtendedChips));
    }
}