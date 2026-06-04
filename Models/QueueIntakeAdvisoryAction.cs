namespace HakamiqChdTool.App.Models;

public enum QueueIntakeAdvisoryAction
{
    Unknown = 0,
    Convert = 1,
    Extract = 2,
    Verify = 3,
    Skip = 4,
    Warn = 5,
    Block = 6,
    ReportOnly = 7
}