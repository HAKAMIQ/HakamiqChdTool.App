using HakamiqChdTool.UiPorts.Shell;

namespace HakamiqChdTool.App.Services.UiPorts;

public sealed class WpfWindowActivationService : IWindowActivationService
{
    public bool TryShowPath(string path)
    {
        return ExplorerLaunchHelper.TrySelectPathInExplorer(path);
    }
}
