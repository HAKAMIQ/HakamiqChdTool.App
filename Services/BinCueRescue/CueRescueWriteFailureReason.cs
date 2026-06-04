namespace HakamiqChdTool.App.Services.BinCueRescue;

internal enum CueRescueWriteFailureReason
{
    None = 0,
    PlanIsNull = 1,
    PlanNotUsable = 2,
    EmptyPlan = 3,
    AmbiguousPlan = 4,
    MissingTrackFile = 5,
    UnsupportedTrack = 6,
    UnsafeTempRoot = 7,
    CouldNotCreateWorkspace = 8,
    InvalidCueContent = 9,
    AtomicWriteFailed = 10
}