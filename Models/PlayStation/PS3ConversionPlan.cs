namespace HakamiqChdTool.App.Models.PlayStation;

public sealed record PS3ConversionPlan(
    bool CanProceed,
    string Pipeline,
    bool RequiresTemporaryIso);
