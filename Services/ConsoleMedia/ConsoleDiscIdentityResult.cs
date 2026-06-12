using System;

namespace HakamiqChdTool.App.Services.ConsoleMedia;

internal enum ConsoleDiscIdentityEvidenceKind
{
    None = 0,
    BinaryContent = 1,
    RawSerial = 2,
    PathHint = 3
}

internal sealed record ConsoleDiscIdentityResult(
    string PlatformName,
    int Confidence,
    string ReasonKey,
    string Evidence,
    ConsoleDiscIdentityEvidenceKind EvidenceKind)
{
    public bool IsIdentified =>
        !string.IsNullOrWhiteSpace(PlatformName)
        && Confidence >= 60;

    public bool IsPathHintOnly =>
        EvidenceKind == ConsoleDiscIdentityEvidenceKind.PathHint;

    public bool HasOperationalEvidence =>
        EvidenceKind is ConsoleDiscIdentityEvidenceKind.BinaryContent or ConsoleDiscIdentityEvidenceKind.RawSerial;

    public static ConsoleDiscIdentityResult Unknown(string reasonKey = "LocConsoleDiscIdentity_Unknown") =>
        new(string.Empty, 0, reasonKey, string.Empty, ConsoleDiscIdentityEvidenceKind.None);

    public static ConsoleDiscIdentityResult Create(
        string platformName,
        int confidence,
        string reasonKey,
        string? evidence = null,
        ConsoleDiscIdentityEvidenceKind evidenceKind = ConsoleDiscIdentityEvidenceKind.BinaryContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonKey);

        return new ConsoleDiscIdentityResult(
            platformName.Trim(),
            Math.Clamp(confidence, 0, 100),
            reasonKey.Trim(),
            string.IsNullOrWhiteSpace(evidence) ? string.Empty : evidence.Trim(),
            evidenceKind);
    }
}
