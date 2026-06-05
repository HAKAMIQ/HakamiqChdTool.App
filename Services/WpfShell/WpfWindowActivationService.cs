
namespace HakamiqChdTool.App.Services.WpfShell;

public sealed class WpfWindowActivationService : IWindowActivationService
{
    public bool TryShowPath(string path)
    {
        return ExplorerLaunchHelper.TrySelectPathInExplorer(path);
    }
}
