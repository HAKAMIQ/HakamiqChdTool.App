namespace HakamiqChdTool.App.Models.PlayStation.BluRayAnalysis;

public sealed record BluRayPs3DiscMetadata(
    string DiscTitleId,
    string SfbTitleId,
    string ParamSfoTitleId,
    string Title,
    string AppVersion,
    string SystemVersion,
    bool IsSectorSizeAligned,
    bool HasUdfAnchor,
    bool HasPlayStation3Magic,
    bool HasPs3DiscSfb,
    bool HasParamSfo,
    bool HasEbootBin)
{
    public string PreferredTitleId => !string.IsNullOrWhiteSpace(ParamSfoTitleId)
        ? ParamSfoTitleId
        : !string.IsNullOrWhiteSpace(SfbTitleId)
            ? SfbTitleId
            : DiscTitleId;

    public bool LooksLikePs3Disc => HasPlayStation3Magic || HasPs3DiscSfb || HasParamSfo;

    public bool LooksLikeBluRayStyleIso => HasUdfAnchor || LooksLikePs3Disc;

    public bool HasMinimumRequiredStructure => IsSectorSizeAligned
        && HasUdfAnchor
        && LooksLikePs3Disc;

    public bool HasCompletePs3Metadata => !string.IsNullOrWhiteSpace(PreferredTitleId)
        && !string.IsNullOrWhiteSpace(Title)
        && HasParamSfo;

    public bool IsStructurallyConsistent => HasMinimumRequiredStructure && HasEbootBin;
}