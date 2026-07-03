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

public sealed class AboutLinkInfo
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string IconGlyph { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
}
