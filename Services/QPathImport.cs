using HakamiqChdTool.App.Models;

namespace HakamiqChdTool.App.Services;

public sealed record QueuePathImportResult(
    IReadOnlyList<string> SupportedPaths,
    IntakeBatchSummary Summary);
