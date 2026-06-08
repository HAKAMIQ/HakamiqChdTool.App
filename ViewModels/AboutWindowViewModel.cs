using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class AboutWindowViewModel : ObservableObject
{
    private readonly IExternalLinkService _externalLinkService;

    public AboutWindowViewModel(AboutInfo info, IExternalLinkService externalLinkService)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(externalLinkService);

        _externalLinkService = externalLinkService;
        WindowTitle = info.WindowTitle;
        ProductName = info.ProductName;
        VersionLabel = ResolveVersionLabel(info.VersionLabel);
        Tagline = info.Tagline;
        Description = info.Description;
        DeveloperLine = info.DeveloperLine;
        LicenseLine = info.LicenseLine;
        CreditsTitle = info.CreditsTitle;
        CreditsDescription = info.CreditsDescription;
        WebsiteUrl = info.WebsiteUrl;
        Credits = CreateCreditsCollection(info.Credits);
        Links = CreateLinksCollection(info.Links);
    }

    public string WindowTitle { get; }

    public string ProductName { get; }

    public string VersionLabel { get; }

    public string Tagline { get; }

    public string Description { get; }

    public string DeveloperLine { get; }

    public string LicenseLine { get; }

    public string CreditsTitle { get; }

    public string CreditsDescription { get; }

    public string WebsiteUrl { get; }

    public ObservableCollection<AboutCreditInfo> Credits { get; }

    public ObservableCollection<AboutLinkInfo> Links { get; }

    [ObservableProperty]
    private string? lastErrorMessage;

    [RelayCommand]
    private void OpenLink(AboutLinkInfo? link)
    {
        LastErrorMessage = null;

        if (link is null)
        {
            return;
        }

        if (!_externalLinkService.TryOpen(link.Url, out string errorMessageKey))
        {
            LastErrorMessage = ArabicUi.ResolveDisplayString(errorMessageKey);
        }
    }

    [RelayCommand]
    private void OpenWebsite()
    {
        LastErrorMessage = null;

        if (!_externalLinkService.TryOpen(WebsiteUrl, out string errorMessageKey))
        {
            LastErrorMessage = ArabicUi.ResolveDisplayString(errorMessageKey);
        }
    }

    private static string ResolveVersionLabel(string fallbackVersionLabel)
    {
        Assembly assembly = typeof(AboutWindowViewModel).Assembly;

        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        string? normalizedVersion = NormalizeVersionLabel(informationalVersion);

        if (!string.IsNullOrWhiteSpace(normalizedVersion))
        {
            return normalizedVersion;
        }

        Version? assemblyVersion = assembly.GetName().Version;

        if (assemblyVersion is not null &&
            assemblyVersion.Major >= 0 &&
            assemblyVersion.Minor >= 0 &&
            assemblyVersion.Build >= 0)
        {
            return $"v{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
        }

        normalizedVersion = NormalizeVersionLabel(fallbackVersionLabel);

        return string.IsNullOrWhiteSpace(normalizedVersion)
            ? "v1.0.0"
            : normalizedVersion;
    }

    private static string? NormalizeVersionLabel(string? versionLabel)
    {
        if (string.IsNullOrWhiteSpace(versionLabel))
        {
            return null;
        }

        string cleaned = versionLabel.Trim();

        int buildMetadataIndex = cleaned.IndexOf('+', StringComparison.Ordinal);
        if (buildMetadataIndex >= 0)
        {
            cleaned = cleaned[..buildMetadataIndex].Trim();
        }

        if (cleaned.Length == 0)
        {
            return null;
        }

        return cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? cleaned
            : $"v{cleaned}";
    }

    private static ObservableCollection<AboutCreditInfo> CreateCreditsCollection(
        IEnumerable<AboutCreditInfo>? credits)
    {
        var result = new ObservableCollection<AboutCreditInfo>();

        if (credits is null)
        {
            return result;
        }

        foreach (AboutCreditInfo? credit in credits)
        {
            if (credit is not null)
            {
                result.Add(credit);
            }
        }

        return result;
    }

    private static ObservableCollection<AboutLinkInfo> CreateLinksCollection(
        IEnumerable<AboutLinkInfo>? links)
    {
        var result = new ObservableCollection<AboutLinkInfo>();

        if (links is null)
        {
            return result;
        }

        foreach (AboutLinkInfo? link in links)
        {
            if (link is not null)
            {
                result.Add(link);
            }
        }

        return result;
    }
}