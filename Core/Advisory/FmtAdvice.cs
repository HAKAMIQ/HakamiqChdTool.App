namespace HakamiqChdTool.App.Core.Advisory;

public sealed record FormatSafetyAdvice(
    string TitleKey,
    string MessageKey,
    FormatSafetyLevel Level,
    bool IsBlocking,
    IReadOnlyList<string> RelatedFormats,
    string RecommendedActionKey)
{
    public static FormatSafetyAdvice NonBlocking(
        string titleKey,
        string messageKey,
        FormatSafetyLevel level,
        IEnumerable<string> relatedFormats,
        string recommendedActionKey) => new(
            titleKey,
            messageKey,
            level,
            false,
            [.. relatedFormats],
            recommendedActionKey);
}
