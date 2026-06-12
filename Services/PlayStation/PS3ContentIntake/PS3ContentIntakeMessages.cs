namespace HakamiqChdTool.App.Services.PlayStation.PS3ContentIntake;

internal static class PS3ContentIntakeMessages
{
    public const string PipelineIsoToChd = "LocPs3Intake_PipelineIsoToChd";
    public const string PipelineFolderToChd = "LocPs3Intake_PipelineFolderToChd";
    public const string PipelinePkgInstallable = "LocPs3Intake_PipelinePkgInstallable";
    public const string PipelinePkgUnsupported = "LocPs3Intake_PipelinePkgUnsupported";
    public const string PipelinePkgNotDiscImage = "LocPs3Intake_PipelinePkgNotDiscImage";
    public const string PipelineChdLogicalReport = "LocPs3Intake_PipelineChdLogicalReport";
    public const string PipelineUnsupportedChd = "LocPs3Intake_PipelineUnsupportedChd";
    public const string PipelineUnsupportedSource = "LocPs3Intake_PipelineUnsupportedSource";
    public const string PipelineUnsupportedIso = "LocPs3Intake_PipelineUnsupportedIso";
    public const string PipelineUnsupportedFolder = "LocPs3Intake_PipelineUnsupportedFolder";
    public const string PipelineEncryptedUnreadableIso = "LocPs3Intake_PipelineEncryptedUnreadableIso";
    public const string PipelineEncryptedIncompleteOrNonPs3Iso = "LocPs3Intake_PipelineEncryptedIncompleteOrNonPs3Iso";

    public const string WarningNoSourcePath = "LocPs3Intake_WarningNoSourcePath";
    public const string WarningSourcePathInvalid = "LocPs3Intake_WarningSourcePathInvalid";
    public const string WarningUnsupportedInputFormat = "LocPs3Intake_WarningUnsupportedInputFormat";
    public const string WarningAnalyzeUnsafe = "LocPs3Intake_WarningAnalyzeUnsafe";

    public const string WarningChdMissing = "LocPs3Intake_WarningChdMissing";
    public const string WarningChdExistingContainer = "LocPs3Intake_WarningChdExistingContainer";

    public const string WarningPkgMissing = "LocPs3Intake_WarningPkgMissing";
    public const string WarningPkgHeaderUnrecognized = "LocPs3Intake_WarningPkgHeaderUnrecognized";
    public const string WarningPkgContentIdMissing = "LocPs3Intake_WarningPkgContentIdMissing";
    public const string WarningPkgHeaderUnreadable = "LocPs3Intake_WarningPkgHeaderUnreadable";

    public const string WarningIsoMissing = "LocPs3Intake_WarningIsoMissing";
    public const string WarningIsoUnreadableDkey = "LocPs3Intake_WarningIsoUnreadableDkey";
    public const string WarningIsoUnreadableEncryptedOrIncomplete = "LocPs3Intake_WarningIsoUnreadableEncryptedOrIncomplete";
    public const string WarningIsoNoPs3Game = "LocPs3Intake_WarningIsoNoPs3Game";
    public const string WarningIsoParamMissing = "LocPs3Intake_WarningIsoParamMissing";
    public const string WarningIsoEbootMissing = "LocPs3Intake_WarningIsoEbootMissing";
    public const string WarningIsoDiscSfbMissing = "LocPs3Intake_WarningIsoDiscSfbMissing";
    public const string WarningRawPs3MarkersDetected = "LocPs3Intake_WarningRawPs3MarkersDetected";
    public const string WarningRawBluRayWithoutPs3 = "LocPs3Intake_WarningRawBluRayWithoutPs3";
    public const string WarningIsoMountedRawPs3 = "LocPs3Intake_WarningIsoMountedRawPs3";
    public const string WarningDkeyDecryptionOutOfScope = "LocPs3Intake_WarningDkeyDecryptionOutOfScope";
    public const string WarningPs3MarkersIncomplete = "LocPs3Intake_WarningPs3MarkersIncomplete";

    public const string WarningFolderInvalidPath = "LocPs3Intake_WarningFolderInvalidPath";
    public const string WarningFolderMissing = "LocPs3Intake_WarningFolderMissing";
    public const string WarningFolderReparsePoint = "LocPs3Intake_WarningFolderReparsePoint";
    public const string WarningPs3GameReparsePoint = "LocPs3Intake_WarningPs3GameReparsePoint";
    public const string WarningFolderNoSafePs3Game = "LocPs3Intake_WarningFolderNoSafePs3Game";
    public const string WarningFolderParamMissing = "LocPs3Intake_WarningFolderParamMissing";
    public const string WarningFolderEbootMissing = "LocPs3Intake_WarningFolderEbootMissing";
    public const string WarningFolderDiscSfbMissing = "LocPs3Intake_WarningFolderDiscSfbMissing";
    public const string WarningSelectedFolderIsPs3Game = "LocPs3Intake_WarningSelectedFolderIsPs3Game";
}
