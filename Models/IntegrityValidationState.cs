namespace HakamiqChdTool.App.Models;

public enum IntegrityValidationState
{
    None = 0,
    Validating = 1,
    Verified = 2,
    Failed = 3,
    NoDat = 4,
    Unsupported = 5,
    Error = 6,
    NoDirectRedump = 7,
    NoRedumpMatch = 8
}
