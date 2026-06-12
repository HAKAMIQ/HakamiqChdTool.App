using System;

namespace HakamiqChdTool.App.Services.MediaInputPolicy;

internal enum MediaInputDecisionAction
{
    AcceptOriginal = 0,
    RedirectToCue = 1,
    AcceptStandaloneBinWithTemporaryCue = 2,
    Block = 3
}

internal sealed record MediaInputDecision(
    MediaInputDecisionAction Action,
    string OriginalPath,
    string EffectivePath,
    string MessageKey,
    string? WarningKey,
    string? PlatformName,
    int PlatformConfidence)
{
    public bool IsAccepted => Action != MediaInputDecisionAction.Block;

    public bool IsBlocked => Action == MediaInputDecisionAction.Block;

    public bool IsRedirectedToCue => Action == MediaInputDecisionAction.RedirectToCue;

    public bool RequiresTemporaryCue => Action == MediaInputDecisionAction.AcceptStandaloneBinWithTemporaryCue;

    public static MediaInputDecision Accept(string path) => new(
        MediaInputDecisionAction.AcceptOriginal,
        path,
        path,
        string.Empty,
        null,
        null,
        0);

    public static MediaInputDecision RedirectToCue(string originalPath, string cuePath) => new(
        MediaInputDecisionAction.RedirectToCue,
        originalPath,
        cuePath,
        MediaInputPolicy.BinRedirectedToCueMessageKey,
        null,
        null,
        0);

    public static MediaInputDecision AcceptTemporaryCue(
        string path,
        string platformName,
        int platformConfidence,
        string warningKey) => new(
        MediaInputDecisionAction.AcceptStandaloneBinWithTemporaryCue,
        path,
        path,
        MediaInputPolicy.BinTemporaryCueAcceptedMessageKey,
        warningKey,
        platformName,
        Math.Clamp(platformConfidence, 0, 100));

    public static MediaInputDecision Block(string path, string messageKey) => new(
        MediaInputDecisionAction.Block,
        path,
        path,
        string.IsNullOrWhiteSpace(messageKey) ? MediaInputPolicy.UnsupportedMessageKey : messageKey,
        null,
        null,
        0);
}
