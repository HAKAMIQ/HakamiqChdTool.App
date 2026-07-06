using HakamiqChdTool.App.Models;

namespace HakamiqChdTool.App.Services;

public enum RedumpSourceFormat
{
    Unknown = 0,
    Iso = 1,
    BinCue = 2,
    Gdi = 3,
    Chd = 4,
    Cso = 5,
    Rvz = 6,
    Wbfs = 7,
    Nkit = 8,
    Archive = 9,
    Ps3JbFolder = 10,
    DecryptedPs3Iso = 11,
    Gcm = 12
}

public enum RedumpNormalizedFormat
{
    Original = 0,
    Iso = 1,
    CueBin = 2,
    Gdi = 3,
    RawIsoGcm = 4,
    IrdOnly = 5,
    Unsupported = 6,
    Gcm = 7
}

public enum RedumpV2ResultState
{
    Verified = 0,
    VerifiedNormalized = 1,
    NoRedumpMatch = 2,
    Failed = 3,
    NoDatabase = 4,
    Unsupported = 5,
    Error = 6,

    // Compatibility alias for clearer Redump wording.
    DatabaseMissing = 4,

    // Explicit policy states.
    NotRedumpOriginalLayout = 7,
    IrdFilesVerified = 8,
    IrdFilesFailed = 9,
    NormalizationFailed = 10,
    UnsupportedSourceFormat = 11,
    AmbiguousMultiTrack = 12
}

public sealed record RedumpV2ScanOptions(
    string ChdmanPath,
    AppSettings? Settings = null);

public sealed record RedumpSourceClassification(
    string InputPath,
    RedumpSourceFormat SourceFormat,
    bool IsDirectory,
    string DisplayName,
    long SourceBytes);

public sealed record RedumpV2ScanResult(
    RedumpV2ResultState State,
    string OriginalPath,
    RedumpSourceFormat SourceFormat,
    RedumpNormalizedFormat NormalizedFormat,
    bool UsedTemporaryNormalization,
    long RequiredTempSpaceBytes,
    string StatusMessageKey,
    string DetailMessageKey,
    IReadOnlyList<object?> DetailArgs,
    IReadOnlyList<DeepHashFileDigest> HashedFiles,
    IReadOnlyList<DeepHashMatch> Matches,
    IReadOnlyList<string> UnmatchedFileNames,
    string SuggestedStandardName = "",
    string MatchedSystemName = "",
    string MatchedGameName = "",
    int MatchedFileCount = 0,
    int HashedFileCount = 0,
    string FailureCode = "");