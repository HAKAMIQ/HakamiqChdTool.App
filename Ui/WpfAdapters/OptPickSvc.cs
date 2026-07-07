using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Services;
using Microsoft.Win32;
using Ookii.Dialogs.Wpf;
using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace HakamiqChdTool.App.Ui.WpfAdapters;

public sealed class OptionsPickerService : IOptionsPickerService
{
    public string? PickFolder(string titleKey, string? selectedPath)
    {
        var dialog = new VistaFolderBrowserDialog
        {
            Description = ArabicUi.Get(titleKey),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = TryGetSafeExistingDirectory(selectedPath, out string safeSelectedPath)
                ? safeSelectedPath
                : null
        };

        Window? owner = GetActiveOwner();
        bool? result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);

        if (result != true || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return null;
        }

        return TryNormalizeSafeFolderSelection(dialog.SelectedPath, out string normalizedSelection)
            ? normalizedSelection
            : null;
    }

    public string? PickFile(string titleKey, string filterKey, string? currentPath, string fallbackDirectory)
    {
        string current = currentPath?.Trim() ?? string.Empty;
        string initialDirectory = ResolveInitialDirectory(current, fallbackDirectory);

        var dialog = new OpenFileDialog
        {
            Title = ArabicUi.Get(titleKey),
            Filter = ArabicUi.Get(filterKey),
            FileName = TryGetSafeExistingFile(current, out string safeCurrentFile)
                ? Path.GetFileName(safeCurrentFile)
                : string.Empty,
            InitialDirectory = initialDirectory
        };

        Window? owner = GetActiveOwner();
        bool? result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);

        if (result != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return null;
        }

        return TryNormalizeSafeFileSelection(dialog.FileName, out string normalizedSelection)
            ? normalizedSelection
            : null;
    }

    private static string ResolveInitialDirectory(string currentPath, string fallbackDirectory)
    {
        if (TryGetSafeExistingFile(currentPath, out string safeCurrentFile))
        {
            string? directory = Path.GetDirectoryName(safeCurrentFile);
            if (TryGetSafeExistingDirectory(directory, out string safeCurrentDirectory))
            {
                return safeCurrentDirectory;
            }
        }

        if (TryGetSafeExistingDirectory(fallbackDirectory, out string safeFallbackDirectory))
        {
            return safeFallbackDirectory;
        }

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (TryGetSafeExistingDirectory(documents, out string safeDocumentsDirectory))
        {
            return safeDocumentsDirectory;
        }

        return AppContext.BaseDirectory;
    }

    private static bool TryNormalizeSafeFolderSelection(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path.Trim());

            if (HasReparsePointInExistingPathFromVolumeRoot(fullPath))
            {
                return false;
            }

            ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(path));

            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }
    }

    private static bool TryNormalizeSafeFileSelection(string? path, out string normalizedPath)
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
                || HasReparsePointInExistingPathFromVolumeRoot(fullPath))
            {
                return false;
            }

            ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(path));

            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }
    }

    private static bool TryGetSafeExistingFile(string? path, out string normalizedPath)
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
                || HasReparsePointInExistingPathFromVolumeRoot(fullPath))
            {
                return false;
            }

            ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(path));

            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }
    }

    private static bool TryGetSafeExistingDirectory(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path.Trim());

            if (!Directory.Exists(fullPath)
                || HasReparsePointInExistingPathFromVolumeRoot(fullPath))
            {
                return false;
            }

            ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(path));

            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }
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
        catch (Exception ex) when (IsExpectedPathException(ex))
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

            if (!IsSamePathOrChild(root, candidate))
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
        catch (Exception ex) when (IsExpectedPathException(ex))
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
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return true;
        }
    }

    private static bool IsSamePathOrChild(string rootPath, string candidatePath)
    {
        string root = TrimDirectorySeparators(Path.GetFullPath(rootPath));
        string candidate = TrimDirectorySeparators(Path.GetFullPath(candidatePath));

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(EnsureDirectorySeparatorSuffix(root), StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            TrimDirectorySeparators(Path.GetFullPath(left)),
            TrimDirectorySeparators(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
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

    private static Window? GetActiveOwner()
    {
        System.Windows.Application? application = System.Windows.Application.Current;
        if (application is null)
        {
            return null;
        }

        return application.Windows
            .OfType<Window>()
            .FirstOrDefault(static window => window.IsActive && window.IsVisible)
            ?? (application.MainWindow?.IsVisible == true ? application.MainWindow : null);
    }

    private static bool IsExpectedPathException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException;
    }
}