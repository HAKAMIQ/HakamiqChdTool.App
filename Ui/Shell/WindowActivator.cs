using HakamiqChdTool.App.Services;


namespace HakamiqChdTool.App.Ui.Shell;

public sealed class WindowActivator : IWindowActivationService
{
    public bool TryShowPath(string path)
    {
        return ExplorerLaunchHelper.TrySelectPathInExplorer(path);
    }
}
