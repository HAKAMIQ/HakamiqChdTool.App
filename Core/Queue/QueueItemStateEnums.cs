namespace HakamiqChdTool.App.Core.Queue;

public enum QueueItemStage
{
    Pending = 0,
    ReadingFile = 1,
    Extracting = 2,
    Converting = 3,
    Verifying = 4
}

public enum QueueItemTerminalOutcome
{
    Healthy = 0,
    Extracted = 1,
    Moved = 2,
    SkippedExists = 3
}

public enum QueueItemFailureKind
{
    Failed = 0,
    Cancelled = 1,
    PasswordRequired = 2,
    FailedConvert = 3,
    FailedVerify = 4,
    FailedExtract = 5,
    Unsupported = 6,
    SourceUnreadable = 7
}

public enum QueueItemArtifactKind
{
    OutputFile = 0,
    LogFile = 1,
    TempWorkingDirectory = 2
}
