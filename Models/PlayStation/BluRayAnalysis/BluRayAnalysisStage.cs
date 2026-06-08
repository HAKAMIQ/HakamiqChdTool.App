namespace HakamiqChdTool.App.Models.PlayStation.BluRayAnalysis;

public enum BluRayAnalysisStage
{
    Idle = 0,
    Preparing,
    ReadingDiscHeader,
    CheckingDiscStructure,
    SearchingMetadata,
    EstimatingCompression,
    BuildingReport,
    Completed
}
