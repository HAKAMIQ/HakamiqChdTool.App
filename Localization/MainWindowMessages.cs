namespace HakamiqChdTool.App.Localization;

public static class MainWindowMessages
{
    public const string ReadyForProcessing = "LocUi_ReadyForProcessing";
    public const string ChooseOperationForItem = "LocUi_ChooseOperationForItem";
    public const string SelectOperationBeforeProcessFooter = "LocFooter_SelectOperationBeforeProcess";
    public const string UnsupportedQueueFile = "LocUi_UnsupportedQueueFile";

    public const string StatusDetail_OutputFileExists = "LocStatus_OutputFileExists";
    public const string StatusDetail_FinalOutputExists = "LocStatus_FinalOutputExists";

    public const string FooterIdleNoTasks = "LocFooter_IdleNoTasks";
    public const string WaitForBackgroundOp = "LocFooter_WaitForBackgroundOp";
    public const string AnalyzingFiles = "LocFooter_AnalyzingFiles";
    public const string NothingNewAdded = "LocFooter_NothingNewAdded";
    public const string NoSupportedFiles = "LocFooter_NoSupportedFiles";
    public const string AlreadyInQueue = "LocFooter_AlreadyInQueue";
    public const string ArchiveWillUnpackThenConvertFooter = "LocFooter_ArchiveWillUnpackThenConvert";
    public const string ArchiveWillUnpackThenConvertDetail = "LocUi_ArchiveWillUnpackThenConvert";
    public const string ArchiveAwaitingPreviewAtStartup = "LocUi_ArchiveAwaitingPreviewAtStartup";
    public const string AddedOne = "LocFooter_AddedOne";
    public const string ChdNotConvertible = "LocUi_Footer_ChdNotConvertible";
    public const string Fmt_AddedMany = "LocFmt_AddedMany";
    public const string Fmt_AddFilesFailed = "LocFmt_AddFilesFailed";
    public const string Fmt_FolderReadFailed = "LocFmt_FolderReadFailed";
    public const string Fmt_DeepIntegrityDone = "LocFmt_DeepIntegrityDone";

    public const string AddFilesErrorTitle = "LocDialog_ErrorTitle";
    public const string AddFilesErrorBody = "LocBody_AddFilesError";
    public const string AddFilesCancelledFooter = "LocFooter_AddFilesCancelled";

    public const string ClearQueuePrompt = "LocDialog_ClearQueueBody";
    public const string ClearQueueTitle = "LocDialog_ClearQueueTitle";
    public const string QueueClearedSummary = "LocUi_QueueClearedSummary";
    public const string QueueClearedLog = "LocUi_QueueClearedLog";
    public const string QueueClearedFooter = "LocFooter_QueueCleared";

    public const string InitializingTools = "LocFooter_InitializingTools";

    public static string StartupRuntimeErrorBody(string detail) =>
        ArabicUi.Format("LocFmt_StartupRuntimeError", ArabicUi.ToUserSafeDetail(detail));

    public const string StartupRuntimeErrorTitle = "LocDialog_CannotStartAppTitle";

    public const string AdvancedSettingsUpdatedLog = "Loc_Log_AdvancedSettingsUpdated";
    public const string AdvancedSettingsUpdatedFooter = "LocFooter_AdvancedSettingsUpdated";

    public const string CouldNotOpenLogsFolder = "LocDialog_OpenLogsFolderFailedTitle";
    public const string CouldNotOpenAppDataFolder = "LocDialog_OpenAppDataFolderFailedTitle";

    public const string SelectFolderDialogDescription = "LocDialog_SelectFolderScanDescription";
    public const string AddFilesDialogTitle = "LocDialog_AddFilesTitle";

    public const string QuickVerifyDialogTitle = "LocDialog_QuickVerifyTitle";
    public const string QuickExtractDialogTitle = "LocDialog_QuickExtractTitle";
    public const string QuickConvertDialogTitle = "LocDialog_QuickConvertTitle";
    public const string QuickChdFilesFilter = "LocFilter_QuickChdFiles";
    public const string QuickConvertFilesFilter = "LocFilter_QuickConvertFiles";
    public const string QuickExtractFolderDescription = "LocDialog_QuickExtractFolderDescription";
    public const string QuickConvertFolderDescription = "LocDialog_QuickConvertFolderDescription";

    public const string DeepIntegrityDisabledShort = "LocIntegrity_Disabled";
    public const string DeepIntegrityDisabledDetail = "LocIntegrity_DeepVerifyDisabledHint";
    public const string DeepIntegrityScanning = "LocIntegrity_Scanning";
    public const string DeepIntegrityNoDatShort = "LocIntegrity_NoDatabase";
    public const string DeepIntegrityNoDatDetail = "LocIntegrity_NoDatHint";
    public const string IntegrityErrorShort = "LocIntegrity_ErrorGeneric";
    public const string IntegrityCancelledDetail = "LocIntegrity_CancelledDetail";

    public const string ProcessSelectedFailedFooter = "LocFooter_ProcessSelectedFailed";
    public const string ProcessingErrorTitle = "LocDialog_ProcessingErrorTitle";

    public const string ProcessingStartedFooter = "LocFooter_ProcessingStarted";
    public const string NothingQueuedToStartFooter = "LocFooter_NothingQueuedToStart";
    public const string ProcessingUnexpectedErrorFooter = "LocFooter_ProcessingUnexpectedError";

    public const string VerifyChdFailedFooter = "LocFooter_VerifyChdFailed";
    public const string VerifyChdErrorTitle = "LocDialog_VerifyChdErrorTitle";

    public const string IntegrityNoDiskFileFooter = "LocFooter_IntegrityNoDiskFile";
    public const string IntegrityNoDiskFileBody = "LocDialog_IntegrityNoDiskBody";
    public const string IntegrityNoDiskFileTitle = "LocDialog_IntegrityNoDiskTitle";

    public const string OpenFolderNoPathFooter = "LocFooter_OpenFolderNoPath";
    public const string OpenFolderNoPathBody = "LocDialog_OpenFolderNoPathBody";
    public const string OpenFolderTitle = "LocDialog_OpenFolderTitle";
    public const string OpenFolderFailedFooter = "LocFooter_OpenFolderFailed";
    public const string OpenFolderFailedBody = "LocDialog_OpenFolderFailedBody";

    public const string ItemRemovedFooter = "LocFooter_ItemRemoved";

    public const string PathCopiedFooter = "LocFooter_PathCopied";
    public const string PathCopyFailedFooter = "LocFooter_PathCopyFailed";

    public const string CancellingProcessingFooter = "LocFooter_CancellingProcessing";

    public const string SelectFileToViewDetails = "LocUi_SelectFileToViewDetails";

    public const string Fmt_RenameFailedLog = "LocFmt_RenameFailedLog";
    public const string RenameSuccessFooter = "LocFooter_RenameSuccess";
    public const string RenameErrorTitle = "LocDialog_RenameFailedTitle";

    public const string RenameDialogTitle = "LocDialog_RenameConfirmTitle";
    public const string RenameDialogHeading = "LocDialog_RenameConfirmHeading";
    public const string RenameDialogQuestion = "LocDialog_RenameConfirmQuestion";
    public const string RenameDialogOk = "Loc_Button_Ok";
    public const string RenameDialogCancel = "Loc_Button_Cancel";

    public const string ToolbarIdleNoTasks = "LocUi_NoTasksAddOrDrag";

    public const string RunSummaryUpdating = "LocUi_RunSummaryUpdating";
    public const string RunSummarySessionStarted = "LocUi_RunSummarySessionStarted";

    public const string SessionEndedUserCancel = "LocSession_EndedCancelled";
    public const string SessionEnded = "LocSession_Ended";
    public const string SessionTotalItems = "LocSession_TotalItems";
    public const string SessionCompleted = "LocSession_Completed";
    public const string SessionFailed = "LocSession_Failed";
    public const string SessionSkipped = "LocSession_Skipped";
    public const string SessionCancelled = "LocSession_CancelledCount";
    public const string SessionFeatureReport = "LocSession_FeatureReport";
    public const string SessionDirectSupported = "LocSession_DirectSupported";
    public const string SessionReverseSupported = "LocSession_ReverseSupported";
    public const string SessionAvgCompression = "LocSession_AvgCompression";
    public const string SessionRedumpMatches = "LocSession_RedumpMatches";
    public const string SessionCleanupGb = "LocSession_CleanupGb";
    public const string SessionGbUnit = "LocSession_GbUnit";
    public const string SessionItemsNeedReview = "LocSession_ItemsNeedReview";
    public const string SessionNoFailedItems = "LocSession_NoFailedItems";
    public const string SessionSavedGb = "LocSession_SavedGb";
    public const string SessionSbiCopied = "LocSession_SbiCopied";
    public const string SessionM3uGenerated = "LocSession_M3uGenerated";
    public const string SessionM3uSkippedExisting = "LocSession_M3uSkippedExisting";
    public const string SessionPostProcessingWarnings = "LocSession_PostProcessingWarnings";

    public const string IntakeReviewCancelledFooter = "LocIntakeReview_CancelledFooter";

    public const string Fmt_M3uGeneratedFooter = "LocFmt_M3uGeneratedFooter";

    public const string SessionCancelledFooter = "LocFooter_SessionCancelled";
    public const string SessionCompletedFooter = "LocFooter_SessionCompleted";

    public const string SummaryCancelledTitle = "LocDialog_SummaryCancelledTitle";
    public const string SummaryCompletedTitle = "LocDialog_SummaryCompletedTitle";

    public const string TraySessionCancelledTitle = "LocTray_SessionCancelledTitle";
    public const string TraySessionCompletedTitle = "LocTray_SessionCompletedTitle";
    public const string Fmt_TraySessionBalloon = "LocFmt_TraySessionBalloon";

    public const string CloseWhileProcessingPrompt = "LocDialog_CloseWhileProcessingBody";
    public const string CloseWhileProcessingTitle = "LocDialog_CloseWhileProcessingTitle";
}