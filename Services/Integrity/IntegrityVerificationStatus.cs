namespace HakamiqChdTool.App.Services.Integrity;

public enum IntegrityVerificationStatus
{
    Passed = 0,
    FileMissing = 1,
    ManifestMissing = 2,
    ManifestInvalid = 3,
    ManifestEntryMissing = 4,
    HashMismatch = 5,
    VerificationFailed = 6
}