namespace HakamiqChdTool.App.Core.Verification;

// Systems whose disc verification policy is known. Anything else is Unknown and fails closed.
public enum DiscSystem
{
    Unknown = 0,
    PlayStation = 1,
    PlayStation2 = 2,
    PlayStationPortable = 3,
    PlayStation3 = 4,
    Saturn = 5,
    SegaCd = 6,
    PcEngineCd = 7,
    NeoGeoCd = 8,
    ThreeDo = 9,
    JaguarCd = 10,
    Dreamcast = 11,
    GameCube = 12,
    Wii = 13,
    Xbox = 14,
    Xbox360 = 15
}

// What the bytes on hand structurally are, independent of any console name.
public enum DiscStructure
{
    Unknown = 0,
    CdTracks = 1,
    GdRomTracks = 2,
    SingleImage2048 = 3,
    NintendoOpticalImage = 4,
    DolphinCompressedImage = 5,
    Ps3DecryptedImage = 6,
    Ps3DecryptedFolder = 7
}

// How the image is stored on disk.
public enum DiscContainer
{
    Unknown = 0,
    Iso = 1,
    Gcm = 2,
    CueBin = 3,
    Gdi = 4,
    Chd = 5,
    Cso = 6,
    DolphinCompressed = 7,
    Ps3Folder = 8,
    Archive = 9
}

// Verification semantics: decides which Redump representation and proof are meaningful.
public enum MediaSemantics
{
    Unknown = 0,
    TrackStructuredCd = 1,
    GdRomTrackLayout = 2,
    SingleRawImage2048 = 3,
    Ps3EncryptedDisc = 4,
    Ps3DecryptedIso = 5,
    DolphinOpticalImage = 6
}

public enum RedumpFastPath
{
    None = 0,
    RawSha1AndLogicalSize = 1
}

public enum FastPathState
{
    NotAllowed = 0,
    CandidateUnproven = 1,
    Proven = 2
}

public enum FullVerificationPath
{
    None = 0,
    DirectImageHash = 1,
    TrackFileHash = 2,
    ExtractCdTracks = 3,
    ExtractGdRomTracks = 4,
    ExtractDvdImage = 5,
    DecompressCsoImage = 6,
    Ps3IrdFileCheck = 7,
    PlatformNativeTool = 8,
    IntegrityOnly = 9
}

// Whether the container is usable in the platform's reference emulator. This is never
// evidence of Redump provenance.
public enum StorageCompatibility
{
    Unknown = 0,
    Native = 1,
    Supported = 2,
    Experimental = 3,
    Unsupported = 4
}

// Redump provenance that can be claimed. Independent of StorageCompatibility and of
// chdman integrity checks.
public enum RedumpVerification
{
    NotVerified = 0,
    NotApplicable = 1,
    TrackVerified = 2,
    RawSha1AndSize = 3,
    Full = 4
}

public sealed record DiscVerificationPolicy(
    DiscSystem System,
    MediaSemantics Semantics,
    RedumpFastPath FastPath,
    FastPathState FastPathState,
    FullVerificationPath FullPath,
    StorageCompatibility Storage,
    RedumpVerification MaxProof,
    bool FailClosed)
{
    public bool CanUseFastPath =>
        FastPath != RedumpFastPath.None && FastPathState == FastPathState.Proven;
}

// A file that was hashed for verification (a track, or the whole image).
public sealed record HashedDiscFile(long SizeBytes, string Md5, string Sha1, string Crc32);

// A Redump reference ROM entry for the matched disc.
public sealed record RedumpReferenceRom(string RomName, long? SizeBytes, string? Md5, string? Sha1, string? Crc32);
