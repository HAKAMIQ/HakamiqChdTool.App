using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace HakamiqChdTool.App.Services;

public static class AboutInfoFactory
{
    private const string ProductFallback = "Hakamiq CHD Tool";

    public static AboutInfo Create(AppMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        Assembly assembly = Assembly.GetExecutingAssembly();
        AssemblyName assemblyName = assembly.GetName();

        string version = ResolveVersion(assembly, assemblyName);
        string productName = NormalizeDisplayValue(
            Read<AssemblyProductAttribute>(assembly, static x => x.Product),
            ProductFallback);

        string company = NormalizeDisplayValue(
            Read<AssemblyCompanyAttribute>(assembly, static x => x.Company),
            metadata.PublisherName);

        return new AboutInfo
        {
            WindowTitle = ArabicUi.Format("LocAbout_WindowTitle", productName),
            ProductName = productName,
            VersionLabel = version,
            Tagline = ArabicUi.Get("LocAbout_Tagline"),
            Description = ArabicUi.Get("LocAbout_DefaultDescription"),
            DeveloperLine = ArabicUi.Format("LocAbout_DeveloperLine", company, metadata.DeveloperLineSuffix),
            LicenseLine = ArabicUi.Get("LocAbout_LicenseLine"),
            WebsiteUrl = metadata.WebsiteUrl,
            Links = LocalizeLinks(metadata.Links)
        };
    }


    private static Collection<AboutLinkInfo> LocalizeLinks(IEnumerable<AboutLinkInfo>? links)
    {
        var localized = new Collection<AboutLinkInfo>();

        if (links is null)
        {
            return localized;
        }

        foreach (AboutLinkInfo? link in links)
        {
            if (link is null)
            {
                continue;
            }

            localized.Add(new AboutLinkInfo
            {
                Key = link.Key,
                DisplayName = ResolveLinkDisplayName(link),
                IconGlyph = link.IconGlyph,
                Url = link.Url
            });
        }

        return localized;
    }

    private static string ResolveLinkDisplayName(AboutLinkInfo link)
    {
        if (string.IsNullOrWhiteSpace(link.Key))
        {
            return link.DisplayName;
        }

        string key = link.Key.Trim().ToLowerInvariant();

        string resourceKey = key switch
        {
            "website" => "LocAbout_Link_Website",
            "github" => "LocAbout_Link_GitHub",
            "youtube" => "LocAbout_Link_YouTube",
            "telegram" => "LocAbout_Link_Telegram",
            "discord" => "LocAbout_Link_Discord",
            "tiktok" => "LocAbout_Link_TikTok",
            _ => string.Empty
        };

        return string.IsNullOrWhiteSpace(resourceKey)
            ? link.DisplayName
            : ArabicUi.Get(resourceKey);
    }

    private static string NormalizeDisplayValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }

    private static string? Read<TAttribute>(Assembly assembly, Func<TAttribute, string> selector)
        where TAttribute : Attribute
    {
        return assembly.GetCustomAttribute<TAttribute>() is TAttribute attribute
            ? selector(attribute)
            : null;
    }
}
