namespace HakamiqChdTool.App.Models.PlayStation.BluRayAnalysis;

public sealed class BluRayAnalysisProgress
{
    public BluRayAnalysisProgress(BluRayAnalysisStage stage, double percent, string technicalDetail = "")
    {
        Stage = stage;
        Percent = Math.Clamp(percent, 0, 100);
        TechnicalDetail = technicalDetail ?? string.Empty;
    }

    public BluRayAnalysisStage Stage { get; }

    public double Percent { get; }

    public string TechnicalDetail { get; }
}
