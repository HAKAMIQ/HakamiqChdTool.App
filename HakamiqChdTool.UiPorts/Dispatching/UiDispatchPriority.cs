namespace HakamiqChdTool.UiPorts.Dispatching;

public enum UiDispatchPriority
{
    Send,
    Normal,
    Background,
    ContextIdle,
    ApplicationIdle,
    DataBind,
    Render
}
