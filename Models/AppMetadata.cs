using System;
using System.Collections.ObjectModel;

namespace HakamiqChdTool.App.Models;

public sealed class AppMetadata
{
    private const string DefaultPublisherName = "HAKAMIQ";
    private const string DefaultDeveloperLineSuffix = "Gaming & Emulation";

    private const string WebsiteUrlValue = "https://hakamiq1.blogspot.com/";
    private const string GitHubUrlValue = "https://github.com/hakamiq";
    private const string YouTubeUrlValue = "https://www.youtube.com/@hakamiq";
    private const string TelegramUrlValue = "https://t.me/hakamiq0";
    private const string DiscordUrlValue = "https://discord.gg/WqgRrmAKpC";
    private const string TikTokUrlValue = "https://www.tiktok.com/@hakamiq";

    public string PublisherName { get; init; } = DefaultPublisherName;

    public string DeveloperLineSuffix { get; init; } = DefaultDeveloperLineSuffix;

    public string WebsiteUrl { get; init; } = string.Empty;

    public string DiscordUrl { get; init; } = string.Empty;

    public Collection<AboutLinkInfo> Links { get; init; } = [];

    public static AppMetadata CreateDefault()
    {
        return new AppMetadata
        {
            WebsiteUrl = ValidatePublicUrl(WebsiteUrlValue),
            DiscordUrl = ValidatePublicUrl(DiscordUrlValue),
            Links =
            [
                CreateLink("website", "HAKAMIQ Website", "", WebsiteUrlValue),
                CreateLink("github", "GitHub", "", GitHubUrlValue),
                CreateLink("youtube", "YouTube", "", YouTubeUrlValue),
                CreateLink("telegram", "Telegram", "", TelegramUrlValue),
                CreateLink("discord", "Discord", "", DiscordUrlValue),
                CreateLink("tiktok", "TikTok", "", TikTokUrlValue)
            ]
        };
    }

    private static AboutLinkInfo CreateLink(
        string key,
        string displayName,
        string iconGlyph,
        string url)
    {
        return new AboutLinkInfo
        {
            Key = key,
            DisplayName = displayName,
            IconGlyph = iconGlyph,
            Url = ValidatePublicUrl(url)
        };
    }

    private static string ValidatePublicUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            throw new InvalidOperationException("Application metadata contains an invalid URL.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Application metadata links must use HTTPS.");
        }

        return uri.AbsoluteUri;
    }
}