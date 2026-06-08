using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Services.RedumpCatalog;
using System;

namespace HakamiqChdTool.App.ViewModels;

public sealed class RedumpCatalogChoiceOption
{
    public RedumpCatalogChoiceOption(
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
        Url = string.IsNullOrWhiteSpace(url) ? null : url.Trim();
        IsLegalAdvancedArtifact = isLegalAdvancedArtifact;
        TechnicalDescription = string.IsNullOrWhiteSpace(technicalDescription) ? null : technicalDescription.Trim();
    }

    public string Key { get; }

    public string LabelKey { get; }

    public string DescriptionKey { get; }

    public string Label => ArabicUi.Get(LabelKey);

    public string Description => string.IsNullOrWhiteSpace(TechnicalDescription)
        ? ArabicUi.Get(DescriptionKey)
        : TechnicalDescription;

    public string? Url { get; }

    public bool IsLegalAdvancedArtifact { get; }

    private string? TechnicalDescription { get; }

    public static RedumpCatalogChoiceOption FromCatalogOption(RedumpCatalogOption option)
    {
        ArgumentNullException.ThrowIfNull(option);

        return new RedumpCatalogChoiceOption(
            option.Key,
            option.LabelKey,
            option.DescriptionKey,
            option.Url,
            option.IsLegalAdvancedArtifact,
            option.TechnicalDescription);
    }

    public override string ToString()
    {
        return Label;
    }
}

public sealed class AdvancedProcessorOption(int value, string label)
{
    public int Value { get; } = value;

    public string Label { get; } = label;
}

public sealed class AdvancedChoiceOption(string key, string label, string description)
{
    public string Key { get; } = key;

    public string Label { get; } = label;

    public string Description { get; } = description;
}
