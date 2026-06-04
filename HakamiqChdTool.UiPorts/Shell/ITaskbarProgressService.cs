namespace HakamiqChdTool.UiPorts.Shell;

public interface ITaskbarProgressService
{
    void SetProgress(double value, UiTaskbarProgressState state);

    void Clear();
}
