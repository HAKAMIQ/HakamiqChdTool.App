using System.Collections.Generic;

namespace HakamiqChdTool.App.Services.BinCueRescue;

internal enum BinSectorProbeReasonCode
{
    None = 0,
    FileDoesNotExist = 1,
    FileIsEmpty = 2,
    LengthNotDivisibleBySupportedSectorSize = 3,
    UnexpectedRawModeByteObserved = 4,
    MixedRawDataModeBytesObserved = 5,
    Raw2352Mode1Observed = 6,
    Raw2352Mode2Observed = 7,
    OnlyZeroModeRawSyncObserved = 8,
    Raw2352AudioCandidate = 9,
    InsufficientRaw2352Evidence = 10,
    Iso9660PrimaryVolumeDescriptorObserved = 11,
    Cooked2048WithoutConfirmedIso9660Pvd = 12
}

internal sealed record BinSectorProbeResult(
    string Path,
    long Length,
    int? SectorSize,
    BinTrackKind Kind,
    byte? ModeByte,
    int SyncObservedAt,
    IReadOnlyList<BinSectorProbeReasonCode> ConfidenceReasons)
{
    public bool IsRaw2352Data => Kind is BinTrackKind.Raw2352Mode1 or BinTrackKind.Raw2352Mode2;

    public bool IsAudioCandidate => Kind == BinTrackKind.Raw2352AudioCandidate;

    public bool IsCookedData => Kind == BinTrackKind.Cooked2048Data;

    public bool IsUsableForCueRescue => IsRaw2352Data || IsAudioCandidate;
}