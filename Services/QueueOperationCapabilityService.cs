using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Services.MediaInputPolicy;

namespace HakamiqChdTool.App.Services;

internal static class QueueOperationCapabilityService
{
    private static readonly IReadOnlyList<string> NoOperations =
        Array.AsReadOnly<string>([]);

    private static readonly IReadOnlyList<string> ConvertibleDiscOperations =
        Array.AsReadOnly([TaskActionCodes.ConvertToChd]);

    private static readonly IReadOnlyList<string> ArchiveContainerOperations =
        Array.AsReadOnly([TaskActionCodes.StageArchiveForConversion]);

    private static readonly IReadOnlyList<string> ChdOperations =
        Array.AsReadOnly([
            TaskActionCodes.VerifyChd,
            TaskActionCodes.RestoreDiscImageFromChd
        ]);

    public static IReadOnlyList<string> GetSupportedOperationCodes(string? path)
    {
        MediaInputDecision mediaDecision = global::HakamiqChdTool.App.Services.MediaInputPolicy.MediaInputPolicy.Evaluate(path);
        return mediaDecision.IsBlocked
            ? NoOperations
            : GetSupportedOperationCodes(QueueInputClassifier.Classify(mediaDecision.EffectivePath));
    }

    public static IReadOnlyList<string> GetSupportedOperationCodes(QueueInputClassification classification) =>
        classification.Role switch
        {
            QueueInputRole.ConvertibleDiscImage => ConvertibleDiscOperations,
            QueueInputRole.BinCueRescueCandidate => ConvertibleDiscOperations,
            QueueInputRole.ArchiveContainer => ArchiveContainerOperations,
            QueueInputRole.ChdImage => ChdOperations,
            QueueInputRole.DependentTrackFile => NoOperations,
            QueueInputRole.Unsupported => NoOperations,
            _ => NoOperations
        };

    public static bool IsOperationAllowed(string? path, string? actionCode)
    {
        if (string.IsNullOrWhiteSpace(actionCode)
            || string.Equals(actionCode, TaskActionCodes.PendingSelection, StringComparison.Ordinal))
        {
            return false;
        }

        IReadOnlyList<string> operations = GetSupportedOperationCodes(path);
        for (int index = 0; index < operations.Count; index++)
        {
            if (string.Equals(operations[index], actionCode, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static QueueOperationMode GetOperationMode(string? actionCode) =>
        actionCode switch
        {
            TaskActionCodes.ConvertToChd => QueueOperationMode.Convert,
            TaskActionCodes.StageArchiveForConversion => QueueOperationMode.Convert,
            TaskActionCodes.RestoreDiscImageFromChd => QueueOperationMode.Extract,
            TaskActionCodes.VerifyChd => QueueOperationMode.Verify,
            _ => QueueOperationMode.None
        };
}
