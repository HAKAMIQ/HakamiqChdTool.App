namespace HakamiqChdTool.App.Services.RedumpCatalog;

internal enum RedumpArtifactKind
{
    Unknown = -1,
    Datfile = 0,
    Cuesheet = 1,
    Gdi = 2,
    Subchannel = 3,
    DiscKey = 4,
    BiosDatfile = 5,
    SerialVersionDatfile = 6,
    TrackOnlyDatfile = 7,
    Unsupported = 8
}