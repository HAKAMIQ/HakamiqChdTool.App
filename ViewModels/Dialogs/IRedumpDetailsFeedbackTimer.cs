namespace HakamiqChdTool.App.ViewModels.Dialogs;

public interface IRedumpDetailsFeedbackTimer : IDisposable
{
    void Restart(Action elapsed);

    void Stop();
}
