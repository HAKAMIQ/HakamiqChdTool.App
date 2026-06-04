using System.Collections.ObjectModel;

namespace HakamiqChdTool.App.Models;

public sealed class AboutInfo
{
    public string WindowTitle { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public string VersionLabel { get; init; } = string.Empty;
    public string Tagline { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string DeveloperLine { get; init; } = string.Empty;
    public string LicenseLine { get; init; } = string.Empty;
    public string CreditsTitle { get; init; } = string.Empty;
    public string CreditsDescription { get; init; } = string.Empty;
    public string WebsiteUrl { get; init; } = string.Empty;
    public Collection<AboutCreditInfo> Credits { get; init; } = [];
    public Collection<AboutLinkInfo> Links { get; init; } = [];
}

public sealed class AboutCreditInfo
{
    public string DisplayName { get; init; } = string.Empty;
    public string Handle { get; init; } = string.Empty;
    public string BadgeText { get; init; } = string.Empty;
    public string Contribution { get; init; } = string.Empty;
    public string ProfileSummary { get; init; } = string.Empty;
    public string AvatarImage { get; init; } = string.Empty;
    public string ProfileCardImage { get; init; } = string.Empty;
    public string AccentBrush { get; init; } = string.Empty;
    public string AccentSoftBrush { get; init; } = string.Empty;
}

public sealed class AboutLinkInfo
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string IconGlyph { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
}
