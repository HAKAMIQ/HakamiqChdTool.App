using System;

namespace HakamiqChdTool.App.Services.RedumpCatalog;

public sealed class RedumpCatalogOption
{
    public RedumpCatalogOption(
        string key,
        string labelKey,
        string descriptionKey,
        string? url = null,
        bool isLegalAdvancedArtifact = false,
        string? technicalDescription = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(labelKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptionKey);

        Key = key.Trim();
        LabelKey = labelKey.Trim();
        DescriptionKey = descriptionKey.Trim();
        Url = NormalizeSafeUrl(url);
        IsLegalAdvancedArtifact = isLegalAdvancedArtifact;
        TechnicalDescription = NormalizeOptionalValue(technicalDescription);
    }

    public string Key { get; }

    public string LabelKey { get; }

    public string DescriptionKey { get; }

    public string? TechnicalDescription { get; }

    public string? Url { get; }

    public bool IsLegalAdvancedArtifact { get; }

    public override string ToString()
    {
        return Key;
    }

    private static string? NormalizeSafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        string normalized = url.Trim();

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
