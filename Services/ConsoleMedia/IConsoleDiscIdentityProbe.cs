namespace HakamiqChdTool.App.Services.ConsoleMedia;

internal interface IConsoleDiscIdentityProbe
{
    ConsoleDiscIdentityResult Probe(ConsoleDiscScanContext context);
}
