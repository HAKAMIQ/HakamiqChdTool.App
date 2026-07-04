namespace HakamiqChdTool.App.Localization;

public static class TaskQueueStateCodes
{
    public const string Pending = "Pending";
    public const string Ready = "Ready";
    public const string Extracting = "Extracting";
    public const string PasswordRequired = "PasswordRequired";
    public const string Failed = "Failed";
    public const string Skipped = "Skipped";
    public const string Cancelled = "Cancelled";
    public const string Converting = "Converting";
    public const string Verifying = "Verifying";
    public const string Completed = "Completed";
    public const string ReadingFile = "ReadingFile";
    public const string Processing = "Processing";
    public const string AwaitingOperationSelection = "AwaitingOperationSelection";

    public static bool IsTerminal(string? state) =>
        state == Completed || state == Failed || state == PasswordRequired || state == Cancelled || state == Skipped;

    public static bool IsActiveRunning(string? state) =>
        state == Processing || state == Extracting || state == Converting || state == Verifying || state == ReadingFile;

    public static bool IsWaiting(string? state) =>
        state == Pending || state == Ready || state == AwaitingOperationSelection;
}