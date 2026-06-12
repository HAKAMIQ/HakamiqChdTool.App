using HakamiqChdTool.App.Core.Input;

namespace HakamiqChdTool.App.Core.Workflow;

public static class WorkflowPathHelpers
{
    public static bool IsArchivePath(string path) =>
        QueueInputClassifier.IsArchiveContainerPath(path);
}
