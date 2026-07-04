namespace HakamiqChdTool.App.Services.PlayStation.Ps2;

internal sealed record Ps2DiscStructure(
    bool HasSystemCnf,
    bool IsPlayStation2,
    string BootDirective,
    string BootPath,
    string BootExecutable,
    string Serial,
    string Region,
    string SourceFile,
    string SourceLayout)
{
    public static Ps2DiscStructure Empty { get; } = new(
        HasSystemCnf: false,
        IsPlayStation2: false,
        BootDirective: string.Empty,
        BootPath: string.Empty,
        BootExecutable: string.Empty,
        Serial: string.Empty,
        Region: string.Empty,
        SourceFile: string.Empty,
        SourceLayout: string.Empty);
}
