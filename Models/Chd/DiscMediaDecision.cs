namespace HakamiqChdTool.App.Models.Chd;

public enum DiscMediaClassification
{
    Unknown = 0,
    CdRom = 1,
    DvdRom = 2
}

public enum DiscMediaDecisionSource
{
    Unknown = 0,
    Structure = 1,
    SizeFallback = 2
}

public enum DiscMediaStructure
{
    None = 0,
    Ps1 = 1,
    Psp = 2,
    Ps3 = 3
}

public readonly record struct DiscMediaDecision(
    DiscMediaClassification Kind,
    DiscMediaDecisionSource Source,
    DiscMediaStructure Structure);

public enum DescriptorCreateCdEvidence
{
    None = 0,
    Cue = 1,
    Gdi = 2
}
