namespace HakamiqChdTool.App.Core.Verification;

public enum GameVerificationStatus
{
    Unknown,
    Match,
    Mismatch,
    PartialMatch,
    NotInDatabase,
    MultiTrackRequiresPerTrackVerification,
    UnsupportedDatabaseFormat
}
