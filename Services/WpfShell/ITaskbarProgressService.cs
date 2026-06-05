namespace HakamiqChdTool.App.Services.WpfShell;

public interface ITaskbarProgressService
{
    void SetProgress(double value, UiTaskbarProgressState state);

    void Clear();
}
