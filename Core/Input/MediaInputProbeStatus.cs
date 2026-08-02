namespace HakamiqChdTool.App.Core.Input;

public enum MediaInputProbeStatus
{
    NotRequired = 0,
    MagicConfirmed = 1,
    HeaderEnvelopeValid = 2,
    HeaderMismatch = 3,
    HeaderTruncated = 4,
    UnsupportedVersion = 5,
    InvalidHeaderLength = 6,
    ProbeUnavailable = 7
}
