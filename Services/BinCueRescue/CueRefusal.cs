namespace HakamiqChdTool.App.Services.BinCueRescue;

internal enum BinCueRescueRefusalReason
{
    NoSyncProof = 1,
    AmbiguousOrder = 2,
    MixedSectorSizes = 3,
    Cooked2048ShouldBeIso = 4,
    MultipleDataTracksConflict = 5,
    InsufficientSectorEvidence = 6,
    UnsupportedPlatform = 9,
    NonStandardSectorLayout = 10,
    PathHintOnly = 11
}