namespace HakamiqChdTool.App.Core.Verification;

public sealed record GameHashMatch(
    GameVerificationStatus Status,
    GameHashEntry? MatchedEntry,
    GameHashAlgorithm? MatchedAlgorithm,
    string MessageKey,
    double Confidence,
    IReadOnlyList<string> Warnings);
