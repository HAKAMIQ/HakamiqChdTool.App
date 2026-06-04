using HakamiqChdTool.App.Localization;
using Microsoft.Win32;
using Ookii.Dialogs.Wpf;
using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace HakamiqChdTool.App.Services;

public sealed class WpfAdvancedOptionsPickerService : IAdvancedOptionsPickerService
{
    public string? PickFolder(string titleKey, string? selectedPath)
    {
        var dialog = new VistaFolderBrowserDialog
        {
            Description = ArabicUi.Get(titleKey),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(selectedPath) ? selectedPath : null
        };

        Window? owner = GetActiveOwner();
        bool? result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return result == true ? dialog.SelectedPath ?? string.Empty : null;
    }

    public string? PickFile(string titleKey, string filterKey, string? currentPath, string fallbackDirectory)
    {
        string current = currentPath?.Trim() ?? string.Empty;
        string initialDirectory = ResolveInitialDirectory(current, fallbackDirectory);

        var dialog = new OpenFileDialog
        {
            Title = ArabicUi.Get(titleKey),
            Filter = ArabicUi.Get(filterKey),
            FileName = File.Exists(current) ? Path.GetFileName(current) : string.Empty,
            InitialDirectory = initialDirectory
        };

        Window? owner = GetActiveOwner();
        bool? result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return result == true ? dialog.FileName : null;
    }

    private static string ResolveInitialDirectory(string currentPath, string fallbackDirectory)
    {
        if (File.Exists(currentPath))
        {
            string? directory = Path.GetDirectoryName(currentPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                return directory;
            }
        }

        if (!string.IsNullOrWhiteSpace(fallbackDirectory) && Directory.Exists(fallbackDirectory))
        {
            return fallbackDirectory;
        }

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Directory.Exists(documents) ? documents : AppContext.BaseDirectory;
    }

    private static Window? GetActiveOwner()
    {
        Application? application = Application.Current;
        if (application is null)
        {
            return null;
        }

        return application.Windows
            .OfType<Window>()
            .FirstOrDefault(static window => window.IsActive && window.IsVisible)
            ?? (application.MainWindow?.IsVisible == true ? application.MainWindow : null);
    }
}