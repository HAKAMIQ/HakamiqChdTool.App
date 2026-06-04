namespace HakamiqChdTool.App.Services;

public interface IExternalLinkService
{
    bool TryOpen(string url, out string errorMessageKey);
}