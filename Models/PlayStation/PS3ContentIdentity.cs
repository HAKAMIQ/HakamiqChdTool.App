using System.Collections.Generic;

namespace HakamiqChdTool.App.Models.PlayStation;

public sealed record PS3ContentIdentity(
    string? TitleId,
    string? TitleName,
    string? DiscId,
    string? Category,
    IReadOnlyDictionary<string, string> Fields)
{
    public static PS3ContentIdentity Empty { get; } = new(
        TitleId: null,
        TitleName: null,
        DiscId: null,
        Category: null,
        Fields: new Dictionary<string, string>());
}