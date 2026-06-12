namespace HakamiqChdTool.App.Ui.Shell;

public interface ITaskbarProgressService
{
    void SetProgress(double value, TaskbarProgressState state);

    void Clear();
}
