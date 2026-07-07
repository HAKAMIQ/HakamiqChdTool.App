using CommunityToolkit.Mvvm.Input;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Ui.WpfAdapters;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class OptionsViewModel
{
    private IOptionsPickerService? _optionsPickerService;
    private IRelayCommand? _browseOutputFolderCommand;
    private IRelayCommand? _browseExternalChdmanCommand;
    private IRelayCommand? _browseRedumpDatCommand;
    private IRelayCommand? _browseRedumpLocalLibraryCommand;
    private AsyncRelayCommand? _scanRedumpLocalLibraryCommand;
    private AsyncRelayCommand? _importRedumpLocalLibraryCommand;
    private IRelayCommand? _browsePendingWorkspaceCommand;
    private readonly RedumpLocalLibraryScanner _redumpLocalLibraryScanner = new();

    internal IOptionsPickerService OptionsPickerService
    {
        get => _optionsPickerService ??= new OptionsPickerService();
        set => _optionsPickerService = value ?? throw new ArgumentNullException(nameof(value));
    }

    public IRelayCommand BrowseOutputFolderCommand =>
        _browseOutputFolderCommand ??= new RelayCommand(BrowseOutputFolder);

    public IRelayCommand BrowseExternalChdmanCommand =>
        _browseExternalChdmanCommand ??= new RelayCommand(BrowseExternalChdman);

    public IRelayCommand BrowseRedumpDatCommand =>
        _browseRedumpDatCommand ??= new RelayCommand(BrowseRedumpDat);

    public IRelayCommand BrowseRedumpLocalLibraryCommand =>
        _browseRedumpLocalLibraryCommand ??= new RelayCommand(BrowseRedumpLocalLibrary);

    public IAsyncRelayCommand ScanRedumpLocalLibraryCommand =>
        _scanRedumpLocalLibraryCommand ??= new AsyncRelayCommand(
            ScanAndIndexRedumpLocalLibraryAsync,
            CanScanRedumpLocalLibrary);

    public IAsyncRelayCommand ImportRedumpLocalLibraryCommand =>
        _importRedumpLocalLibraryCommand ??= new AsyncRelayCommand(
            PrepareRedumpLocalLibraryDatabaseAsync,
            CanScanRedumpLocalLibrary);

    public IRelayCommand BrowsePendingWorkspaceCommand =>
        _browsePendingWorkspaceCommand ??= new RelayCommand(BrowsePendingWorkspace);

    private void BrowseOutputFolder()
    {
        string current = CustomOutputRoot?.Trim() ?? string.Empty;
        string? selected = OptionsPickerService.PickFolder(
            "LocAdv_Picker_SelectOutputFolderTitle",
            TryGetSafeOptionsDirectory(current, out string safeCurrent) ? safeCurrent : null);

        if (!string.IsNullOrWhiteSpace(selected))
        {
            CustomOutputRoot = selected;
        }
    }

    private void BrowseExternalChdman()
    {
        string current = ExternalChdmanPath?.Trim() ?? string.Empty;
        string fallback = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string? selected = OptionsPickerService.PickFile(
            "LocAdv_Picker_SelectExternalChdmanTitle",
            "LocFilter_ExecutableFiles",
            TryGetSafeOptionsFile(current, out string safeCurrent) ? safeCurrent : null,
            fallback);

        if (!string.IsNullOrWhiteSpace(selected))
        {
            ExternalChdmanPath = selected;
            UseBundledChdman = false;
            UseExternalChdman = true;
        }
    }

    private void BrowsePendingWorkspace()
    {
        string current = PendingWorkspaceCustomRoot?.Trim() ?? string.Empty;
        string fallback = Path.GetTempPath();

        string selectedPath = TryGetSafeOptionsDirectory(current, out string safeCurrent)
            ? safeCurrent
            : fallback;

        string? selected = OptionsPickerService.PickFolder(
            "LocAdv_Picker_SelectPendingWorkspaceTitle",
            selectedPath);

        if (!string.IsNullOrWhiteSpace(selected))
        {
            PendingWorkspaceCustomRoot = selected;
            UseCustomPendingWorkspace = true;
        }
    }

    private void BrowseRedumpDat()
    {
        string current = RedumpDatXmlPath?.Trim() ?? string.Empty;
        string fallback = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string? selected = OptionsPickerService.PickFile(
            "LocAdv_Picker_SelectRedumpDatTitle",
            "LocFilter_RedumpDatXmlFiles",
            TryGetSafeOptionsFile(current, out string safeCurrent) ? safeCurrent : null,
            fallback);

        if (!string.IsNullOrWhiteSpace(selected))
        {
            RedumpDatXmlPath = selected;
        }
    }

    private void BrowseRedumpLocalLibrary()
    {
        string current = RedumpLocalLibraryRoot?.Trim() ?? string.Empty;
        string fallback = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        string selectedPath = TryGetSafeOptionsDirectory(current, out string safeCurrent)
            ? safeCurrent
            : fallback;

        string? selected = OptionsPickerService.PickFolder(
            "LocAdv_Picker_SelectRedumpLocalFolderTitle",
            selectedPath);

        if (!string.IsNullOrWhiteSpace(selected))
        {
            RedumpLocalLibraryRoot = selected;
            RedumpLocalLibraryScanSummary = ArabicUi.Format(
                "LocRedumpSettings_LocalFolderSelectedFormat",
                RedumpCompactDisplayFormatter.FormatRoot(selected));
        }
    }

    private bool CanScanRedumpLocalLibrary()
    {
        string root = RedumpLocalLibraryRoot?.Trim() ?? string.Empty;

        return CanUseRedumpDatabaseImport
            && !IsRedumpLocalLibraryScanRunning
            && TryGetSafeOptionsDirectory(root, out _);
    }

    private async Task ScanRedumpLocalLibraryAsync()
    {
        string root = RedumpLocalLibraryRoot?.Trim() ?? string.Empty;

        if (!TryGetSafeOptionsDirectory(root, out string safeRoot))
        {
            RedumpLocalLibraryScanSummary = ArabicUi.Get("LocRedumpSettings_LocalFolderScanInvalid");
            return;
        }

        IsRedumpLocalLibraryScanRunning = true;
        RedumpLocalLibraryScanSummary = ArabicUi.Get("LocRedumpSettings_LocalFolderScanRunning");

        try
        {
            RedumpLocalLibraryScanResult result = await _redumpLocalLibraryScanner
                .ScanAsync(safeRoot, CancellationToken.None)
                .ConfigureAwait(true);

            if (!result.HasImportableDatFiles)
            {
                RedumpLocalLibraryScanSummary = ArabicUi.Get("LocRedumpSettings_LocalFolderScanInvalid");
                return;
            }

            string newest = result.NewestModifiedLocal.HasValue
                ? result.NewestModifiedLocal.Value.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)
                : "—";

            RedumpLocalLibraryScanSummary = ArabicUi.Format(
                "LocRedumpSettings_LocalFolderScanReadyFormat",
                FormatInvariantNumber(result.DatXmlFileCount),
                FormatInvariantNumber(result.CueFileCount),
                FormatInvariantNumber(result.GdiFileCount),
                FormatInvariantNumber(result.SubchannelFileCount),
                FormatInvariantNumber(result.DiscKeyFileCount),
                FormatInvariantNumber(result.TopLevelFolderCount),
                newest);
        }
        catch (Exception ex)
        {
            RedumpLocalLibraryScanSummary = RuntimeDiagnosticFormatter.SummarizeException(ex);
        }
        finally
        {
            IsRedumpLocalLibraryScanRunning = false;
        }
    }

    partial void NotifyRedumpLocalLibraryScanCommandState()
    {
        _scanRedumpLocalLibraryCommand?.NotifyCanExecuteChanged();
        _importRedumpLocalLibraryCommand?.NotifyCanExecuteChanged();
    }

    private static bool TryGetSafeOptionsFile(string? path, out string normalizedPath)
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
                || HasOptionsReparsePointInExistingPathFromVolumeRoot(fullPath))
            {
                return false;
            }

            ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(path));
            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (IsExpectedOptionsPathException(ex))
        {
            return false;
        }
    }

    private static bool TryGetSafeOptionsDirectory(string? path, out string normalizedPath)
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
                || HasOptionsReparsePointInExistingPathFromVolumeRoot(fullPath))
            {
                return false;
            }

            ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(path));
            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (IsExpectedOptionsPathException(ex))
        {
            return false;
        }
    }

    private static bool HasOptionsReparsePointInExistingPathFromVolumeRoot(string candidatePath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string? root = Path.GetPathRoot(candidate);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return HasOptionsReparsePointInExistingPath(candidate, root);
        }
        catch (Exception ex) when (IsExpectedOptionsPathException(ex))
        {
            return true;
        }
    }

    private static bool HasOptionsReparsePointInExistingPath(string candidatePath, string rootPath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string root = Path.GetFullPath(rootPath);

            if (!IsOptionsSamePathOrChild(root, candidate))
            {
                return true;
            }

            string current = candidate;

            while (true)
            {
                if ((File.Exists(current) || Directory.Exists(current)) && IsOptionsReparsePoint(current))
                {
                    return true;
                }

                if (OptionsPathsEqual(current, root))
                {
                    return false;
                }

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent) || OptionsPathsEqual(parent, current))
                {
                    return true;
                }

                current = parent;
            }
        }
        catch (Exception ex) when (IsExpectedOptionsPathException(ex))
        {
            return true;
        }
    }

    private static bool IsOptionsReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (IsExpectedOptionsPathException(ex))
        {
            return true;
        }
    }

    private static bool IsOptionsSamePathOrChild(string rootPath, string candidatePath)
    {
        string root = TrimOptionsDirectorySeparators(Path.GetFullPath(rootPath));
        string candidate = TrimOptionsDirectorySeparators(Path.GetFullPath(candidatePath));

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(EnsureOptionsDirectorySeparatorSuffix(root), StringComparison.OrdinalIgnoreCase);
    }

    private static bool OptionsPathsEqual(string left, string right)
    {
        return string.Equals(
            TrimOptionsDirectorySeparators(Path.GetFullPath(left)),
            TrimOptionsDirectorySeparators(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureOptionsDirectorySeparatorSuffix(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string TrimOptionsDirectorySeparators(string path)
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

    private static bool IsExpectedOptionsPathException(Exception ex)
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