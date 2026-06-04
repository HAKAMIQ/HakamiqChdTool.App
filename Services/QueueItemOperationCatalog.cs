using HakamiqChdTool.App.Localization;

namespace HakamiqChdTool.App.Services;

public static class QueueItemOperationCatalog
{
    public static IReadOnlyList<string> GetSupportedOperationCodes(string? path) =>
        QueueOperationCapabilityService.GetSupportedOperationCodes(path);

    public static string GetInitialRequestedAction(string? path)
    {
        IReadOnlyList<string> operations = GetSupportedOperationCodes(path);

        return operations.Count switch
        {
            0 => TaskActionCodes.Unsupported,
            1 => operations[0],
            _ => TaskActionCodes.PendingSelection
        };
    }

    public static bool IsOperationAllowed(string? path, string? actionCode) =>
        QueueOperationCapabilityService.IsOperationAllowed(path, actionCode);
}