namespace HakamiqChdTool.App.Ui.Shell;

public sealed record ShellQueueItem(
    string FileName,
    string Operation,
    string State,
    string ProgressText,
    string ValidationText = "جاهز",
    string ValidationDescription = "",
    bool IsRejected = false,
    bool RequiresReview = false,
    string SourcePath = "",
    string InputKind = "غير معروف",
    string IntakeDisposition = "مرفوض",
    string IntakeSummaryText = "",
    double ProgressPercent = 0,
    string TechnicalProgressText = "",
    bool HasProgress = false);
