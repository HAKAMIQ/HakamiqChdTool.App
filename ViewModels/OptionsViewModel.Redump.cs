using CommunityToolkit.Mvvm.ComponentModel;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.RedumpCatalog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class OptionsViewModel
{

    private void LoadRedumpCatalogChoices()
    {
        RedumpPlatformOptions.Clear();

        foreach (RedumpCatalogOption option in RedumpSystemCatalog.BuildPlatformOptions())
        {
            RedumpPlatformOptions.Add(RedumpCatalogChoiceOption.FromCatalogOption(option));
        }

        SelectedRedumpPlatformOption = RedumpPlatformOptions.FirstOrDefault();
    }


    private void RefreshRedumpArtifactOptions(string? platformKey)
    {
        RedumpArtifactOptions.Clear();

        foreach (RedumpCatalogOption option in RedumpSystemCatalog.BuildArtifactOptions(platformKey))
        {
            RedumpArtifactOptions.Add(RedumpCatalogChoiceOption.FromCatalogOption(option));
        }

        _selectedRedumpArtifactOption = RedumpArtifactOptions.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedRedumpArtifactOption));
        OnPropertyChanged(nameof(SelectedRedumpArtifactDescription));
    }


    private void UpdateRedumpDownloadUrlFromSelection(bool overwriteExistingCatalogUrl)
    {
        string selectedUrl = SelectedRedumpArtifactOption?.Url ?? string.Empty;

        if (overwriteExistingCatalogUrl || string.IsNullOrWhiteSpace(RedumpDatabaseDownloadUrl))
        {
            RedumpDatabaseDownloadUrl = selectedUrl;
            return;
        }

        OnPropertyChanged(nameof(CanDownloadSelectedRedumpDatabase));
    }


    private RedumpCatalogChoiceOption ResolveRedumpPlatformOption(string? mode, string? platformKey)
    {
        string key = string.Equals(mode, "Platform", StringComparison.OrdinalIgnoreCase)
            ? platformKey?.Trim() ?? string.Empty
            : string.IsNullOrWhiteSpace(mode)
                ? "Auto"
                : mode.Trim();

        return RedumpPlatformOptions.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? RedumpPlatformOptions.FirstOrDefault()
            ?? new RedumpCatalogChoiceOption(
                "Auto",
                "LocRedumpCatalog_Platform_Auto_Label",
                "LocRedumpCatalog_Platform_Auto_Description");
    }


    private RedumpCatalogChoiceOption ResolveRedumpArtifactOption(string? artifactKey)
    {
        return RedumpArtifactOptions.FirstOrDefault(x => string.Equals(x.Key, artifactKey?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? RedumpArtifactOptions.FirstOrDefault()
            ?? new RedumpCatalogChoiceOption(
                "Datfile",
                "LocRedumpCatalog_Artifact_Datfile_Label",
                "LocRedumpCatalog_Artifact_Datfile_Description");
    }


    private static string ResolveOptionDescription(RedumpCatalogChoiceOption? option)
    {
        return option?.Description ?? string.Empty;
    }


    private static bool IsSafeDownloadUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri)
               && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(uri.Host)
               && string.IsNullOrEmpty(uri.UserInfo);
    }
}
