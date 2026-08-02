namespace HakamiqChdTool.App.Core.Input;

public enum MediaInputPipelineAction
{
    Block = 0,
    AcceptFolder = 1,
    AcceptConvertibleDiscImage = 2,
    AcceptArchiveContainer = 3,
    AcceptChdImage = 4,
    RequiresStandaloneBinPolicy = 5,
    DetectedOnly = 6
}

public sealed record MediaInputPipelineDecision(
    MediaInputDescriptor Descriptor,
    MediaInputPipelineAction Action,
    QueueInputRole QueueRole,
    string? EffectivePath,
    string Reason,
    string? MessageKey)
{
    public bool IsBlocked => Action == MediaInputPipelineAction.Block;

    public bool IsDetectedOnly => Action == MediaInputPipelineAction.DetectedOnly;

    public bool IsAcceptedForQueue => Action is MediaInputPipelineAction.AcceptConvertibleDiscImage
        or MediaInputPipelineAction.AcceptArchiveContainer
        or MediaInputPipelineAction.AcceptChdImage;

    public bool ShouldEnumerateFolder => Action == MediaInputPipelineAction.AcceptFolder;

    public bool RequiresStandaloneBinPolicy => Action == MediaInputPipelineAction.RequiresStandaloneBinPolicy;
}

public static class MediaInputPipelineDecisionReasons
{
    public const string InputMissingOrInvalid = "input-missing-or-invalid";
    public const string FolderEnumeration = "folder-enumeration";
    public const string FolderKindWithoutDirectory = "folder-kind-without-directory";
    public const string ConvertibleDiscImage = "convertible-disc-image";
    public const string ArchiveContainer = "archive-container";
    public const string ChdImage = "chd-image";
    public const string StandaloneBinPolicyRequired = "standalone-bin-policy-required";
    public const string DependentTrackFile = "dependent-track-file";
    public const string DetectedOnlyPackage = "detected-only-package";
    public const string UnsupportedMediaInput = "unsupported-media-input";
    public const string HeaderEvidenceRejected = "header-evidence-rejected";
}
