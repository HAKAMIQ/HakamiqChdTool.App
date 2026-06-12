using HakamiqChdTool.App.Services;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class TaskQueueItemViewModel
{
    public string OperationLogDisplay =>
        QueueVerificationResultPresenter.BuildOperationLogDisplay(HasLogPath, LogPathDisplay);

    public bool HasOperationReport => HasLogPath;

    public bool IsVerificationReport =>
        QueueVerificationResultPresenter.IsVerificationReport(
            RequestedAction,
            FinalResult,
            IntegrityState);

    public string OperationReportTitle =>
        QueueVerificationResultPresenter.BuildOperationReportTitle(IsVerificationReport);

    public string OperationReportMessage =>
        QueueVerificationResultPresenter.BuildOperationReportMessage(
            IsVerificationReport,
            IntegrityStatusMessage,
            QueueRowDisplayDetailArabic,
            OperationLogDisplay);

    public bool HasVerificationResult =>
        QueueVerificationResultPresenter.HasVerificationResult(IsVerificationReport, LogPath);

    public string VerificationResultBadgeText =>
        QueueVerificationResultPresenter.BuildVerificationResultBadgeText(
            IntegrityState,
            FinalResult,
            IsVerificationReport,
            LogPath);
}
